using System;
using System.Collections.Generic;
using System.Globalization;

namespace IpaDesktop;

public sealed record DeviceSnapshot(string Model, string Ios, ulong? FreeBytes, string InstallationService)
{
    public string Summary => Model + " · iOS " + Ios + " · " + (FreeBytes.HasValue ? "свободно " + FormatBytes(FreeBytes.Value) : "свободное место неизвестно");
    public static DeviceSnapshot From(Dictionary<string, string> values, ulong? free = null, string service = "Служба установки ещё не проверена") =>
        new(values.GetValueOrDefault("ProductType", "Модель неизвестна"), values.GetValueOrDefault("ProductVersion", "неизвестна"), free, service);
    public static string FormatBytes(ulong bytes) => bytes >= 1073741824 ? (bytes / 1073741824d).ToString("0.0", CultureInfo.CurrentCulture) + " ГиБ" :
        bytes >= 1048576 ? (bytes / 1048576d).ToString("0.0", CultureInfo.CurrentCulture) + " МиБ" :
        bytes >= 1024 ? (bytes / 1024d).ToString("0.0", CultureInfo.CurrentCulture) + " КиБ" : bytes + " Б";
    public static bool TryFreeBytes(string value, out ulong bytes) => ulong.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out bytes);
}
