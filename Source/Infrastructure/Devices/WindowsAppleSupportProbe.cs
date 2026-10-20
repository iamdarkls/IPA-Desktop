using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Security;
using System.ServiceProcess;
using Microsoft.Win32;

namespace IpaDesktop
{
    /// <summary>Только читает службы и реестр Windows; не устанавливает драйверы и не меняет службы.</summary>
    public interface IAppleSupportProbe
    {
        AppleSupportSnapshot Read();
    }

    /// <summary>Только читает службы и реестр Windows; не устанавливает драйверы и не меняет службы.</summary>
    public sealed class WindowsAppleSupportProbe : IAppleSupportProbe
    {
        public AppleSupportSnapshot Read()
        {
            var result = new AppleSupportSnapshot();
            var details = new List<string>();
            ServiceController[] services = null;
            try
            {
                services = ServiceController.GetServices();
                result.ServiceScanCompleted = true;
                foreach (var service in services)
                {
                    if (!service.ServiceName.Equals("AppleMobileDeviceService", StringComparison.OrdinalIgnoreCase)
                        && !service.ServiceName.Equals("Apple Mobile Device Service", StringComparison.OrdinalIgnoreCase)
                        && !service.DisplayName.StartsWith("Apple Mobile Device", StringComparison.OrdinalIgnoreCase))
                        continue;
                    result.ServiceFound = true;
                    details.Add("Служба: " + service.ServiceName);
                    result.ServiceRunning = service.Status == ServiceControllerStatus.Running;
                    result.ServiceStatusKnown = true;
                    details.Add("Состояние службы: " + service.Status);
                    break;
                }
            }
            catch (InvalidOperationException error)
            {
                result.ServiceScanCompleted = false;
                details.Add("Проверка службы: " + error.Message);
            }
            catch (Win32Exception error)
            {
                result.ServiceScanCompleted = false;
                details.Add("Проверка службы: " + error.Message);
            }
            catch (SecurityException error)
            {
                result.ServiceScanCompleted = false;
                details.Add("Проверка службы: " + error.Message);
            }
            finally
            {
                if (services != null)
                    foreach (var service in services) service.Dispose();
            }

            bool registryComplete = true;
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    {
                        using (var apple = root.OpenSubKey(@"SOFTWARE\Apple Inc.\Apple Mobile Device Support"))
                        {
                            if (apple != null)
                            {
                                result.InstallationDetected = true;
                                details.Add("Регистрация компонентов Apple: " + view);
                            }
                        }
                        using (var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                        {
                            if (uninstall == null) continue;
                            foreach (var keyName in uninstall.GetSubKeyNames())
                            {
                                using (var product = uninstall.OpenSubKey(keyName))
                                {
                                    if (product == null) continue;
                                    string name = Convert.ToString(product.GetValue("DisplayName"));
                                    if (!name.Equals("Apple Mobile Device Support", StringComparison.OrdinalIgnoreCase))
                                        continue;
                                    result.InstallationDetected = true;
                                    details.Add("Установленный компонент: " + name + " " + Convert.ToString(product.GetValue("DisplayVersion")));
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (UnauthorizedAccessException error)
                {
                    registryComplete = false;
                    details.Add("Проверка реестра: " + error.Message);
                }
                catch (SecurityException error)
                {
                    registryComplete = false;
                    details.Add("Проверка реестра: " + error.Message);
                }
                catch (System.IO.IOException error)
                {
                    registryComplete = false;
                    details.Add("Проверка реестра: " + error.Message);
                }
            }
            result.RegistryScanCompleted = registryComplete;
            result.Details = String.Join(Environment.NewLine, details);
            return result;
        }
    }
}
