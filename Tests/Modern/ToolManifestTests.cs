using Xunit;
namespace IpaDesktop.Tests;

public sealed class ToolManifestTests
{
    [Fact]
    public void DeviceAssetSelectionPrefersExplicitEventsArchiveAndRetainsLegacyFallback()
    {
        Asset Asset(string suffix) => new() { name = "IPA-DeviceTools-1.0.8-Windows-x64" + suffix + ".zip", size = 10,
            digest = "sha256:" + new string('0', 64), browser_download_url = "https://github.com/iamdarkls/IPA-Desktop/releases/download/device-tools-v1.0.8/IPA-DeviceTools-1.0.8-Windows-x64" + suffix + ".zip" };
        var legacy = Asset(""); var events = Asset("-Events");
        var release = new Release { tag_name = "device-tools-v1.0.8", assets = new[] { legacy, events } };
        Assert.Same(events, EngineUpdater.SelectDeviceAsset(release));
        release.assets = new[] { legacy }; Assert.Same(legacy, EngineUpdater.SelectDeviceAsset(release));
        release.assets = new[] { events, events }; Assert.Throws<IOException>(() => EngineUpdater.SelectDeviceAsset(release));
    }
    [Fact]
    public void InstalledManifestUsesSelectedBundleAndRejectsUnknownSchema()
    {
        using var folder = new TempFolder(); var state = new State { Current = "fixture" };
        var path = Path.Combine(folder.Path, "Versions", "fixture", RuntimePlatform.Windows ? "" : RuntimePlatform.ToolFolder, "device-tools.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, "{\"schema\":1,\"version\":\"2.3.4\",\"components\":{\"libplist\":{\"version\":\"2.8.1\",\"revision\":\"fixture-revision\"}}}");
        Assert.Contains("2.3.4", DeviceToolManifest.InstalledDescription(folder.Path, state));
        Assert.Contains("libplist 2.8.1", DeviceToolManifest.Read(path).Components);
        File.WriteAllText(path, "{\"schema\":2}");
        Assert.Throws<InvalidDataException>(() => DeviceToolManifest.Read(path));
        state.Current = "previous";
        Assert.Contains("нет манифеста", DeviceToolManifest.InstalledDescription(folder.Path, state));
    }
    [Fact]
    public void SameVersionLegacyBundleStillNeedsTheEventsVariant()
    {
        using var folder = new TempFolder(); var store = new SettingsStore(folder.Path);
        store.Settings.DeviceCurrent = "fixture"; store.Settings.DeviceVersion = "device-tools-v1.0.8"; store.Settings.DeviceSource = EngineUpdater.DeviceRepository;
        var root = Path.Combine(folder.Path, "Versions", "fixture"); var bins = Path.Combine(root, "windows_amd64"); Directory.CreateDirectory(bins);
        foreach (var name in new[] { "idevice_id.exe", "ideviceinfo.exe", "ideviceinstaller.exe", "idevicepair.exe", "libimobiledevice-1.0.dll", "libplist-2.0.dll" }) File.WriteAllText(Path.Combine(bins, name), "fixture");
        var nameEvents = "IPA-DeviceTools-1.0.8-Windows-x64-Events.zip";
        var release = new Release { tag_name = "device-tools-v1.0.8", assets = new[] { new Asset { name = nameEvents, size = 10, digest = "sha256:" + new string('0', 64), browser_download_url = "https://github.com/iamdarkls/IPA-Desktop/releases/download/device-tools-v1.0.8/" + nameEvents } } };
        var updater = new EngineUpdater(store, new RecordingProcessRunner());
        Assert.False(updater.DeviceToolsCurrent(release));
        File.WriteAllText(Path.Combine(bins, "ipa-device-events.exe"), "fixture");
        File.WriteAllText(Path.Combine(root, "device-tools.json"), "{\"schema\":1,\"version\":\"1.0.8\",\"eventProtocol\":0,\"components\":{}}");
        Assert.False(updater.DeviceToolsCurrent(release));
        File.WriteAllText(Path.Combine(root, "device-tools.json"), "{\"schema\":1,\"version\":\"1.0.8\",\"eventProtocol\":1,\"components\":{}}");
        Assert.True(updater.DeviceToolsCurrent(release));
    }
}
