using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace IpaDesktop;

public static class DeviceApplications
{
    static Dictionary<string,XElement> Fields(XElement dictionary)
    {
        var result=new Dictionary<string,XElement>(StringComparer.Ordinal);
        var elements=dictionary.Elements().ToArray();
        for(var i=0;i+1<elements.Length;i++)if(elements[i].Name.LocalName=="key")result[elements[i].Value]=elements[++i];
        return result;
    }
    static string Text(Dictionary<string,XElement> fields,string key) => fields.TryGetValue(key,out var value) && value.Name.LocalName is "string" or "integer" ? value.Value : null;
    public static List<LibraryApp> Parse(string xml)
    {
        if(xml==null||xml.Length>15000000)throw new InvalidDataException("Список приложений слишком велик.");
        int start=xml.IndexOf("<?xml",StringComparison.Ordinal);if(start<0)start=xml.IndexOf("<plist",StringComparison.Ordinal);
        if(start<0)throw new InvalidDataException("Инструменты iPhone не вернули XML-список приложений. Обновите комплект в настройках.");
        using var reader=XmlReader.Create(new StringReader(xml.Substring(start)),new XmlReaderSettings{XmlResolver=null,DtdProcessing=DtdProcessing.Ignore,MaxCharactersInDocument=15000000});
        var doc=XDocument.Load(reader);var array=doc.Root?.Element("array");
        if(array==null)throw new InvalidDataException("Неизвестный формат списка приложений iPhone.");
        var rows=new List<LibraryApp>();
        foreach(var entry in array.Elements("dict"))
        {
            if(rows.Count>=10000)throw new InvalidDataException("Слишком много приложений в ответе устройства.");
            var fields=Fields(entry);var bundle=Text(fields,"CFBundleIdentifier");
            if(String.IsNullOrWhiteSpace(bundle))continue;
            string id=null;
            if(fields.TryGetValue("iTunesMetadata",out var metadata))
            {
                if(metadata.Name.LocalName=="dict") { var values=Fields(metadata);id=Text(values,"itemId")??Text(values,"itemID"); }
                else if(metadata.Name.LocalName=="data" && metadata.Value.Length<1400000)
                {
                    try{id=IpaMetadata.StoreID(Convert.FromBase64String(metadata.Value));}catch(Exception error) when(error is FormatException or InvalidDataException or XmlException or ArgumentException or OverflowException) { }
                }
            }
            rows.Add(new LibraryApp{BundleID=bundle,Name=Text(fields,"CFBundleDisplayName")??Text(fields,"CFBundleName")??bundle,
                Version=Text(fields,"CFBundleShortVersionString"),Build=Text(fields,"CFBundleVersion"),ID=AppSetStore.ValidId(id)?id:null,
                Placeholder=fields.TryGetValue("IsPlaceholder",out var flag) && (flag.Name.LocalName=="true"||flag.Value=="true"||flag.Value=="1")});
        }
        return rows.GroupBy(x=>x.BundleID,StringComparer.OrdinalIgnoreCase).Select(g=>g.OrderBy(x=>x.Placeholder).First()).OrderBy(x=>x.Name).ToList();
    }
    public static string Compare(IpaDetails expected, LibraryApp installed)
    {
        if(String.IsNullOrWhiteSpace(expected.BundleID) || (String.IsNullOrWhiteSpace(expected.Version) && String.IsNullOrWhiteSpace(expected.Build)))return "Недостаточно метаданных IPA для достоверного сравнения.";
        if(installed==null)return "Приложение с этим Bundle ID не найдено в ответе iPhone.";
        if(installed.Placeholder)return "iPhone помечает приложение как placeholder: оно может быть сгружено или не завершить установку.";
        if(!String.Equals(expected.BundleID,installed.BundleID,StringComparison.OrdinalIgnoreCase))return "Bundle ID не совпадает.";
        if(!String.IsNullOrWhiteSpace(expected.Version) && expected.Version!=installed.Version)return "Версия отличается: IPA "+expected.Version+", iPhone "+(installed.Version??"не указана")+".";
        if(!String.IsNullOrWhiteSpace(expected.Build) && expected.Build!=installed.Build)return "Номер сборки отличается: IPA "+expected.Build+", iPhone "+(installed.Build??"не указан")+".";
        return null;
    }
}
