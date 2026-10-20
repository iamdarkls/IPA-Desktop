using System;
using System.Linq;
using System.Text.RegularExpressions;
namespace IpaDesktop
{
    /// <summary>Блокирует только подтверждённую несовместимость; неизвестные сведения не подменяет догадками.</summary>
    public static class AppCompatibility
    {
        static Version Parse(string value)
        {
            if (!Regex.IsMatch(value ?? "", @"^\d+(\.\d+){0,3}$")) return null;
            var parts = value.Split('.').ToList(); while (parts.Count < 4) parts.Add("0");
            Version result; return Version.TryParse(String.Join(".", parts), out result) ? result : null;
        }
        public static string BlockReason(IpaDetails app, string ios)
        {
            if (app.Platforms != null && app.Platforms.Length > 0 && !app.Platforms.Any(x => String.Equals(x, "iPhoneOS", StringComparison.OrdinalIgnoreCase)))
                return "Пакет предназначен для другой платформы: " + String.Join(", ", app.Platforms) + ". Для iPhone нужен iPhoneOS.";
            var minimum = Parse(app.MinimumOS); var current = Parse(ios);
            if (minimum != null && current != null && minimum > current)
                return "Приложению требуется iOS " + app.MinimumOS + ", на iPhone установлена " + ios + ". Выберите более старую версию приложения.";
            return null;
        }
    }
}
