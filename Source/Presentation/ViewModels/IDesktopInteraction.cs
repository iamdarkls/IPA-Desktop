using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop;

/// <summary>Platform UI boundary. ViewModels do not know about windows, controls or dispatchers.</summary>
public interface IDesktopInteraction
{
    void Post(Action action);
    IDisposable Repeat(TimeSpan interval, Action tick);
    void CloseMenus();
    void Close();
    void ShowJournal(DesktopViewModel model);
    Task ShowText(string title, string details);
    Task<bool> Confirm(string title, string message);
    Task<string> Login(IAppleLoginService service, CancellationToken cancellation);
    Task<IReadOnlyList<string>> SelectVersions(string name, string id, List<AppVersionRow> rows, AppVersions service, CancellationToken cancellation);
    Task<bool> EditSet(string name, IEnumerable<AppRow> apps, Action<string, IEnumerable<AppRow>> save);
    void ShowLibrary(AppBrowserViewModel model);
    Task ShowNetwork(NetworkSettingsViewModel model);
    Task ShowIpa(LocalRow row);
    Task<string[]> ImportFiles();
    Task<bool> ExportText(string suggestedName, string text);
}
