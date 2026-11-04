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
        readonly ISettingsStore settings;
        readonly IToolUpdates toolUpdates;
        readonly IGuiUpdates guiUpdates;
        public UpdatesViewModel(DesktopViewModel desktop, ISettingsStore settings, IToolUpdates toolUpdates, IGuiUpdates guiUpdates) : base(desktop) { this.settings = settings; this.toolUpdates = toolUpdates; this.guiUpdates = guiUpdates; }
        internal void Initialize()
        {
            CheckGuiUpdates = settings.Settings.CheckGuiUpdates;
            PropertyChanged += (_, change) =>
            {
                if (setting || change.PropertyName is not (nameof(SoundsEnabled) or nameof(CheckGuiUpdates) or nameof(AutoUpdate) or nameof(AutoUpdateDeviceTools))) return;
                if (busy) { StateView(); return; }
                var previous = settings.Settings.Copy();
                try
                {
                    settings.Settings.SoundsEnabled = SoundsEnabled; settings.Settings.CheckGuiUpdates = CheckGuiUpdates;
                    settings.Settings.AutoUpdate = AutoUpdate; settings.Settings.AutoUpdateDeviceTools = AutoUpdateDeviceTools;
                    settings.Save();
                }
                catch (Exception error) { settings.Settings = previous; StateView(); Error(error); }
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
                    string stage = await guiUpdates.Prepare(guiRelease, ct, delegate (double value)
                    {
                        desktop.Interaction.Post(new Action(delegate
                        {
                            desktop.Indeterminate = false;
                            desktop.Progress = value;
                        }));
                    });
                    ct.ThrowIfCancellationRequested();
                    guiUpdates.Launch(stage);
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
                try { toolUpdates.RollbackDeviceTools(); StateView(); ApplyActions(); }
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
                    toolUpdates.Rollback();
                    StateView();
                }
            });

        }
        /// <summary>Пересчитывает доступность действий по состоянию выбора и выполняемой операции.</summary>
        public override void RefreshActions()
        {
            AutoUpdateEnabled = AutoUpdateDeviceToolsEnabled = CheckGuiUpdatesEnabled = !busy;
            InstallGuiUpdateEnabled = !busy && guiRelease != null;
            RollbackDevicesEnabled = !busy && !String.IsNullOrEmpty(settings.Settings.PreviousDeviceCurrent);
        }
        public async Task Update(CancellationToken ct, bool startup)
        {
            if (!RuntimePlatform.Windows) { Log("На этой системе движок обновляется вместе с IPA Desktop."); await CheckGui(ct); return; }
            var release = await toolUpdates.Check(ct);
            if (settings.Settings.Source == EngineUpdater.SourceRepository && String.Equals(release.tag_name, settings.Settings.Version, StringComparison.Ordinal) && !String.IsNullOrEmpty(settings.Settings.DeviceCurrent))
            {
                Log("Установлен актуальный релиз " + release.tag_name);
                return;
            }
            if (!startup && !await desktop.Interaction.Confirm("Обновление движка", "Установить официальный релиз " + release.tag_name + "? Текущая версия будет сохранена для отката.")) return;
            await toolUpdates.Install(release, ct);
            if (startup) Log("Автоматическое обновление завершено.");
        }
        public async Task UpdateDevices(CancellationToken ct, bool startup)
        {
            if (!RuntimePlatform.Windows) { Log("На этой системе инструменты iPhone обновляются вместе с IPA Desktop."); await CheckGui(ct); return; }
            var release = await toolUpdates.CheckDeviceTools(ct);
            if (toolUpdates.DeviceToolsCurrent(release))
            {
                Log("Наш комплект для iPhone актуален: " + release.tag_name);
                return;
            }
            if (!startup && !await desktop.Interaction.Confirm("Обновление инструментов", "Обновить комплект для iPhone до " + release.tag_name + "? Предыдущий комплект будет сохранён для отката.")) return;
            await toolUpdates.InstallDeviceTools(release, ct);
        }
        public async Task CheckGui(CancellationToken ct)
        {
            guiRelease = await guiUpdates.Check(ct);
            string text = guiRelease == null ? "Установлена актуальная версия " + AppInfo.Version : "Доступна версия " + guiRelease.tag_name;
            GuiVersionStatus = text;
            InstallGuiUpdateLabel = guiRelease == null ? "Установить обновление" : "Обновить до " + guiRelease.tag_name;
            UpdateNotice = guiRelease == null ? "" : text + " — вкладка «Настройки»";
            UpdateNoticeVisible = guiRelease != null;
            Log(text);
        }
    }
}
