using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop;

/// <summary>One route for native authentication/download commands and network diagnostics. Never changes process-global variables.</summary>
public sealed class ProxyConfiguration
{
    readonly Func<State> settings;
    public ProxyConfiguration(Func<State> settings) => this.settings = settings;
    public static readonly string[] Variables = { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy", "NO_PROXY", "no_proxy" };
    public static Uri Validate(string value, bool allowCredentials = false)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https" or "socks5" or "socks5h") || String.IsNullOrEmpty(uri.Host) ||
            uri.Port is < 1 or > 65535 || uri.AbsolutePath is not ("" or "/") || uri.Query.Length>0 || uri.Fragment.Length>0 ||
            (!allowCredentials && uri.UserInfo.Length>0))
            throw new ArgumentException("Укажите http://host:port, https://host:port или socks5://host:port без логина, пароля и пути.");
        return uri;
    }
    static bool InheritedProxy => Array.Exists(Variables,name=>!name.Equals("NO_PROXY",StringComparison.OrdinalIgnoreCase)&&!String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)));
    static bool AppleBypassed()
    {
        var values=Environment.GetEnvironmentVariable("NO_PROXY")??Environment.GetEnvironmentVariable("no_proxy")??"";
        foreach(var entry in values.Split(','))
        {
            var host=entry.Trim().Split(':')[0].TrimStart('.');
            if(host=="*"||host=="buy.itunes.apple.com"||("buy.itunes.apple.com").EndsWith("."+host,StringComparison.OrdinalIgnoreCase)&&host.Length>0)return true;
        }
        return false;
    }
    public Uri Resolve()
    {
        var state = settings();
        if (state.ProxyMode == "Direct") return null;
        if (state.ProxyMode == "Custom") return Validate(state.ProxyUrl);
        foreach (var key in new[]{"HTTPS_PROXY","https_proxy","ALL_PROXY","all_proxy"})
        {
            var value=Environment.GetEnvironmentVariable(key);
            if (!String.IsNullOrWhiteSpace(value)) return AppleBypassed()?null:Validate(value,true);
        }
        if(InheritedProxy)return null;
#pragma warning disable SYSLIB0014
        var destination = new Uri("https://buy.itunes.apple.com/");
        var proxy = WebRequest.GetSystemWebProxy();
        var result = proxy.GetProxy(destination);
#pragma warning restore SYSLIB0014
        return result == null || result == destination ? null : Validate(result.AbsoluteUri,true);
    }
    public static string Describe(Uri uri) => uri == null ? "Напрямую" : uri.Scheme+"://"+uri.Host+":"+uri.Port;
    public Dictionary<string,string> EnvironmentChanges()
    {
        if(settings().ProxyMode is not ("Direct" or "Custom") && InheritedProxy)return new Dictionary<string,string>();
        var route=Resolve();
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var name in Variables) result[name]=null;
        if(route != null)
        {
            foreach(var name in new[]{"HTTP_PROXY","HTTPS_PROXY","ALL_PROXY"}) result[name]=route.AbsoluteUri;
        }
        else result["NO_PROXY"]="*";
        return result;
    }
    public static string EnvironmentBlock(IDictionary<string,string> changes)
    {
        var variables=new SortedDictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(DictionaryEntry entry in Environment.GetEnvironmentVariables()) variables[(string)entry.Key]=(string)entry.Value;
        foreach(var entry in changes) { if(entry.Value==null) variables.Remove(entry.Key);else variables[entry.Key]=entry.Value; }
        var lines=new List<string>();foreach(var entry in variables)lines.Add(entry.Key+"="+entry.Value);
        return String.Join('\0',lines)+"\0\0";
    }
    public async Task<string> DiagnoseAsync(CancellationToken ct)
    {
        var route=Resolve();using var stop=CancellationTokenSource.CreateLinkedTokenSource(ct);stop.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            if(route!=null)
            {
                using var tcp=new TcpClient();await tcp.ConnectAsync(route.Host,route.Port,stop.Token);
            }
            using var handler=new HttpClientHandler{UseProxy=route!=null,AllowAutoRedirect=false};
            if(route!=null)
            {
                var address=route.Scheme=="socks5h"?new UriBuilder(route){Scheme="socks5"}.Uri:route;
                handler.Proxy=new WebProxy(address);
            }
            using var client=new HttpClient(handler);using var request=new HttpRequestMessage(HttpMethod.Head,"https://buy.itunes.apple.com/");
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,stop.Token);
            return "Маршрут: "+Describe(route)+"\nTLS-соединение установлено. HTTP "+(int)response.StatusCode+". Для адреса без команды ответ 4xx допустим.\nПароль, токены и запрос авторизации не отправлялись. Это не проверка доступности аккаунта.";
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested) { throw new TimeoutException("Проверка сети не завершилась за 20 секунд. Проверьте прокси и интернет."); }
        catch(HttpRequestException) { throw new InvalidOperationException("Не удалось установить HTTPS-соединение. Проверьте прокси, сертификаты, дату системы и интернет. Проверка сертификата не отключалась."); }
        catch(SocketException) { throw new InvalidOperationException("Прокси недоступен по указанному адресу и порту. Проверьте, запущен ли VPN-клиент и включён ли в нём локальный прокси."); }
    }
}
