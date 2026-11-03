using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop;

public sealed class PurchaseHistory
{
    readonly IAppToolClient client;
    public PurchaseHistory(IAppToolClient client) => this.client=client;
    public async Task<List<LibraryApp>> LoadAsync(int? year, bool all, CancellationToken ct)
    {
        if(year.HasValue && (year<2008||year>DateTime.Now.Year))throw new ArgumentException("Выберите год от 2008 до текущего.");
        var args=new List<string>{"list-purchases"};
        if(all)args.Add("--all");else if(year.HasValue)args.AddRange(new[]{"--range",year+"-all"});
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(ct);stop.CancelAfter(TimeSpan.FromMinutes(5));
        var result=await client.RunAsync(args.ToArray(),stop.Token,false);ToolResponse.Ensure(result);
        return Parse(result.Output);
    }
    public static List<LibraryApp> Parse(string output)
    {
        var data=ToolResponse.Object(output);var values=ToolResponse.Get(data,"apps") as object[] ?? throw new InvalidOperationException("В истории покупок отсутствует список apps.");
        return values.OfType<Dictionary<string,object>>().Select(row=>new LibraryApp{
            Name=Convert.ToString(ToolResponse.Get(row,"name")),ID=Convert.ToString(ToolResponse.Get(row,"id")),
            Kind=Convert.ToString(ToolResponse.Get(row,"kind")),Date=Convert.ToString(ToolResponse.Get(row,"purchaseDate"))
        }).Where(x=>!String.IsNullOrWhiteSpace(x.ID)).GroupBy(x=>(x.ID,x.Kind)).Select(g=>g.OrderByDescending(x=>x.Date).First()).ToList();
    }
}
