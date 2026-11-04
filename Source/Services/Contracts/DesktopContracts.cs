using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop;

public interface ISettingsStore { State Settings { get; set; } void Save(); }
public sealed record WorkspacePaths(string Root, string Downloads);
public interface IAccountSession { Task<string> GetEmailAsync(CancellationToken cancellation); Task RevokeAsync(CancellationToken cancellation); }
public interface IAccountProtection { bool RequiresLogin { get; } bool SafetyStopped { get; } string SafetyStatus { get; } void AcknowledgeRecovery(CancellationToken cancellation); }
public interface IAppCatalog { Task<List<AppRow>> SearchAsync(string query, CancellationToken cancellation); }
public interface IAppVersions { Task<List<AppVersionRow>> ListAsync(string appId, CancellationToken cancellation); Task ReadAsync(string appId, AppVersionRow row, CancellationToken cancellation); }
public interface IPurchaseHistory { Task<List<LibraryApp>> LoadAsync(int? year, bool all, CancellationToken cancellation); }
public interface IApplicationDownloads { Task DownloadAsync(DownloadItem item, CancellationToken cancellation); Task<string> DownloadFileAsync(DownloadItem item, CancellationToken cancellation); }
public interface ILocalApps { Task<List<LocalRow>> ListAsync(CancellationToken cancellation = default); Task ImportAsync(IEnumerable<string> paths, CancellationToken cancellation); void Delete(string path); }
public interface IAppSets
{
    List<AppSet> Load(); void Update(string originalName, string name, IEnumerable<AppRow> apps);
    void AddApps(string name, IEnumerable<AppRow> apps); AppRow[] ResolveApps(AppSet set);
    AppRow[] ResolveMany(IEnumerable<AppSet> sets); void DeleteMany(IEnumerable<string> names);
}
public interface IDeviceService
{
    Task<List<DeviceRow>> FindAsync(bool wifi, CancellationToken cancellation, Action<string> report);
    Task<Dictionary<string, string>> ValidateReadyAsync(DeviceRow device, CancellationToken cancellation);
    Task<Dictionary<string, string>> EnsureReadyAsync(DeviceRow device, Action<DeviceConnectionProgress> progress, CancellationToken cancellation);
    Task<List<LibraryApp>> ApplicationsAsync(DeviceRow device, CancellationToken cancellation);
    Task InstallAsync(DeviceRow device, LocalRow file, Action<DeviceConnectionProgress> progress, CancellationToken cancellation);
    Task<string> DetailsAsync(DeviceRow device, CancellationToken cancellation);
}
public interface IAppleSupport { Task<AppleSupportReport> CheckAsync(CancellationToken cancellation); }
public interface INetworkDiagnostics { Task<string> DiagnoseAsync(CancellationToken cancellation); }
public interface IToolUpdates
{
    Task<Release> Check(CancellationToken cancellation); Task Install(Release release, CancellationToken cancellation);
    Task<Release> CheckDeviceTools(CancellationToken cancellation); bool DeviceToolsCurrent(Release release);
    Task InstallDeviceTools(Release release, CancellationToken cancellation); void RollbackDeviceTools(); void Rollback();
}
public interface IGuiUpdates
{
    Task<Release> Check(CancellationToken cancellation); Task<string> Prepare(Release release, CancellationToken cancellation, Action<double> progress); void Launch(string stage);
}
public interface IApplicationEvents { IDisposable Subscribe(Action<string> log, Action<double> progress); }
