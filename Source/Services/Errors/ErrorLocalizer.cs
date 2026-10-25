using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
namespace IpaDesktop;

public enum FailureCode { Unknown, Pairing, TrustPending, TrustDenied, Locked, Disconnected, Transport, Verification, Signature, Storage, Incompatible, PasswordChanged, EmptyCatalog, Network, MissingTool, Credentials, Terms, AccountRestricted, Purchase, RateLimited }
public sealed record UserError(FailureCode Code, string Title, string Cause, string Remedy)
{
    public bool Connection => Code is FailureCode.Pairing or FailureCode.TrustPending or FailureCode.TrustDenied or FailureCode.Locked or FailureCode.Disconnected or FailureCode.Transport;
    public string Description => Title + "\nПричина: " + Cause + "\nЧто сделать: " + Remedy;
}
public static class ErrorLocalizer
{
    public static UserError Describe(Exception error) => error is AppleAccountException account ? account.Failure : error is DeviceConnectionException connection ? connection.Failure : Describe(error.Message, error);
    public static UserError Describe(string text, Exception error = null)
    {
        var s=(text ?? "").ToLowerInvariant();
        bool Lockdown(params int[] codes) { if(!s.Contains("lockdown")) return false; foreach(var code in codes) if(Regex.IsMatch(s,@"(?:error(?: code)?[ :]+|\()[ ]*"+code+@"\b")) return true; return false; }
        bool Has(params string[] fragments) { foreach(var f in fragments) if(s.Contains(f)) return true; return false; }
        if(Lockdown(-20,-21,-29,-31) || Has("invalidhostid","invalid hostid","invalid host id","invalid_host_id","not paired","invalid conf","invalid pair record"))
            return new(FailureCode.Pairing,"Нарушено доверие между iPhone и компьютером","Запись сопряжения отсутствует или больше не соответствует этому iPhone.","Подключите выбранный iPhone по USB, разблокируйте и подтвердите «Доверять». Программа попробует восстановить сопряжение.");
        if(Lockdown(-19) || Has("pairingdialogresponsepending","pairing dialog response pending","accept the trust","trust dialog on","pairing_dialog_response_pending"))
            return new(FailureCode.TrustPending,"Ожидается подтверждение доверия на iPhone","iPhone ещё не разрешил этому компьютеру доступ.","Разблокируйте iPhone, нажмите «Доверять» и введите код-пароль на телефоне. Очередь ждёт подтверждения.");
        if(Lockdown(-18) || Has("userdeniedpairing","user denied","denied the trust","user_denied_pairing"))
            return new(FailureCode.TrustDenied,"Доверие компьютеру отклонено","На iPhone отклонён запрос сопряжения.","Переподключите iPhone по USB и подтвердите «Доверять». Если запрос не появляется, проверьте настройки доверия на телефоне, затем повторите проверку связи.");
        if(Lockdown(-17,-35) || Has("passwordprotected","password protected","passcode is set","device is locked","device locked","password_protected"))
            return new(FailureCode.Locked,"Разблокируйте iPhone","Телефон заблокирован и не разрешает доступ.","Разблокируйте выбранный iPhone код-паролем и оставьте экран включённым. Пароль телефона вводится только на iPhone.");
        if(Has("no device found","no device connected","could not connect to device","device disconnected","device was disconnected","connection lost","not connected","устройство отключено"))
            return new(FailureCode.Disconnected,"Соединение с iPhone потеряно","Выбранное устройство недоступно по USB или Wi-Fi.","Проверьте кабель и USB-порт либо общую сеть Wi-Fi, разблокируйте iPhone и восстановите соединение. Очередь остановлена; уже установленные IPA повторяться не будут.");
        if(Has("lockdownd","start_service","could not connect to lockdown","receive timeout","connection reset","broken pipe","ssl error","не ответил за","не удалось подтвердить связь","pairing is not possible"))
            return new(FailureCode.Transport,"Не удалось установить защищённое соединение с iPhone","Служба устройства не отвечает, соединение прервано или сопряжение недоступно через Wi-Fi.","Подключите iPhone по USB, разблокируйте его и подтвердите доверие. Проверьте службу Apple Mobile Device и повторите проверку связи.");
        if(Has("too many requests", "too many attempts", "rate limit", "http 429", "status code 429", "temporarily throttled"))
            return new(FailureCode.RateLimited,"Apple ограничила частоту запросов","Сервер просит уменьшить количество обращений.","Очередь остановлена. Подождите перед ручным повтором; программа не отправляет запросы автоматически.");
        if(Has("your password has changed","password has been changed","password changed"))
            return new(FailureCode.PasswordChanged,"Сессия Apple Account устарела после смены пароля","Apple больше не принимает сохранённую авторизацию.","Нажмите «Войти» и подтвердите вход с новым паролем и кодом 2FA. Очередь остановлена; после входа продолжите оставшиеся загрузки.");
        if(Has("empty songlist","empty song list"))
            return new(FailureCode.EmptyCatalog,"Apple не вернула данные приложения","Магазин вернул пустой список. Приложение может быть недоступно для региона или аккаунта; также возможен временный сбой.","Проверьте ID и доступность приложения в App Store для этого аккаунта. Проверьте вход; после смены пароля войдите заново. Затем повторите запрос.");
        if(Has("account is locked","account has been locked","account has been disabled","account is disabled","account is not active","account is inactive","apple id has been disabled","apple id is locked","apple id has been locked","apple id is not active","account has been deactivated","media & purchases account has been disabled","account has been locked for security"))
            return new(FailureCode.AccountRestricted,"Apple ограничила доступ к аккаунту","Магазин отклонил авторизацию из-за состояния аккаунта.","Проверьте аккаунт на устройстве и восстановите доступ через Apple. После этого войдите в программу заново.");
        if(Has("incorrect password","password is incorrect","invalid credentials","invalid username","authentication failed","failed to authenticate"))
            return new(FailureCode.Credentials,"Apple не подтвердила вход","Логин, пароль или сохранённая сессия не приняты.","Проверьте Apple Account и обычный пароль, войдите заново с кодом 2FA. Пароль приложения для этого входа не подходит.");
        if(Has("terms and conditions","terms have changed","agreement required"))
            return new(FailureCode.Terms,"Нужно принять условия Apple","Аккаунт требует завершить настройку или принять обновлённые условия магазина.","Откройте App Store на iPhone под этим аккаунтом, завершите запросы Apple и затем войдите в программу заново.");
        if(Has("purchase error","not purchased","license not found","not available in your country"))
            return new(FailureCode.Purchase,"Apple не выдала право на загрузку","Приложение недоступно этому аккаунту/региону или магазин отклонил получение лицензии.","Проверьте приложение в App Store под тем же аккаунтом, получите его штатным способом и повторите загрузку. Сверьте регион аккаунта.");
        if(Has("applicationverificationfailed","0xe8008001"))
            return new(FailureCode.Verification,"iPhone не прошёл проверку приложения","iOS отклонила пакет, его подпись или права на установку.","Используйте целый IPA из доверенного источника и аккаунт, которому принадлежит приложение. Проверьте совместимость версии с iOS и повторно скачайте IPA.");
        if(Has("0xe8008015","0xe8008016","0xe8008018","application signature","invalid signature","provisioning profile"))
            return new(FailureCode.Signature,"Подпись приложения не подходит этому iPhone","Подпись, профиль или права приложения не прошли проверку iOS.","Получите корректный IPA и проверьте аккаунт/способ подписи. Повторное сопряжение не исправляет подпись приложения.");
        if(Has("not enough space","no space left","disk full","insufficient storage","0xe8000044"))
            return new(FailureCode.Storage,"Недостаточно свободного места","На компьютере или iPhone не хватает места для распаковки и установки.","Проверьте свободное место на обоих устройствах, освободите его и повторите только неудавшийся IPA.");
        if(Has("minimumosversion","deviceosversiontoolow","incompatible","unsupportedarchitecture"))
            return new(FailureCode.Incompatible,"Приложение несовместимо с устройством","Версия iOS, архитектура или платформа не подходит IPA.","Выберите более старую совместимую версию приложения либо обновите iOS, если это возможно.");
        if(Has("zip_open","invalid archive","central directory","не существует"))
            return new(FailureCode.Storage,"Не удалось прочитать IPA-файл","Файл удалён, повреждён или недоступен.","Проверьте файл в папке IPA, повторно скачайте его и добавьте в очередь.");
        if(error is HttpRequestException || Has("could not resolve","name resolution","network is unreachable","timed out","tls handshake"))
            return new(FailureCode.Network,"Нет ответа от сервера","Не удалось связаться с Apple или сервером обновлений.","Проверьте интернет, дату и время Windows, VPN и брандмауэр. Повторите запрос позже.");
        if(Has("однозначная windows-утилита","сначала установите инструменты","idevicepair отсутствует"))
            return new(FailureCode.MissingTool,"Нужны обновлённые инструменты iPhone","В выбранном комплекте отсутствует необходимая утилита.","Откройте «Настройки» → «Инструменты iPhone» и установите актуальный комплект. Для проверки доверия нужен idevicepair.");
        // Existing Russian validation messages already explain the concrete failure.
        if(Regex.IsMatch(text ?? "", "[А-Яа-яЁё]")) return new(FailureCode.Unknown,(text ?? "").Split('\n')[0],"Операция не была завершена. Подробности сохранены в итоговом отчёте.","Следуйте сообщению выше, проверьте выбранный файл и устройство. При повторении откройте итог операции и диагностический журнал.");
        return new(FailureCode.Unknown,"Операция завершилась с ошибкой","Утилита вернула нераспознанный ответ; точная причина пока не определена.","Проверьте соединение, выбранный IPA и версии инструментов. Откройте итог операции для технических подробностей.");
    }
}
public sealed class DeviceConnectionException : IOException
{
    public UserError Failure { get; }
    public DeviceConnectionException(UserError failure, Exception inner = null) : base(failure.Description,inner) => Failure=failure;
}
