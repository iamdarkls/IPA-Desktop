using System.Reflection;
using IpaDesktop;
using Xunit;
namespace IpaDesktop.Tests;

public sealed class MvvmTests
{
 [Fact] public void LocalFileTablesPreserveSelectionAcrossMetadataRefresh()
 {
  var grid=new SelectionViewModel<LocalRow>();var old=new LocalRow{Path="/ipa/first.ipa",Version="1"};
  grid.ReplacePreservingSelection(new[]{old,new LocalRow{Path="/ipa/gone.ipa"}}, x=>x.Path);
  grid.SelectedItems.Add(old);
  var refreshed=new LocalRow{Path=old.Path,Version="2"};
  grid.ReplacePreservingSelection(new[]{refreshed}, x=>x.Path);Assert.Same(refreshed,Assert.Single(grid.SelectedItems.Cast<LocalRow>()));
  grid.ReplacePreservingSelection(new[]{refreshed}, x=>x.Path);Assert.Single(grid.SelectedItems.Cast<LocalRow>());
  grid.ReplacePreservingSelection(Array.Empty<LocalRow>(), x=>x.Path);Assert.Empty(grid.SelectedItems);
 }

    static Engine Backend(string root)
    {
        var engine = new Engine(root, new RecordingProcessRunner(), new InstalledAppleSupportProbe());
        engine.Settings.Current = "test"; engine.Settings.CheckGuiUpdates = engine.Settings.AutoUpdate = engine.Settings.AutoUpdateDeviceTools = engine.Settings.SoundsEnabled = false;
        var bins = Path.Combine(root, "Versions/test/" + RuntimePlatform.ToolFolder); Directory.CreateDirectory(bins);
        foreach (var name in new[] { "ipatool.exe", "idevice_id.exe", "ideviceinfo.exe", "ideviceinstaller.exe", "idevicepair.exe" }) File.WriteAllText(Path.Combine(bins, RuntimePlatform.ToolName(name)), "fixture");
        return engine;
    }
    [Fact] public void ViewModelsDoNotExposeOrStoreAvaloniaObjects()
    {
        static bool UiType(Type type) => type.Namespace?.StartsWith("Avalonia", StringComparison.Ordinal) == true ||
            type.HasElementType && UiType(type.GetElementType()) || type.IsGenericType && type.GetGenericArguments().Any(UiType);
        var types = typeof(DesktopViewModel).Assembly.GetTypes().Where(x => typeof(ObservableObject).IsAssignableFrom(x));
        foreach (var type in types)
        {
            Assert.False(UiType(type.BaseType), type.FullName);
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) Assert.False(UiType(field.FieldType), type.Name + "." + field.Name);
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Assert.False(UiType(method.ReturnType), type.Name + "." + method.Name);
                Assert.DoesNotContain(method.GetParameters(), p => UiType(p.ParameterType));
            }
        }
    }
    [Fact] public async Task SearchSelectionAndCancellationWorkWithoutCreatingAWindow()
    {
        using var folder = new TempFolder(); using var model = new DesktopViewModel(Backend(folder.Path), new NoWindowInteraction());
        model.Search.Query = "123"; await model.Search.SearchCommand.ExecuteAsync(); await model.ActiveOperation;
        Assert.Equal("123", Assert.Single(model.Search.Apps.Items).ID);
        await model.Search.AddChosenCommand.ExecuteAsync();
        Assert.Equal("123", Assert.Single(model.Search.ChosenAppsOptions).ID);
        var pending = model.RunWorkAsync("Wait", ct => Task.Delay(Timeout.Infinite, ct));
        Assert.True(model.IsBusy); Assert.False(model.Search.DownloadCommand.CanExecute(null));
        model.CancelCommand.Execute(null); Assert.False(await pending);
        Assert.False(model.IsBusy); Assert.True(model.Search.DownloadCommand.CanExecute(null));
        Assert.Single(model.Search.ChosenAppsOptions);
    }
    [Fact] public async Task FailedNetworkSettingsValidationPreservesConfigurationAndSafetyState()
    {
        using var folder = new TempFolder(); var engine = Backend(folder.Path);
        using var model = new DesktopViewModel(engine, new NoWindowInteraction());
        var before = engine.Settings.Copy(); var safety = engine.AccountRequests.SafetyStatus;
        using var network = new NetworkSettingsViewModel(model, engine) { Mode = 2, Address = "not-a-proxy" };
        await network.SaveCommand.ExecuteAsync();
        Assert.Equal(before.ProxyMode, engine.Settings.ProxyMode); Assert.Equal(before.ProxyUrl, engine.Settings.ProxyUrl);
        Assert.Equal(safety, engine.AccountRequests.SafetyStatus); Assert.NotEmpty(network.Status);
    }
    [Fact] public async Task AsyncCommandRejectsReentryAndRoutesAnExceptionOnce()
    {
        var gate = new TaskCompletionSource(); int calls = 0, errors = 0;
        var command = new OperationCommand(async _ => { calls++; await gate.Task; throw new IOException("fixture"); }, _ => errors++);
        var first = command.ExecuteAsync(); var duplicate = command.ExecuteAsync(); Assert.Same(first, duplicate);
        Assert.Equal(1, calls); Assert.False(command.CanExecute(null));
        gate.SetResult(); await first;
        Assert.Equal(1, errors); Assert.True(command.CanExecute(null));
    }
}

internal sealed class NoWindowInteraction : IDesktopInteraction
{
    sealed class Timer : IDisposable { public void Dispose() { } }
    public void Post(Action action) => action();
    public IDisposable Repeat(TimeSpan interval, Action tick) => new Timer();
    public void CloseMenus() { }
    public void Close() { }
    public void ShowJournal(DesktopViewModel model) { }
    public Task ShowText(string title, string details) => Task.CompletedTask;
    public Task<bool> Confirm(string title, string message) => Task.FromResult(false);
    public Task<string> Login(IAppleLoginService service, CancellationToken cancellation) => Task.FromResult<string>(null);
    public Task<IReadOnlyList<string>> SelectVersions(string name, string id, List<AppVersionRow> rows, AppVersions service, CancellationToken cancellation) => Task.FromResult<IReadOnlyList<string>>(null);
    public Task<bool> EditSet(string name, IEnumerable<AppRow> apps, Action<string, IEnumerable<AppRow>> save) => Task.FromResult(false);
    public void ShowLibrary(AppBrowserViewModel model) => model.Dispose();
    public Task ShowNetwork(NetworkSettingsViewModel model) { model.Dispose(); return Task.CompletedTask; }
    public Task ShowIpa(LocalRow row) => Task.CompletedTask;
    public Task<string[]> ImportFiles() => Task.FromResult(Array.Empty<string>());
    public Task<bool> ExportText(string suggestedName, string text) => Task.FromResult(false);
}
