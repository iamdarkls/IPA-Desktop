using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Linq;
using System.Collections.Generic;

namespace IpaDesktop
{
    // Reads account identity from the IPA itself; never substitutes the current session.
    /// <summary>Читает ограниченные XML и binary plist из IPA; никогда не подставляет текущий Apple Account.</summary>
    public static class IpaMetadata
    {
        const int Limit = 1024 * 1024;
        /// <summary>Читает Apple Account из корневых метаданных IPA; возвращает понятный статус при отсутствии данных.</summary>
        public static string ReadAccount(string path)
        {
            try
            {
                using (var zip = ZipFile.OpenRead(path))
                {
                    return ReadAccount(zip);
                }
            } catch (IOException)
            {
                return "Не удалось прочитать";
            } catch (InvalidDataException)
            {
                return "Не удалось прочитать";
            } catch (UnauthorizedAccessException)
            {
                return "Нет доступа";
            } catch (XmlException)
            {
                return "Не удалось прочитать";
            } catch (ArgumentException)
            {
                return "Не удалось прочитать";
            } catch (OverflowException)
            {
                return "Не удалось прочитать";
            }
        }
        /// <summary>Читает аккаунт из уже открытого архива, не перечитывая его центральный каталог.</summary>
        static string ReadAccount(ZipArchive zip)
        {
            try
            {
                var entry = zip.GetEntry("iTunesMetadata.plist");
                if (entry == null) return "Не указан";
                if (entry.Length > Limit) return "Метаданные слишком велики";
                return ParseAccount(EntryBytes(entry));
            }
            catch (UnauthorizedAccessException) { return "Нет доступа"; }
            catch (Exception e)
            {
                if (!(e is IOException || e is InvalidDataException || e is XmlException || e is ArgumentException || e is OverflowException)) throw;
                return "Не удалось прочитать";
            }
        }
        /// <summary>Ограничивает объём чтения метаданных до выделения и заполнения буфера.</summary>
        static byte[] EntryBytes(ZipArchiveEntry entry)
        {
            if (entry == null) return null;
            if (entry.Length > Limit) throw new InvalidDataException();
            using (var source = entry.Open()) using (var target = new MemoryStream())
            {
                var buffer = new byte[8192]; int n;
                while ((n = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (target.Length + n > Limit) throw new InvalidDataException();
                    target.Write(buffer, 0, n);
                }
                return target.ToArray();
            }
        }
        static string Value(byte[] bytes, params string[] keys)
        {
            if (bytes == null) return null;
            if (bytes.Length > Limit) throw new InvalidDataException();
            if (bytes.Length >= 8 && Encoding.ASCII.GetString(bytes, 0, 8) == "bplist00")
            {
                var plist = new BinaryPlist(bytes);
                foreach (var key in keys) { var value = plist.Value(key); if (!String.IsNullOrWhiteSpace(value)) return value; }
                return null;
            }
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = Limit };
            using (var stream = new MemoryStream(bytes)) using (var reader = XmlReader.Create(stream, settings))
            {
                var doc = new XmlDocument { XmlResolver = null }; doc.Load(reader);
                var root = doc.SelectSingleNode("/plist/dict");
                if (root == null) return null;
                foreach (var key in keys)
                    for (var node = root.FirstChild; node != null; node = node.NextSibling)
                    {
                        if (node.Name != "key" || !String.Equals(node.InnerText, key, StringComparison.OrdinalIgnoreCase)) continue;
                        var next = node.NextSibling;
                        while (next != null && next.NodeType != XmlNodeType.Element) next = next.NextSibling;
                        if (next != null && (next.Name == "string" || next.Name == "integer") && !String.IsNullOrWhiteSpace(next.InnerText)) return next.InnerText;
                    }
                return null;
            }
        }
        static string[] Platforms(byte[] bytes)
        {
            if (bytes == null) return new string[0];
            if (bytes.Length >= 8 && Encoding.ASCII.GetString(bytes, 0, 8) == "bplist00") return new BinaryPlist(bytes).Strings("CFBundleSupportedPlatforms");
            using (var stream = new MemoryStream(bytes))
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = Limit }))
            {
                var doc = new XmlDocument { XmlResolver = null }; doc.Load(reader);
                var node = doc.SelectSingleNode("/plist/dict/key[.='CFBundleSupportedPlatforms']/following-sibling::*[1][self::array]");
                return node == null ? new string[0] : node.ChildNodes.Cast<XmlNode>().Where(x => x.Name == "string").Select(x => Field(x.InnerText)).Where(x => x != null).Distinct().ToArray();
            }
        }
        static string Field(string text)
        {
            if (String.IsNullOrWhiteSpace(text) || text.Length > 320 || text.Any(Char.IsControl)) return null;
            return text.Trim();
        }
        /// <summary>Читает корневые метаданные и Info.plist основного приложения; расширения и Watch-приложения исключены.</summary>
        public static IpaDetails ReadDetails(string path)
        {
            var details = new IpaDetails { Account = "Не удалось прочитать" };
            try
            {
                using (var zip = ZipFile.OpenRead(path))
                {
                    details.Account = ReadAccount(zip);
                    if (zip.Entries.Count > 30000) return details;
                    var metadata = EntryBytes(zip.GetEntry("iTunesMetadata.plist"));
                    details.AppStoreID = Field(Value(metadata, "itemId", "itemID", "softwareVersionItemId"));
                    details.BundleID = Field(Value(metadata, "softwareVersionBundleId", "bundleId"));
                    details.AppName = Field(Value(metadata, "itemName"));
                    details.Version = Field(Value(metadata, "bundleShortVersionString", "bundleVersion"));
                    var apps = zip.Entries.Where(x => System.Text.RegularExpressions.Regex.IsMatch(x.FullName, @"^Payload/[^/]+\.app/Info\.plist$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).Take(2).ToArray();
                    if (apps.Length == 1)
                    {
                        var info = EntryBytes(apps[0]);
                        details.BundleID = Field(Value(info, "CFBundleIdentifier")) ?? details.BundleID;
                        details.AppName = Field(Value(info, "CFBundleDisplayName", "CFBundleName")) ?? details.AppName;
                        details.Version = Field(Value(info, "CFBundleShortVersionString")) ?? details.Version;
                        details.Build = Field(Value(info, "CFBundleVersion"));
                        details.MinimumOS = Field(Value(info, "MinimumOSVersion"));
                        details.Platforms = Platforms(info);
                        if (details.Platforms.Length == 0)
                        {
                            string platform = Field(Value(info, "DTPlatformName"));
                            if (platform != null) details.Platforms = new[] { String.Equals(platform, "iphoneos", StringComparison.OrdinalIgnoreCase) ? "iPhoneOS" : platform };
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (e is UnauthorizedAccessException) details.Account = "Нет доступа";
                if (!(e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is XmlException || e is ArgumentException || e is OverflowException)) throw;
            }
            return details;
        }
        /// <summary>Распознаёт только поле appleId, не принимая название приложения за аккаунт.</summary>
        static bool IsAccountKey(string key)
        {
            return String.Equals(key, "appleId", StringComparison.OrdinalIgnoreCase);
        }
        static string Clean(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "Не указан";
            value = value.Trim();
            if (value.Length > 320) return "Не удалось прочитать";
            foreach (char c in value) if (Char.IsControl(c)) return "Не удалось прочитать";
            return value;
        }
        /// <summary>Разбирает XML или binary plist с ограничением размера и отключённым внешним XML-резолвером.</summary>
        public static string ParseAccount(byte[] bytes)
        {
            if (bytes.Length > Limit) throw new InvalidDataException();
            if (bytes.Length >= 8 && Encoding.ASCII.GetString(bytes, 0, 8) == "bplist00") return Clean(new BinaryPlist(bytes).Account());
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = Limit
            };
            using (var stream = new MemoryStream(bytes)) using (var reader = XmlReader.Create(stream, settings))
            {
                var doc = new XmlDocument
                {
                    XmlResolver = null
                };
                doc.Load(reader);
                var root = doc.SelectSingleNode("/plist/dict");
                if (root == null) return "Не указан";
                for (var node = root.FirstChild; node != null; node = node.NextSibling)
                {
                    if (node.Name != "key" || !IsAccountKey(node.InnerText)) continue;
                    var value = node.NextSibling;
                    while (value != null && value.NodeType != XmlNodeType.Element) value = value.NextSibling;
                    if (value != null && value.Name == "string") return Clean(value.InnerText);
                }
                return "Не указан";
            }
        }
        // Only the root dictionary's string keys/values are needed. Other plist types are not decoded.
        sealed class BinaryPlist
        {
            readonly byte[] data;
            readonly int offsetSize, refSize, objects, top, table;
            void Bounds(int start, int size, int end)
            {
                if (start < 0 || size < 0 || start > end - size) throw new InvalidDataException("Invalid binary plist bounds");
            }
            ulong Number(int start, int size)
            {
                if (size < 1 || size > 8) throw new InvalidDataException();
                Bounds(start, size, data.Length);
                ulong value = 0;
                for (int i = 0; i < size; i++) value = (value << 8) | data[start + i];
                return value;
            }
            int IntNumber(int start, int size)
            {
                var n = Number(start, size);
                if (n > Int32.MaxValue) throw new InvalidDataException();
                return(int) n;
            }
            public BinaryPlist(byte[] bytes)
            {
                data = bytes;
                if (data.Length<40) throw new InvalidDataException();
                int trailer = data.Length - 32;
                offsetSize = data[trailer + 6];
                refSize = data[trailer + 7];
                objects = IntNumber(trailer + 8, 8);
                top = IntNumber(trailer + 16, 8);
                table = IntNumber(trailer + 24, 8);
                if (objects<1 || objects>Limit || top>= objects || table < 8 || offsetSize < 1 || offsetSize > 8 || refSize < 1 || refSize > 8) throw new InvalidDataException();
                Bounds(table, checked(objects * offsetSize), trailer);
            }
            int Offset(int id)
            {
                if (id < 0 || id >= objects) throw new InvalidDataException();
                int offset = IntNumber(table + id * offsetSize, offsetSize);
                if (offset < 8 || offset >= table) throw new InvalidDataException();
                return offset;
            }
            int Length(int marker, ref int pos)
            {
                int n = marker & 15;
                if (n < 15) return n;
                Bounds(pos, 1, table);
                int m = data[pos++];
                if ((m >> 4) != 1 || (m & 15) > 3) throw new InvalidDataException();
                int size = 1 <<(m & 15);
                Bounds(pos, size, table);
                n = IntNumber(pos, size);
                pos += size;
                return n;
            }
            string Text(int id)
            {
                int pos = Offset(id);
                int marker = data[pos++];
                int type = marker >> 4;
                if (type != 5 && type != 6) return null;
                int n = Length(marker, ref pos);
                int size = checked(n * (type == 6 ? 2 : 1));
                Bounds(pos, size, table);
                return type == 6 ? Encoding.BigEndianUnicode.GetString(data, pos, size) : Encoding.ASCII.GetString(data, pos, size);
            }
            public string[] Strings(string wantedKey)
            {
                int pos = Offset(top), marker = data[pos++];
                if ((marker >> 4) != 13) return new string[0];
                int count = Length(marker, ref pos); Bounds(pos, checked(count * refSize * 2), table);
                for (int i = 0; i < count; i++)
                {
                    if (!String.Equals(Text(IntNumber(pos + i * refSize, refSize)), wantedKey, StringComparison.OrdinalIgnoreCase)) continue;
                    int offset = Offset(IntNumber(pos + (count + i) * refSize, refSize));
                    int arrayMarker = data[offset++];
                    if ((arrayMarker >> 4) != 10) return new string[0];
                    int length = Length(arrayMarker, ref offset); Bounds(offset, checked(length * refSize), table);
                    var values = new List<string>();
                    for (int j = 0; j < length; j++)
                    {
                        string value = Field(Text(IntNumber(offset + j * refSize, refSize)));
                        if (value != null) values.Add(value);
                    }
                    return values.Distinct().ToArray();
                }
                return new string[0];
            }
            public string Account() { return Value("appleId", false); }
            public string Value(string wantedKey, bool allowInteger = true)
            {
                int pos = Offset(top);
                int marker = data[pos++];
                if ((marker >> 4) != 13) return null;
                int count = Length(marker, ref pos);
                Bounds(pos, checked(count * refSize * 2), table);
                for (int i = 0; i < count; i++)
                {
                    var key = Text(IntNumber(pos + i * refSize, refSize));
                    if (String.Equals(key, wantedKey, StringComparison.OrdinalIgnoreCase))
                    {
                        int id = IntNumber(pos + (count + i) * refSize, refSize);
                        int offset = Offset(id), kind = data[offset];
                        if (allowInteger && (kind >> 4) == 1 && (kind & 15) <= 3)
                        {
                            int size = 1 << (kind & 15); Bounds(offset + 1, size, table);
                            return Number(offset + 1, size).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        }
                        return Text(id);
                    }
                }
                return null;
            }
        }
    }
}
