using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace IpaDesktop;

public sealed partial class Desktop
{
    QueueArchiveStore queueStore;
    bool queueStoreBlocked;
    DateTime lastQueueSave;
    internal static string AccountHint(string email)
    {
        int at=email.IndexOf('@');
        return at>0 ? email.Substring(0,Math.Min(2,at))+"…"+email.Substring(at) : "…"+email.Substring(Math.Max(0,email.Length-4));
    }
    internal void SetAccountIdentity(string email)
    {
        Model.AccountKey=String.IsNullOrWhiteSpace(email)?null:Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant())));
        Model.Queue.Refresh();
    }
    internal async Task<string> RequireAccountAsync(CancellationToken ct, string expected = null)
    {
        var result=await engine.Tool(new[]{"auth","info"},ct,false);Engine.Ensure(result);
        var data=Engine.Object(result.Output);var email=Convert.ToString(Engine.Get(data,"email"));
        if(String.IsNullOrWhiteSpace(email)||!Object.Equals(Engine.Get(data,"success"),true))throw new InvalidOperationException("Проверьте аккаунт и выполните вход перед продолжением.");
        SetAccountIdentity(email);RegisterPrivate(email);
        if(expected!=null && expected!=Model.AccountKey)throw new InvalidOperationException("Очередь создана для другого Apple Account. Войдите в исходный аккаунт или создайте новую очередь.");
        return email;
    }
    void ConfigureQueueStorage()
    {
        queueStore=new QueueArchiveStore(engine.Root);
        try { Model.Queue.Restore(queueStore.Load(),(batch,keys)=>RunAsyncCommand(()=>ResumeStoredBatch(batch,keys))); }
        catch(Exception error)
        {
            queueStoreBlocked=true;Model.Queue.StorageStatus="История не прочитана; файл сохранён. «Сбросить историю» создаст его резервную копию.";Log("История очереди не загружена: "+error.Message);
        }
        Model.Queue.PersistRequested=SaveQueue;
        OnAsync("ResetQueueHistory",async()=>
        {
            if(busy||!await AppDialogs.Confirm(w,"Сброс истории","Сохранить резервную копию истории и очистить список операций? IPA останутся на диске."))return;
            queueStore.Backup();queueStoreBlocked=false;Model.Queue.ClearHistory();SaveQueue();
        });
    }
    void SaveQueue()
    {
        if(queueStore==null||queueStoreBlocked)return;
        lastQueueSave=DateTime.UtcNow;
        try { queueStore.Save(Model.Queue);Model.Queue.StorageStatus="История сохранена. После перезапуска подтвердите аккаунт или связь с исходным iPhone."; }
        catch(Exception) { Model.Queue.StorageStatus="Не удалось сохранить историю. Проверьте место и доступ к папке программы."; }
    }
    internal async Task ResumeStoredBatch(QueueBatchViewModel batch,IReadOnlyCollection<string> keys)
    {
        if(busy)return;
        var seeds=batch.Items.Where(x=>keys.Contains(x.Seed.Key)&&x.State!=QueueItemState.Succeeded).Select(x=>x.Seed).ToList();
        if(batch.Kind=="Скачивание")
        {
            if(batch.AccountKey==null)throw new InvalidOperationException("У старой очереди нет подтверждённого аккаунта. Добавьте приложения в новую очередь.");
            search.StartDownloads(seeds.Select(seed=>
            {
                var parts=seed.Key.Split('@');
                if(parts.Length!=2||!AppSetStore.ValidId(parts[0])||(parts[1]!="latest"&&!AppSetStore.ValidId(parts[1])))throw new InvalidDataException("Некорректная сохранённая загрузка.");
                return new DownloadItem{ID=parts[0],VersionID=parts[1]=="latest"?null:parts[1],Name=seed.Name};
            }).ToList(),false,batch);
        }
        else
        {
            var available=await engine.LocalFiles.ListAsync();var rows=new List<LocalRow>();
            foreach(var seed in seeds)
            {
                var row=available.FirstOrDefault(x=>String.Equals(x.Path,seed.Key,StringComparison.OrdinalIgnoreCase));
                if(row==null)throw new IOException("IPA больше нет в папке приложения: "+seed.Name+". Добавьте файл заново.");
                var file=new FileInfo(row.Path);
                if(seed.FileSize>0&&(file.Length!=seed.FileSize||file.LastWriteTimeUtc.Ticks!=seed.ModifiedTicks))throw new IOException("IPA изменился после сохранения очереди: "+seed.Name+". Добавьте его заново.");
                rows.Add(row);
            }
            files.StartInstall(rows,batch);
        }
    }
    internal void StartSetInstallation(List<LocalRow> rows,string target) => files.StartInstall(rows,null,target);
    internal bool CanVerifyPhone(string key) => files.CanResume(key);
    internal void ReportDeviceFailure(DeviceRow phone,DeviceConnectionException error)=>files.MarkConnectionLost(phone,error);
    internal void ResolveVerifiedFile(string path)=>files.ResolveVerified(path);
    internal void AddLibraryApps(IEnumerable<LibraryApp> rows)
    {
        var selected=rows.ToList();if(selected.Count==0||selected.Any(x=>!x.Downloadable))throw new InvalidOperationException("У части выбранных записей нет подтверждённого App Store ID приложения iOS. Выберите приложения с ID.");
        AddSetApps(selected.Select(x=>new AppRow{ID=x.ID,Name=x.Name}));
    }
}
