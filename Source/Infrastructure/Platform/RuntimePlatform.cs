using System;
using System.IO;
using System.Runtime.InteropServices;
namespace IpaDesktop;

public static class RuntimePlatform
{
    [System.Runtime.Versioning.SupportedOSPlatformGuard("windows")]
    public static bool Windows => OperatingSystem.IsWindows();
    public static string Architecture => RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";
    public static string Rid => (Windows?"win":OperatingSystem.IsMacOS()?"osx":"linux")+"-"+Architecture;
    public static string ToolFolder => Windows?"windows_amd64":Rid;
    public static string AssetSuffix => Windows?"Windows":(OperatingSystem.IsMacOS()?"macOS":"Linux")+"-"+Architecture;
    public static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IPA-Desktop");
    public static string ToolName(string name) => Windows?name:name.Replace("*.exe", "").Replace(".exe", "");
    public static void Executable(string file)
    {
        if(!Windows)File.SetUnixFileMode(file,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute|UnixFileMode.GroupRead|UnixFileMode.GroupExecute|UnixFileMode.OtherRead|UnixFileMode.OtherExecute);
    }
    public static void Bootstrap(SettingsStore store) => BundledTools.Initialize(store);
    public static void Trash(string path) => SystemIntegration.Trash(path);
}
