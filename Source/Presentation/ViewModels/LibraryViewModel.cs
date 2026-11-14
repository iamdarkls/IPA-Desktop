using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop;

public sealed partial class LibraryViewModel : FeatureViewModel
{
    static string Key(DeviceRow phone) => phone.ID + "|" + phone.Network;
    readonly IDeviceService devices;
    readonly ILocalApps localApps;
    readonly IPurchaseHistory purchases;
    readonly IAccountProtection protection;
    public LibraryViewModel(DesktopViewModel desktop, IDeviceService devices, ILocalApps localApps, IPurchaseHistory purchases, IAccountProtection protection) : base(desktop) { this.devices = devices; this.localApps = localApps; this.purchases = purchases; this.protection = protection; }
    internal void Initialize()
    {
        DeviceAppsCommand = Command(OpenInstalled);
        PurchaseHistoryCommand = AsyncCommand(OpenPurchases);
        NetworkSettingsCommand = AsyncCommand(async () => await desktop.Interaction.ShowNetwork(desktop.CreateNetworkSettings()));
        desktop.Queue.CanVerify = row => desktop.CanUseBatchAccount(row.Batch) && row.Kind == "Установка" && row.State is QueueItemState.Uncertain or QueueItemState.Interrupted && desktop.CanVerifyPhone(row.Batch.TargetKey);
        desktop.Queue.VerifyRequested = row => _ = desktop.RunAsyncCommand(() => Verify(row));
    }
    DeviceRow Selected() => desktop.Files.DeviceList.SelectedItem as DeviceRow;
    void OpenInstalled()
    {
        var phone = Selected() ?? throw new InvalidOperationException("Выберите iPhone."); var key = Key(phone);
        var browser = new AppBrowserViewModel(desktop, "Приложения · " + phone.DisplayLabel, false, () => Selected() != null && Key(Selected()) == key);
        browser.AddRequested = rows => _ = desktop.RunAsyncCommand(() => { desktop.AddLibraryApps(rows); return Task.CompletedTask; });
        browser.ReloadRequested = () => Work("Чтение приложений iPhone…", async ct =>
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, browser.Token);
            try
            {
                await devices.ValidateReadyAsync(phone, stop.Token);
                var apps = await devices.ApplicationsAsync(phone, stop.Token);
                browser.SetRows(apps, "Приложений: " + apps.Count + ". Placeholder может быть сгруженным или устанавливаемым приложением; для их чтения нужен комплект 1.0.5+. Записи без App Store ID нельзя автоматически добавить в загрузку.");
            }
            catch (DeviceConnectionException error) { desktop.ReportDeviceFailure(phone, error); throw; }
        });
        desktop.Interaction.ShowLibrary(browser); browser.ReloadRequested();
    }
    async Task OpenPurchases()
    {
        string email = null;
        if (!await desktop.RunWorkAsync("Проверка текущего аккаунта…", async ct => { email = await desktop.RequireAccountAsync(ct); })) return;
        var account = desktop.AccountKey;
        var browser = new AppBrowserViewModel(desktop, "История покупок · " + email, true, () => desktop.AccountKey == account);
        browser.AddRequested = rows => _ = desktop.RunAsyncCommand(() => { desktop.AddLibraryApps(rows); return Task.CompletedTask; });
        browser.ReloadRequested = () => _ = desktop.RunAsyncCommand(async () =>
        {
            if (browser.AllYears && !await desktop.Interaction.Confirm("Вся история", "Запросить все годы истории покупок? Это может занять несколько минут. При ошибке аккаунта или сети запрос остановится.")) return;
            await desktop.RunWorkAsync("Чтение истории покупок…", async ct =>
            {
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, browser.Token);
                await desktop.RequireAccountAsync(stop.Token, account);
                var apps = await purchases.LoadAsync(browser.SelectedYear, browser.AllYears, stop.Token);
                browser.SetRows(apps, "Записей: " + apps.Count + ". Доступность загрузки зависит от Apple. История этого аккаунта хранится только в открытом окне.");
            });
        });
        desktop.Interaction.ShowLibrary(browser); browser.ReloadRequested();
    }
    async Task Verify(QueueItemViewModel row)
    {
        if (row == null || !desktop.Queue.CanVerify(row)) return;
        var phone = Selected();
        await desktop.RunWorkAsync("Проверка результата на iPhone…", async ct =>
        {
            var available = await localApps.ListAsync(ct); var file = available.FirstOrDefault(x => String.Equals(x.Path, row.Seed.Key, StringComparison.OrdinalIgnoreCase));
            if (file == null) throw new IOException("Исходного IPA нет в папке программы. Без него результат нельзя достоверно сравнить.");
            var info = new FileInfo(file.Path);
            if (row.Seed.FileSize > 0 && (info.Length != row.Seed.FileSize || info.LastWriteTimeUtc.Ticks != row.Seed.ModifiedTicks)) throw new IOException("IPA изменился после создания очереди. Добавьте его заново.");
            var expected = IpaMetadata.ReadDetails(file.Path);
            await devices.ValidateReadyAsync(phone, ct); var apps = await devices.ApplicationsAsync(phone, ct);
            var installed = apps.FirstOrDefault(x => String.Equals(x.BundleID, expected.BundleID, StringComparison.OrdinalIgnoreCase));
            var difference = DeviceApplications.Compare(expected, installed);
            if (difference != null) { await desktop.Interaction.ShowText("Проверка результата", difference + "\nОчередь не изменена."); return; }
            if (await desktop.Interaction.Confirm("Версия найдена на iPhone", file.DisplayName + " · " + expected.Version + " · сборка " + expected.Build + "\nНужная версия присутствует на выбранном iPhone. Пропустить повторную установку этого IPA?"))
            { desktop.Queue.AcceptInstalled(row); desktop.ResolveVerifiedFile(file.Path); }
        });
    }
    public override void RefreshActions()
    {
        DeviceAppsEnabled = !busy && Selected() != null;
        PurchaseHistoryEnabled = !busy && !protection.RequiresLogin;
        NetworkSettingsEnabled = !busy;
        desktop.ResetQueueHistoryEnabled = !busy;
    }
}
