using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Microsoft.Win32;

namespace IpaDesktop
{
    /// <summary>Связывает кнопки обновления и отката GUI, ipatool и инструментов установки.</summary>
    public sealed class UpdatesController : WindowController
    {
        Release guiRelease;
        public UpdatesController(Desktop view, Engine engine) : base(view, engine)
        {
            C<CheckBox>("SoundsEnabled").Click += delegate
            {
                bool previous = engine.Settings.SoundsEnabled;
                engine.Settings.SoundsEnabled = C<CheckBox>("SoundsEnabled").IsChecked == true;
                try { engine.Save(); }
                catch (Exception e)
                {
                    engine.Settings.SoundsEnabled = previous;
                    StateView();
                    Error(e);
                }
            };
            C<CheckBox>("CheckGuiUpdates").IsChecked = engine.Settings.CheckGuiUpdates;
            C<CheckBox>("CheckGuiUpdates").Click += delegate
            {
                engine.Settings.CheckGuiUpdates = C<CheckBox>("CheckGuiUpdates").IsChecked == true;
                try
                {
                    engine.Save();
                } catch (Exception e)
                {
                    Error(e);
                }
            };
            On("CheckGuiUpdate", delegate
            {
                Work("Проверка версии IPA Desktop…", CheckGui);
            });
            OnAsync("InstallGuiUpdate", async delegate
            {
                if (guiRelease == null) return;
                if (!await AppDialogs.Confirm(w, "Обновление программы", "Скачать и установить IPA Desktop " + guiRelease.tag_name + "? Программа перезапустится. IPA и настройки сохранятся.")) return;
                Work("Скачивание обновления IPA Desktop…", async delegate(CancellationToken ct)
                {
                    string stage = await GuiUpdater.Prepare(guiRelease, ct, delegate(double value)
                    {
                        Dispatcher.UIThread.Post(new Action(delegate
                        {
                            C<ProgressBar>("Progress").IsIndeterminate = false;
                            C<ProgressBar>("Progress").Value = value;
                        }));
                    });
                    ct.ThrowIfCancellationRequested();
                    GuiUpdater.Launch(stage);
                    view.RequestRestart();
                });
            });
            On("OpenGuiReleases", delegate
            {
                Process.Start(new ProcessStartInfo(AppInfo.ReleasesUrl)
                {
                    UseShellExecute = true
                });
            });
            On("OpenRelease", delegate
            {
                Process.Start(new ProcessStartInfo("https://github.com/iamdarkls/IPA-Desktop/releases/tag/ipatool-guard-v1.2.0.2")
                {
                    UseShellExecute = true
                });
            });
            C<CheckBox>("AutoUpdateDeviceTools").Click += delegate
            {
                engine.Settings.AutoUpdateDeviceTools = C<CheckBox>("AutoUpdateDeviceTools").IsChecked == true;
                try { engine.Save(); } catch (Exception e) { Error(e); }
            };
            On("CheckDeviceUpdate", delegate
            {
                Work("Проверка инструментов для iPhone…", ct => UpdateDevices(ct, false));
            });
            OnAsync("RollbackDevices", async delegate
            {
                if (!await AppDialogs.Confirm(w, "Откат инструментов", "Вернуть предыдущий комплект для iPhone и отключить его автообновление?")) return;
                try { engine.RollbackDeviceTools(); StateView(); ApplyActions(); }
                catch (Exception e) { Error(e); }
            });
            On("OpenDeviceReleases", delegate
            {
                Process.Start(new ProcessStartInfo(AppInfo.ReleasesUrl + "?q=device-tools") { UseShellExecute = true });
            });
            On("CheckUpdate", delegate
            {
                Work("Проверка обновлений…", ct => Update(ct, false));
            });
            OnAsync("Rollback", async delegate
            {
                if (await AppDialogs.Confirm(w, "Откат", "Вернуть предыдущий движок и отключить автообновление?"))
                {
                    engine.Rollback();
                    StateView();
                }
            });
            C<CheckBox>("AutoUpdate").IsCheckedChanged += delegate
            {
                if (setting) return;
                engine.Settings.AutoUpdate = C<CheckBox>("AutoUpdate").IsChecked == true;
                try { engine.Save(); } catch (Exception error) { Error(error); }
            };
        }
        /// <summary>Пересчитывает доступность действий по состоянию выбора и выполняемой операции.</summary>
        public override void RefreshActions()
        {
            C<Button>("InstallGuiUpdate").IsEnabled = !busy && guiRelease != null;
            C<Button>("RollbackDevices").IsEnabled = !busy && !String.IsNullOrEmpty(engine.Settings.PreviousDeviceCurrent);
        }
        public async Task Update(CancellationToken ct, bool startup)
        {
            var release = await engine.Check(ct);
            if (engine.Settings.Source == EngineUpdater.SourceRepository && String.Equals(release.tag_name, engine.Settings.Version, StringComparison.Ordinal) && !String.IsNullOrEmpty(engine.Settings.DeviceCurrent))
            {
                Log("Установлен актуальный релиз " + release.tag_name);
                return;
            }
            if (!startup && !await AppDialogs.Confirm(w, "Обновление движка", "Установить официальный релиз " + release.tag_name + "? Текущая версия будет сохранена для отката.")) return;
            await engine.Install(release, ct);
            if (startup) Log("Автоматическое обновление завершено.");
        }
        public async Task UpdateDevices(CancellationToken ct, bool startup)
        {
            var release = await engine.CheckDeviceTools(ct);
            if (engine.DeviceToolsCurrent(release))
            {
                Log("Наш комплект для iPhone актуален: " + release.tag_name);
                return;
            }
            if (!startup && !await AppDialogs.Confirm(w, "Обновление инструментов", "Обновить комплект для iPhone до " + release.tag_name + "? Предыдущий комплект будет сохранён для отката.")) return;
            await engine.InstallDeviceTools(release, ct);
        }
        public async Task CheckGui(CancellationToken ct)
        {
            guiRelease = await GuiUpdater.Check(ct);
            string text = guiRelease == null ? "Установлена актуальная версия " + GuiUpdater.CurrentVersion : "Доступна версия " + guiRelease.tag_name;
            C<TextBlock>("GuiVersionStatus").Text = text;
            C<Button>("InstallGuiUpdate").Content = guiRelease == null ? "Установить обновление" : "Обновить до " + guiRelease.tag_name;
            C<TextBlock>("UpdateNotice").Text = guiRelease == null ? "" : text + " — вкладка «Настройки»";
            C<TextBlock>("UpdateNotice").IsVisible = guiRelease != null;
            Log(text);
        }
    }
}
