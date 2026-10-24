using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace IpaDesktop;

public sealed record DeviceConnectionProgress(string Title, string Message, bool WaitingForTrust, bool Ready = false);
/// <summary>One selected UDID, bounded repair, no installation until the handshake succeeds.</summary>
public sealed class DeviceReadiness
{
    readonly DeviceCommandClient client;
    readonly Func<TimeSpan,CancellationToken,Task> delay;
    readonly TimeSpan wait;
    public DeviceReadiness(DeviceCommandClient client, Func<TimeSpan,CancellationToken,Task> delay = null, TimeSpan? wait = null)
    { this.client=client; this.delay=delay ?? Task.Delay; this.wait=wait ?? TimeSpan.FromSeconds(90); }
    public async Task<Dictionary<string,string>> EnsureAsync(DeviceRow device, Action<DeviceConnectionProgress> progress, CancellationToken ct, bool forceRepair = false)
    {
        progress ??= delegate { };
        ct.ThrowIfCancellationRequested();
        progress(new("Проверка связи с «"+device.Name+"»", "Проверяем доверие и доступ к выбранному iPhone до отправки IPA.",false));
        try
        {
            if(!forceRepair)
            {
                var info=await CheckAsync(device,ct);
                progress(new("iPhone готов к установке", "Доверие и идентификатор выбранного iPhone проверены.",false,true));
                return info;
            }
            if(device.Network) throw new DeviceConnectionException(ErrorLocalizer.Describe("Invalid HostID"));
        }
        catch(DeviceConnectionException error) when(error.Failure.Code is FailureCode.Pairing or FailureCode.TrustPending or FailureCode.Locked)
        {
            if(device.Network) throw new DeviceConnectionException(new(FailureCode.Pairing,"Для восстановления доверия нужен USB","Сопряжение выбранного iPhone по Wi-Fi недействительно.","Подключите этот iPhone по USB, выберите его USB-подключение и подтвердите доверие. Затем при необходимости вернитесь к Wi-Fi."),error);
        }
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(wait);
        bool unpaired=false;
        try
        {
            while(true)
            {
                stop.Token.ThrowIfCancellationRequested();
                progress(new("Подтвердите доверие на «"+device.Name+"»", "Очередь приостановлена. Разблокируйте этот iPhone, нажмите «Доверять» и введите код-пароль на телефоне. После проверки установка продолжится автоматически. Можно отменить ожидание.",true));
                try
                {
                    try { await client.PairAsync(device,"pair",stop.Token); }
                    catch(DeviceConnectionException error) when(error.Failure.Code == FailureCode.Pairing && !unpaired)
                    {
                        // Try pair first. Only a confirmed stale record permits targeted unpair, once per repair.
                        unpaired=true;
                        try { await client.PairAsync(device,"unpair",stop.Token); }
                        catch(DeviceConnectionException stale) when(stale.Failure.Code == FailureCode.Pairing) { }
                        await client.PairAsync(device,"pair",stop.Token);
                    }
                    var info=await CheckAsync(device,stop.Token);
                    progress(new("Доверие подтверждено", "Связь с выбранным iPhone восстановлена. Продолжаем очередь с текущего IPA.",false,true));
                    return info;
                }
                catch(DeviceConnectionException error) when(error.Failure.Code is FailureCode.TrustPending or FailureCode.Locked or FailureCode.Pairing)
                {
                    await delay(TimeSpan.FromSeconds(2),stop.Token);
                }
            }
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested)
        {
            throw new DeviceConnectionException(new(FailureCode.TrustPending,"Подтверждение доверия не получено за 90 секунд","iPhone не завершил сопряжение; установка не продолжалась.","Разблокируйте iPhone, подтвердите «Доверять» и повторите проверку связи. Если запрос не появляется, переподключите USB и проверьте компоненты Apple."));
        }
    }
    async Task<Dictionary<string,string>> CheckAsync(DeviceRow device, CancellationToken ct)
    {
        if(!device.Network) await client.PairAsync(device,"validate",ct);
        return await client.ReadDeviceAsync(device,ct);
    }
}
