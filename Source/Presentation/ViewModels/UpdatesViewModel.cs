using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop
{
    /// <summary>Связывает кнопки обновления и отката GUI, ipatool и инструментов установки.</summary>
    public sealed partial class UpdatesViewModel : FeatureViewModel
    {
        Release guiRelease;
        public UpdatesViewModel(DesktopViewModel desktop, Engine engine) : base(desktop, engine) { }
        internal void Initialize()
        {
            CheckGuiUpdates = engine.Settings.CheckGuiUpdates;
            PropertyChanged += (_, change) =>
            {
                if (setting || change.PropertyName is not (nameof(SoundsEnabled) or nameof(CheckGuiUpdates) or nameof(AutoUpdate) or nameof(AutoUpdateDeviceTools))) return;
                if (busy) { StateView(); return; }
                var previous = engine.Settings.Copy();
                try
                {
                    engine.Settings.SoundsEnabled = SoundsEnabled; engine.Settings.CheckGuiUpdates = CheckGuiUpdates;
                    engine.Settings.AutoUpdate = AutoUpdate; engine.Settings.AutoUpdateDeviceTools = AutoUpdateDeviceTools;
                    engine.Save();
                }
                catch (Exception error) { engine.Settings = previous; StateView(); Error(error); }
            };
            CheckGuiUpdateCommand = Command(delegate
            {
                Work("Проверка версии IPA Desktop…", CheckGui);
            });
            InstallGuiUpdateCommand = AsyncCommand(async delegate
            {
                if (guiRelease == null) return;
                if (!await desktop.Interaction.Confirm("Обновление программы", "Скачать и установить IPA Desktop " + guiRelease.tag_name + "? Программа перезапустится. IPA и настройки сохранятся.")) return;
                Work("Скачивание обновления IPA Desktop…", async delegate (CancellationToken ct)
                {
                    string stage = await GuiUpdater.Prepare(guiRelease, ct, delegate (double value)
                    {
                        desktop.Interaction.Post(new Action(delegate
                        {
                            desktop.Indeterminate = false;
                            desktop.Progress = value;
                        }));
                    });
                    ct.ThrowIfCancellationRequested();
                    GuiUpdater.Launch(stage);
                    desktop.RequestRestart();
                });
            });
            OpenGuiReleasesCommand = Command(delegate
            {
                SystemIntegration.Open(AppInfo.ReleasesUrl);
            });
            OpenReleaseCommand = Command(delegate
            {
                SystemIntegration.Open("https://github.com/iamdarkls/IPA-Desktop/releases/tag/ipatool-guard-v1.2.1.1");
            });
            CheckDeviceUpdateCommand = Command(delegate
            {
                Work("Проверка инструментов для iPhone…", ct => UpdateDevices(ct, false));
            });
            RollbackDevicesCommand = AsyncCommand(async delegate
            {
                if (!await desktop.Interaction.Confirm("Откат инструментов", "Вернуть предыдущий комплект для iPhone и отключить его автообновление?")) return;
                try { engine.RollbackDeviceTools(); StateView(); ApplyActions(); }
                catch (Exception e) { Error(e); }
            });
            OpenDeviceReleasesCommand = Command(delegate
            {
                SystemIntegration.Open(AppInfo.ReleasesUrl + "?q=device-tools");
            });
            CheckUpdateCommand = Command(delegate
            {
                Work("Проверка обновлений…", ct => Update(ct, false));
            });
            RollbackCommand = AsyncCommand(async delegate
            {
                if (await desktop.Interaction.Confirm("Откат", "Вернуть предыдущий движок и отключить автообновление?"))
                {
                    engine.Rollback();
                    StateView();
                }
            });

        }
        /// <summary>Пересчитывает доступность действий по состоянию выбора и выполняемой операции.</summary>
        public override void RefreshActions()
        {
            AutoUpdateEnabled = AutoUpdateDeviceToolsEnabled = CheckGuiUpdatesEnabled = !busy;
            InstallGuiUpdateEnabled = !busy && guiRelease != null;
            RollbackDevicesEnabled = !busy && !String.IsNullOrEmpty(engine.Settings.PreviousDeviceCurrent);
        }
        public async Task Update(CancellationToken ct, bool startup)
        {
            if (!RuntimePlatform.Windows) { Log("На этой системе движок обновляется вместе с IPA Desktop."); await CheckGui(ct); return; }
            var release = await engine.Check(ct);
            if (engine.Settings.Source == EngineUpdater.SourceRepository && String.Equals(release.tag_name, engine.Settings.Version, StringComparison.Ordinal) && !String.IsNullOrEmpty(engine.Settings.DeviceCurrent))
            {
                Log("Установлен актуальный релиз " + release.tag_name);
                return;
            }
            if (!startup && !await desktop.Interaction.Confirm("Обновление движка", "Установить официальный релиз " + release.tag_name + "? Текущая версия будет сохранена для отката.")) return;
            await engine.Install(release, ct);
            if (startup) Log("Автоматическое обновление завершено.");
        }
        public async Task UpdateDevices(CancellationToken ct, bool startup)
        {
            if (!RuntimePlatform.Windows) { Log("На этой системе инструменты iPhone обновляются вместе с IPA Desktop."); await CheckGui(ct); return; }
            var release = await engine.CheckDeviceTools(ct);
            if (engine.DeviceToolsCurrent(release))
            {
                Log("Наш комплект для iPhone актуален: " + release.tag_name);
                return;
            }
            if (!startup && !await desktop.Interaction.Confirm("Обновление инструментов", "Обновить комплект для iPhone до " + release.tag_name + "? Предыдущий комплект будет сохранён для отката.")) return;
            await engine.InstallDeviceTools(release, ct);
        }
        public async Task CheckGui(CancellationToken ct)
        {
            guiRelease = await GuiUpdater.Check(ct);
            string text = guiRelease == null ? "Установлена актуальная версия " + GuiUpdater.CurrentVersion : "Доступна версия " + guiRelease.tag_name;
            GuiVersionStatus = text;
            InstallGuiUpdateLabel = guiRelease == null ? "Установить обновление" : "Обновить до " + guiRelease.tag_name;
            UpdateNotice = guiRelease == null ? "" : text + " — вкладка «Настройки»";
            UpdateNoticeVisible = guiRelease != null;
            Log(text);
        }
    }
}
