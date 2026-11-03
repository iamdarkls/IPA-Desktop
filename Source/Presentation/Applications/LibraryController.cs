using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace IpaDesktop;

public sealed class LibraryController : WindowController
{
    static string Key(DeviceRow phone)=>phone.ID+"|"+phone.Network;
    public LibraryController(Desktop view,Engine engine):base(view,engine)
    {
        On("DeviceApps",OpenInstalled);
        OnAsync("PurchaseHistory",OpenPurchases);
        OnAsync("NetworkSettings",async()=>await new NetworkSettingsWindow(w,view,engine).ShowDialog(w));
        view.Model.Queue.CanVerify=row=>row.Kind=="Установка"&&row.State is QueueItemState.Uncertain or QueueItemState.Interrupted&&view.CanVerifyPhone(row.Batch.TargetKey);
        view.Model.Queue.VerifyRequested=row=>view.RunAsyncCommand(()=>Verify(row));
    }
    DeviceRow Selected()=>C<ComboBox>("DeviceList").SelectedItem as DeviceRow;
    void OpenInstalled()
    {
        var phone=Selected()??throw new InvalidOperationException("Выберите iPhone.");var key=Key(phone);
        var browser=new AppBrowserWindow(w,view.Model,"Приложения · "+phone.DisplayLabel,false,()=>Selected()!=null&&Key(Selected())==key);
        browser.AddRequested=rows=>view.RunAsyncCommand(()=>{view.AddLibraryApps(rows);return Task.CompletedTask;});
        browser.ReloadRequested=()=>Work("Чтение приложений iPhone…",async ct=>
        {
            using var stop=CancellationTokenSource.CreateLinkedTokenSource(ct,browser.Token);
            try
            {
                await engine.Devices.ValidateReadyAsync(phone,stop.Token);
                var apps=await engine.Devices.ApplicationsAsync(phone,stop.Token);
                browser.SetRows(apps,"Приложений: "+apps.Count+". Placeholder может быть сгруженным или устанавливаемым приложением; для их чтения нужен комплект 1.0.5+. Записи без App Store ID нельзя автоматически добавить в загрузку.");
            }
            catch(DeviceConnectionException error){view.ReportDeviceFailure(phone,error);throw;}
        });
        browser.Show(w);browser.ReloadRequested();
    }
    async Task OpenPurchases()
    {
        string email=null;
        if(!await view.RunWorkAsync("Проверка текущего аккаунта…",async ct=>{email=await view.RequireAccountAsync(ct);}))return;
        var account=view.Model.AccountKey;
        var browser=new AppBrowserWindow(w,view.Model,"История покупок · "+email,true,()=>view.Model.AccountKey==account);
        browser.AddRequested=rows=>view.RunAsyncCommand(()=>{view.AddLibraryApps(rows);return Task.CompletedTask;});
        browser.ReloadRequested=()=>view.RunAsyncCommand(async()=>
        {
            if(browser.AllYears&&!await AppDialogs.Confirm(browser,"Вся история","Запросить все годы истории покупок? Это может занять несколько минут. При ошибке аккаунта или сети запрос остановится."))return;
            await view.RunWorkAsync("Чтение истории покупок…",async ct=>
            {
                using var stop=CancellationTokenSource.CreateLinkedTokenSource(ct,browser.Token);
                await view.RequireAccountAsync(stop.Token,account);
                var apps=await engine.Purchases.LoadAsync(browser.SelectedYear,browser.AllYears,stop.Token);
                browser.SetRows(apps,"Записей: "+apps.Count+". Доступность загрузки зависит от Apple. История этого аккаунта хранится только в открытом окне.");
            });
        });
        browser.Show(w);browser.ReloadRequested();
    }
    async Task Verify(QueueItemViewModel row)
    {
        if(row==null||!view.Model.Queue.CanVerify(row))return;
        var phone=Selected();
        await view.RunWorkAsync("Проверка результата на iPhone…",async ct=>
        {
            var available=await engine.LocalFiles.ListAsync();var file=available.FirstOrDefault(x=>String.Equals(x.Path,row.Seed.Key,StringComparison.OrdinalIgnoreCase));
            if(file==null)throw new IOException("Исходного IPA нет в папке программы. Без него результат нельзя достоверно сравнить.");
            var info=new FileInfo(file.Path);
            if(row.Seed.FileSize>0&&(info.Length!=row.Seed.FileSize||info.LastWriteTimeUtc.Ticks!=row.Seed.ModifiedTicks))throw new IOException("IPA изменился после создания очереди. Добавьте его заново.");
            var expected=IpaMetadata.ReadDetails(file.Path);
            await engine.Devices.ValidateReadyAsync(phone,ct);var apps=await engine.Devices.ApplicationsAsync(phone,ct);
            var installed=apps.FirstOrDefault(x=>String.Equals(x.BundleID,expected.BundleID,StringComparison.OrdinalIgnoreCase));
            var difference=DeviceApplications.Compare(expected,installed);
            if(difference!=null) { await AppDialogs.ShowText(w,"Проверка результата",difference+"\nОчередь не изменена.");return; }
            if(await AppDialogs.Confirm(w,"Версия найдена на iPhone",file.DisplayName+" · "+expected.Version+" · сборка "+expected.Build+"\nНужная версия присутствует на выбранном iPhone. Пропустить повторную установку этого IPA?"))
            { view.Model.Queue.AcceptInstalled(row);view.ResolveVerifiedFile(file.Path); }
        });
    }
    public override void RefreshActions()
    {
        C<Button>("DeviceApps").IsEnabled=!busy&&Selected()!=null;
        C<Button>("PurchaseHistory").IsEnabled=!busy&&!engine.AccountRequests.RequiresLogin;
        C<Button>("NetworkSettings").IsEnabled=!busy;
        C<Button>("ResetQueueHistory").IsEnabled=!busy;
    }
}
