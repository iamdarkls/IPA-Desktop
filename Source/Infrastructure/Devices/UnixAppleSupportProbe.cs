using System;
using System.IO;
namespace IpaDesktop;
public sealed class UnixAppleSupportProbe : IAppleSupportProbe
{
    public AppleSupportSnapshot Read() => new(){ ServiceFound=true,ServiceStatusKnown=false,ServiceScanCompleted=true,RegistryScanCompleted=true,
        Details=OperatingSystem.IsMacOS()?"macOS использует встроенную службу Apple. Подключите iPhone, разблокируйте и подтвердите доверие. Готовность проверяется по ответу выбранного устройства.":"Linux: установите и запустите usbmuxd, проверьте правила udev и доступ пользователя к USB. Разблокируйте iPhone и подтвердите доверие. Наличие сокета /var/run/usbmuxd: "+File.Exists("/var/run/usbmuxd") };
}
