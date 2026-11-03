using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace IpaDesktop;

public sealed class QueueArchive
{
    public int Schema { get; set; } = 1;
    public List<QueueBatchArchive> Batches { get; set; } = new();
}
public sealed class QueueBatchArchive
{
    public string Kind { get; set; }
    public string TargetKey { get; set; }
    public string TargetLabel { get; set; }
    public string AccountKey { get; set; }
    public List<QueueItemArchive> Items { get; set; } = new();
}
public sealed class QueueItemArchive
{
    public QueueSeed Seed { get; set; }
    public QueueItemState State { get; set; }
    public double? Percent { get; set; }
    public string Detail { get; set; }
}
public sealed class QueueArchiveStore
{
    readonly string file;
    public QueueArchiveStore(string root) => file=Path.Combine(root,"operation-queue.json");
    public QueueArchive Load()
    {
        if(!File.Exists(file))return new();
        if(new FileInfo(file).Length>15000000)throw new InvalidDataException("Файл очереди слишком велик.");
        var archive=JsonCodec.Deserialize<QueueArchive>(File.ReadAllText(file));
        if(archive==null||archive.Schema!=1||archive.Batches==null||archive.Batches.Count>5000)throw new InvalidDataException("Неизвестный формат очереди.");
        int count=0;
        foreach(var batch in archive.Batches)
        {
            if(batch.Kind is not ("Установка" or "Скачивание")||batch.Items==null||batch.Items.Count>5000)throw new InvalidDataException("Повреждённая очередь.");
            foreach(var item in batch.Items)
            {
                if(++count>5000||item?.Seed==null||String.IsNullOrWhiteSpace(item.Seed.Key)||item.Seed.Key.Length>4096||!Enum.IsDefined(item.State)||
                    item.Percent is double percent && (!Double.IsFinite(percent)||percent<0||percent>100))throw new InvalidDataException("Повреждённый элемент очереди.");
            }
        }
        return archive;
    }
    public void Save(OperationQueueViewModel queue)
    {
        if(queue.Items.Count>5000)throw new IOException("В очереди больше 5000 записей. Уберите готовые элементы.");
        var archive=new QueueArchive{Batches=queue.Items.GroupBy(x=>x.Batch).Select(g=>new QueueBatchArchive{
            Kind=g.Key.Kind,TargetKey=g.Key.TargetKey,TargetLabel=g.Key.TargetLabel,AccountKey=g.Key.AccountKey,
            Items=g.Select(x=>new QueueItemArchive{Seed=x.Seed,State=x.State,Percent=x.ProgressText=="—"?null:x.Progress,Detail=x.Detail?.Substring(0,Math.Min(3000,x.Detail.Length))}).ToList()
        }).ToList()};
        var content=JsonCodec.Serialize(archive);if(content.Length>15000000)throw new IOException("История очереди слишком велика. Уберите готовые элементы.");
        AtomicFile.WriteUtf8(file,content);
    }
    public void Backup() { if(File.Exists(file))File.Move(file,file+".backup-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")); }
}
