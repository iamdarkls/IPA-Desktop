using System;
using System.Collections.Generic;
using System.Linq;

namespace IpaDesktop;

public sealed class AppSetInstallationPlan
{
    public List<LocalRow> Local { get; } = new();
    public List<AppRow> Missing { get; } = new();
    public static AppSetInstallationPlan Build(IEnumerable<AppRow> apps,IEnumerable<LocalRow> files,string account)
    {
        if(String.IsNullOrWhiteSpace(account))throw new InvalidOperationException("Для подготовки набора подтвердите текущий Apple Account.");
        var plan=new AppSetInstallationPlan();var available=files.ToList();
        foreach(var app in apps.GroupBy(x=>x.ID).Select(g=>g.First()))
        {
            var candidates=available.Where(x=>x.AppStoreID==app.ID&&String.Equals(x.AppleAccount,account,StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x=>Version.TryParse(x.Version,out var v)?v:new Version(0,0)).ThenBy(x=>x.Path,StringComparer.OrdinalIgnoreCase);
            var local=candidates.FirstOrDefault();
            if(local==null)plan.Missing.Add(app);else plan.Local.Add(local);
        }
        return plan;
    }
    public string Preview => "На диске: "+Local.Count+" · нужно скачать: "+Missing.Count+"\n\n"+
        String.Join("\n",Local.Take(10).Select(x=>"IPA: "+x.DisplayName+" · "+(x.Version??"версия неизвестна")).Concat(Missing.Take(10).Select(x=>"Скачать: "+x.Name)))+
        (Local.Count+Missing.Count>20?"\n… список сокращён":"");
}
