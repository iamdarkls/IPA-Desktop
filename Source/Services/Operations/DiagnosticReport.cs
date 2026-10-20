using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
namespace IpaDesktop
{
    /// <summary>Создаёт диагностический отчёт и удаляет известные секреты, почту и идентификаторы до записи.</summary>
    public static class DiagnosticReport
    {
        static string Replace(string text, string pattern, string replacement)
        {
            return Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        }
        /// <summary>Очищает текст по структурным шаблонам и известным приватным значениям до экспорта.</summary>
        public static string Redact(string text, IEnumerable<string> privateValues)
        {
            text = text ?? "";
            if (text.Length > 1000000) text = text.Substring(text.Length - 1000000);
            foreach (var value in (privateValues ?? new string[0]).Where(x => !String.IsNullOrWhiteSpace(x) && x.Length >= 3).Distinct().OrderByDescending(x => x.Length)) text = Replace(text, Regex.Escape(value), "[СКРЫТО]");
            text = Replace(text, @"(?m)^.*(?:Authorization|Cookie|Set-Cookie|X-Apple-[A-Za-z-]+)\s*[:=].*$", "[СКРЫТ ЗАГОЛОВОК]");
            text = Replace(text, @"(""(?:password[^""\r\n]*|token|access[_-]?token|refresh[_-]?token|auth[_-]?code|verification[_-]?code|passphrase|keychain-passphrase|secret|kbsync|dsid|directoryServicesID|hardwareID|udid|guid|cookie|authorization)""\s*:\s*)(?:""(?:\\.|[^""\\])*""|[^,}\r\n]+)", "$1\"[СКРЫТО]\"");
            text = Replace(text, @"(<key>\s*(?:password[^<]*|token|access[_-]?token|refresh[_-]?token|auth[_-]?code|passphrase|secret|kbsync|dsid|directoryServicesID|hardwareID|udid|guid|cookie)\s*</key>\s*<(?:string|data|integer)>)[\s\S]*?(</(?:string|data|integer)>)", "$1[СКРЫТО]$2");
            text = Replace(text, @"(?<!\w)(--(?:password|auth-code|keychain-passphrase|email|token|udid)|-p|-e|-u)(?:=|\s+)(?:""(?:\\.|[^""\\])*""|'[^']*'|\S+)", "$1 [СКРЫТО]");
            text = Replace(text, @"\b(password(?:Token)?|access[_-]?token|refresh[_-]?token|token|auth[_ -]?code|verification[_ -]?code|passphrase|keychain-passphrase|secret|kbsync|dsid|directoryServicesID|hardwareID|udid|guid)\s*[:=]\s*[^\r\n]+", "$1=[СКРЫТО]");
            text = Replace(text, @"[\p{L}\p{N}._%+\-]+@[\p{L}\p{N}.\-]+\.[\p{L}]{2,}", "[ПОЧТА]");
            text = Replace(text, @"\b[A-Z]:\\Users\\[^\\\r\n""<>]+", @"C:\Users\[ПОЛЬЗОВАТЕЛЬ]");
            text = Replace(text, @"/(?:Users|home)/[^/\s""']+", "/home/[ПОЛЬЗОВАТЕЛЬ]");
            text = Replace(text, @"\b[0-9a-f]{8}-[0-9a-f]{16}\b|\b[0-9a-f]{40}\b|\b(?:[0-9a-f]{2}:){5}[0-9a-f]{2}\b", "[ИДЕНТИФИКАТОР]");
            text = Replace(text, @"\beyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+", "[ТОКЕН]");
            return text;
        }
        /// <summary>Формирует сводку версий и журнал, затем очищает весь отчёт от чувствительных данных.</summary>
        public static string Create(string journal, string engineVersion, string toolsVersion, IEnumerable<string> privateValues)
        {
            var header = "IPA Desktop " + GuiUpdater.CurrentVersion + Environment.NewLine + "Windows: " + Environment.OSVersion.Version + Environment.NewLine + "ipatool: " + engineVersion + Environment.NewLine + "Device tools: " + toolsVersion + Environment.NewLine + "Created: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + Environment.NewLine;
            return Redact(header + journal, privateValues);
        }
    }
}
