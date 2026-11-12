using System;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop;

public sealed class ArchiveCacheViewModel : FeatureViewModel
{
    readonly ISettingsStore settings;
    readonly IpaArchiveCache cache;
    decimal? limit;
    string usage = "Место ещё не проверено";
    public decimal? Limit
    {
        get => limit;
        set { if (Set(ref limit, value)) RefreshCommands(); }
    }
    public string Usage { get => usage; private set => Set(ref usage, value); }
    public bool CanEdit => !busy;
    public OperationCommand SaveCommand { get; }
    public OperationCommand ClearCommand { get; }
    public ArchiveCacheViewModel(DesktopViewModel desktop, ISettingsStore settings, IpaArchiveCache cache) : base(desktop)
    {
        this.settings = settings;
        this.cache = cache;
        Limit = settings.Settings.IpaCacheGiB;
        SaveCommand = AsyncCommand(async () =>
        {
            if (Limit is not decimal value || value < 0 || value > IpaArchiveCache.MaxGiB || value != decimal.Truncate(value)) return;
            await desktop.RunWorkAsync("Настройка кэша IPA…", async ct =>
            {
                var previous = settings.Settings.IpaCacheGiB;
                settings.Settings.IpaCacheGiB = (int)value;
                try { settings.Save(); }
                catch { settings.Settings.IpaCacheGiB = previous; throw; }
                await cache.TrimAsync((int)value, ct);
            });
        });
        ClearCommand = AsyncCommand(async () =>
        {
            if (!await desktop.Interaction.Confirm("Очистить кэш IPA", "Удалить исходные архивы из кэша? Готовые IPA в библиотеке сохранятся.")) return;
            await desktop.RunWorkAsync("Очистка кэша IPA…", ct => cache.TrimAsync(0, ct));
        });
    }
    internal async Task RefreshUsage(CancellationToken ct)
    {
        try { Usage = "Занято: " + StorageAnalysis.Bytes(await cache.SizeAsync(ct)) + " · Лимит: " + settings.Settings.IpaCacheGiB + " ГиБ"; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        { Usage = "Не удалось прочитать кэш. Проверьте доступ к папке данных."; }
    }
    public override void ConfigureCommands()
    {
        SaveCommand.SetAvailability(() => CanEdit && Limit is >= 0 and <= IpaArchiveCache.MaxGiB && Limit != settings.Settings.IpaCacheGiB);
        ClearCommand.SetAvailability(() => CanEdit);
    }
    public override void RefreshActions() { Changed(nameof(CanEdit)); RefreshCommands(); }
    public override void RefreshCommands() { SaveCommand?.Refresh(); ClearCommand?.Refresh(); }
}
