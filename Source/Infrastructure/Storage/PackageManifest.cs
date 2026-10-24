using System;
using System.IO;
namespace IpaDesktop;
public static class PackageManifest
{
    public static readonly string[] Files = Read();
    static string[] Read()
    {
        using var stream=typeof(PackageManifest).Assembly.GetManifestResourceStream("IpaDesktop.PackageFiles.json") ?? throw new InvalidDataException("Package manifest is missing.");
        using var reader=new StreamReader(stream);
        return JsonCodec.Deserialize<string[]>(reader.ReadToEnd());
    }
}
