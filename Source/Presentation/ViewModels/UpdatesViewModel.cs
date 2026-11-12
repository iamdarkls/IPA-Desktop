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
        readonly SemaphoreSlim guiCheck = new(1, 1);
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
                SystemIntegration.Open(AppInfo.ReleasesUrl + "/tag/" + EngineUpdater.GuardedEngineTag);
            });
            CheckDeviceUpdateCommand = Command(() => Work("Проверка инструментов для iPhone…", ct => UpdateDevices(ct)));
            RollbackDevicesCommand = AsyncCommand(async delegate
            {
                if (!await desktop.Interaction.Confirm("Откат инструментов", "Вернуть предыдущий комплект для iPhone и отключить его автообновление?")) return;
                await desktop.RunWorkAsync("Откат инструментов для iPhone…", _ =>
                {
                    toolUpdates.RollbackDeviceTools();
                    return Task.CompletedTask;
                });
            });
            OpenDeviceReleasesCommand = Command(delegate
            {
                SystemIntegration.Open(AppInfo.ReleasesUrl + "?q=device-tools");
            });
            CheckUpdateCommand = Command(() => Work("Проверка обновлений движка…", UpdateEngine));
            RollbackCommand = AsyncCommand(async delegate
            {
                if (await desktop.Interaction.Confirm("Откат", "Вернуть предыдущий движок и отключить автообновление?"))
                {
                    await desktop.RunWorkAsync("Откат движка…", _ =>
                    {
                        toolUpdates.Rollback();
                        return Task.CompletedTask;
                    });
                }
            });

        }
        /// <summary>Пересчитывает доступность действий по состоянию выбора и выполняемой операции.</summary>
        public override void RefreshActions()
        {
            CheckGuiUpdatesEnabled = !busy;
            AutoUpdateEnabled = AutoUpdateDeviceToolsEnabled = RuntimePlatform.Windows && !busy;
            InstallGuiUpdateEnabled = !busy && guiRelease != null;
            RollbackDevicesEnabled = !busy && !String.IsNullOrEmpty(settings.Settings.PreviousDeviceCurrent);
        }
        bool EngineCurrent(string tag) =>
            settings.Settings.Source == EngineUpdater.SourceRepository &&
            String.Equals(tag, settings.Settings.Version, StringComparison.Ordinal) &&
            !String.IsNullOrEmpty(settings.Settings.Current);

        internal void RefreshEngineState()
        {
            EngineVersion = "Движок: " + (settings.Settings.Version ?? "не установлен");
            EngineUpdateNeeded = RuntimePlatform.Windows && !EngineCurrent(EngineUpdater.GuardedEngineTag);
        }

        async Task UpdateEngine(CancellationToken ct)
        {
            if (!RuntimePlatform.Windows)
            {
                Log("На этой системе движок обновляется вместе с IPA Desktop.");
                await CheckGui(ct);
                return;
            }
            var release = await toolUpdates.Check(ct);
            if (EngineCurrent(release.tag_name)) { Log("Установлен актуальный релиз " + release.tag_name); return; }
            if (await desktop.Interaction.Confirm("Обновление движка", "Установить релиз " + release.tag_name + "? Текущая версия будет сохранена для отката."))
                await toolUpdates.Install(release, ct);
        }

        internal async Task CheckAutomaticUpdates(CancellationToken ct)
        {
            Release engine = null, devices = null;
            async Task CheckEngine(CancellationToken token) { engine = await toolUpdates.Check(token); }
            async Task CheckDevices(CancellationToken token)
            {
                devices = await toolUpdates.CheckDeviceTools(token);
                DescribeDeviceRelease(devices);
            }
            await Task.WhenAll(
                settings.Settings.AutoUpdate && EngineUpdateNeeded
                    ? desktop.CheckStartup("Проверка движка", CheckEngine, ct) : Task.CompletedTask,
                settings.Settings.AutoUpdateDeviceTools
                    ? desktop.CheckStartup("Проверка инструментов для iPhone", CheckDevices, ct) : Task.CompletedTask);
            if ((engine == null || EngineCurrent(engine.tag_name)) &&
                (devices == null || toolUpdates.DeviceToolsCurrent(devices))) return;

            await desktop.ApplyAutomaticUpdates(async token =>
            {
                if (engine != null && settings.Settings.AutoUpdate && !EngineCurrent(engine.tag_name))
                    await desktop.CheckStartup("Автоматическое обновление движка", t => toolUpdates.Install(engine, t), token);
                if (devices != null && settings.Settings.AutoUpdateDeviceTools && !toolUpdates.DeviceToolsCurrent(devices))
                    await desktop.CheckStartup("Автоматическое обновление инструментов для iPhone", t => UpdateDevices(t, devices), token);
            }, ct);
        }

        bool DescribeDeviceRelease(Release release)
        {
            DeviceReleaseNotes = release.body ?? "Описание изменений отсутствует.";
            bool current = toolUpdates.DeviceToolsCurrent(release);
            DeviceUpdateStatus = (current ? "Установлен актуальный комплект " : "Доступен комплект ") + release.tag_name;
            return current;
        }

        async Task UpdateDevices(CancellationToken ct, Release automatic = null)
        {
            DeviceUpdateStatus = "Проверяем доступную версию…";
            try
            {
                if (!RuntimePlatform.Windows)
                {
                    await CheckGui(ct);
                    DeviceUpdateStatus = guiRelease == null ? "Обновлений IPA Desktop нет. Встроенный комплект проверен при сборке приложения." : "Новый встроенный комплект доступен вместе с " + guiRelease.tag_name + ". Обновите IPA Desktop.";
                    DeviceReleaseNotes = guiRelease?.body ?? "Инструменты на macOS и Linux обновляются вместе с программой.";
                    return;
                }
                var release = automatic ?? await toolUpdates.CheckDeviceTools(ct);
                if (DescribeDeviceRelease(release))
                {
                    Log("Наш комплект для iPhone актуален: " + release.tag_name);
                    return;
                }
                if (automatic == null && !await desktop.Interaction.Confirm("Обновление инструментов", "Обновить комплект для iPhone до " + release.tag_name + "? Предыдущий комплект будет сохранён для отката.\n\n" + (release.body ?? "Описание изменений отсутствует."))) return;
                await toolUpdates.InstallDeviceTools(release, ct);
                RefreshInstalledTools();
                DeviceUpdateStatus = "Комплект " + release.tag_name + " установлен: контрольная сумма и запуск утилит проверены.";
            }
            catch (OperationCanceledException) { DeviceUpdateStatus = "Проверка или обновление отменены. Активная версия указана выше."; throw; }
            catch (Exception) { DeviceUpdateStatus = "Проверка или обновление не завершены. Активная версия указана выше; подробности в журнале."; throw; }
        }
        public async Task CheckGui(CancellationToken ct)
        {
            await guiCheck.WaitAsync(ct);
            try
            {
                guiRelease = await guiUpdates.Check(ct);
                string text = guiRelease == null ? "Установлена актуальная версия " + AppInfo.Version : "Доступна версия " + guiRelease.tag_name;
                GuiVersionStatus = text;
                InstallGuiUpdateLabel = guiRelease == null ? "Установить обновление" : "Обновить до " + guiRelease.tag_name;
                UpdateNotice = guiRelease == null ? "" : text + " — вкладка «Настройки»";
                UpdateNoticeVisible = guiRelease != null;
                Log(text);
                RefreshActions();
            }
            finally { guiCheck.Release(); }
        }
    }
}
