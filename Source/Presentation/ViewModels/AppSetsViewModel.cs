using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace IpaDesktop
{
    /// <summary>Управляет наборами в обеих вкладках: сохранением, выбором, предпросмотром и добавлением в очередь.</summary>
    public sealed partial class AppSetsViewModel : FeatureViewModel
    {
        public AppSetsViewModel(DesktopViewModel desktop, Engine engine) : base(desktop, engine) { }
        internal void Initialize()
        {
            AppSets.SelectionChanged += delegate { Preview(); ApplyActions(); };
            AddToSetCommand = Command(delegate { Save(desktop.Search.SearchSet, "SearchSetStatus"); });
            SaveSetCommand = Command(delegate { Save(SetName, "SetStatus"); });
            EditSetCommand = Command(delegate
            {
                var selected = Selected();
                if (selected.Count != 1) return;
                var original = selected[0];
                Work("Открытие набора…", async delegate (CancellationToken ct)
                {
                    var apps = await Task.Run(delegate { ct.ThrowIfCancellationRequested(); return engine.AppSets.ResolveApps(original); }, ct);
                    ct.ThrowIfCancellationRequested();
                    if (await desktop.Interaction.EditSet(original.Name, apps,
                        (name, rows) => engine.AppSets.Update(original.Name, name, rows))) RefreshSets();
                });
            });
            DeleteSetCommand = Command(DeleteSelected);
            UseSetCommand = Command(UseSelected);
            InstallSetCommand = AsyncCommand(InstallSelected);
            RefreshSets();
        }
        /// <summary>Обе кнопки дополняют набор одинаково; повторное имя не уничтожает прежние приложения.</summary>
        void SetStatusMessage(string target, string message)
        { if (target == "SearchSetStatus") desktop.Search.SearchSetStatus = message; else SetStatus = message; }
        void Save(string name, string status)
        {
            name = (name ?? "").Trim();
            try
            {
                var rows = desktop.SelectedDownloads();
                if (name.Length == 0) throw new InvalidOperationException("Введите название нового набора или выберите существующий.");
                if (rows.Count == 0) throw new InvalidOperationException("Добавьте приложения в выбранное в поиске.");
                engine.AppSets.AddApps(name, rows.Select(x => new AppRow { ID = x.ID, Name = x.Name }));
                RefreshSets();
                desktop.Search.SearchSet = name;
                SetStatusMessage(status, "Набор «" + name + "» сохранён. Повторы по ID исключены.");
                desktop.RegisterPrivate(name);
            }
            catch (Exception e) { SetStatusMessage(status, e.Message); Error(e); }
        }
        /// <summary>Сохраняет выделение по именам после перезагрузки списка, независимо от ссылок на модели.</summary>
        internal void RefreshSets()
        {
            var list = AppSets;
            var selected = new HashSet<string>(Selected().Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
            var name = desktop.Search.SearchSet;
            try
            {
                var sets = engine.AppSets.Load();
                list.Items = sets;
                foreach (var set in sets.Where(x => selected.Contains(x.Name))) list.SelectedItems.Add(set);
                if (list.SelectedItems.Count == 0 && sets.Count > 0) list.SelectedIndex = 0;
                desktop.Search.SearchSetOptions = sets.Select(x => x.Name).ToArray(); desktop.Search.SearchSet = name;
                SetsCount = sets.Count == 0 ? "Наборов пока нет" : "Сохранено наборов: " + sets.Count;
                Preview(); ApplyActions();
            }
            catch (Exception e) { SetStatus = e.Message; Error(e); }
        }
        List<AppSet> Selected() { return AppSets.SelectedItems.Cast<AppSet>().ToList(); }
        void Preview()
        {
            var selected = Selected();
            var apps = selected.Where(x => x.Apps != null).SelectMany(x => x.Apps).GroupBy(x => x.ID).Select(g => g.First()).ToArray();
            SetPreview = selected.Count == 0 ? "Отметьте один или несколько наборов." :
                "Выбрано наборов: " + selected.Count + " · уникальных приложений: " + apps.Length +
                (selected.Any(x => x.Apps == null) ? " · есть наборы прежнего формата" : "") + Environment.NewLine +
                String.Join(", ", apps.Take(12).Select(x => x.Name)) + (apps.Length > 12 ? "…" : "");
        }
        /// <summary>Сначала разрешает все наборы; при ошибке ни один из них не попадает в очередь частично.</summary>
        void UseSelected()
        {
            var selected = Selected();
            if (selected.Count == 0) return;
            Work("Добавление наборов…", async delegate (CancellationToken ct)
            {
                var apps = await Task.Run(delegate { ct.ThrowIfCancellationRequested(); return engine.AppSets.ResolveMany(selected); }, ct);
                ct.ThrowIfCancellationRequested();
                int added = desktop.AddSetApps(apps);
                RefreshSets();
                foreach (var set in selected) desktop.RegisterPrivate(set.Name);
                string message = "Наборов: " + selected.Count + ". Добавлено приложений: " + added + ", уже в очереди: " + (apps.Length - added) + ".";
                SetStatus = message;
                desktop.Search.SearchSetStatus = message;
                Log(message);
            });
        }
        async Task InstallSelected()
        {
            if (busy) return;
            var sets = Selected(); var phone = desktop.Files.DeviceList.SelectedItem as DeviceRow;
            if (sets.Count == 0 || phone == null) throw new InvalidOperationException("Выберите наборы и iPhone во вкладке «iPhone и IPA».");
            var target = phone.ID + "|" + phone.Network;
            AppRow[] apps = null; string account = null; AppSetInstallationPlan plan = null;
            if (!await desktop.RunWorkAsync("Подготовка набора…", async ct =>
            {
                account = await desktop.RequireAccountAsync(ct);
                apps = await Task.Run(() => engine.AppSets.ResolveMany(sets), ct);
                plan = AppSetInstallationPlan.Build(apps, await engine.LocalFiles.ListAsync(), account);
            })) return;
            if (!await desktop.Interaction.Confirm("Подготовка установки набора", phone.DisplayLabel + "\nApple Account: " + account + "\n\n" + plan.Preview + "\n\nИспользовать эти локальные версии и скачать недостающие? Перед установкой будет отдельное подтверждение.")) return;
            var expectedAccount = desktop.AccountKey;
            if (plan.Missing.Count > 0)
            {
                if (!await desktop.RunWorkAsync("Загрузка недостающих приложений набора…", async ct =>
                {
                    await desktop.RequireAccountAsync(ct, expectedAccount);
                    desktop.StartReport("Скачивание", plan.Missing.Count, () => Task.CompletedTask);
                    QueueBatchViewModel batch = null;
                    batch = desktop.Queue.Start("Скачивание", plan.Missing.Select(x => new QueueSeed(x.ID + "@latest", x.Name, "Последняя доступная")), null, "Apple Account · " + DesktopViewModel.AccountHint(account),
                        keys => _ = desktop.RunAsyncCommand(() => desktop.ResumeStoredBatch(batch, keys)), null, expectedAccount);
                    var result = await BatchRunner.Run(plan.Missing, async (app, token) =>
                    {
                        var path = await engine.ApplicationDownloads.DownloadFileAsync(new DownloadItem { ID = app.ID, Name = app.Name }, token);
                        if (!System.IO.File.Exists(path)) throw new System.IO.IOException("Движок не создал IPA для " + app.Name);
                    }, (app, index) => desktop.BeginQueue("Скачивание", index, plan.Missing.Count, app.Name), app => desktop.ReportItem(app.Name, null), (app, error) => desktop.ReportItem(app.Name, error), null, ct);
                    desktop.CompleteQueue(result.Succeeded, result.Failed.Count);
                    if (result.Failed.Count > 0) throw new InvalidOperationException("Часть набора не скачалась. Установка не запущена. Продолжите загрузку в очереди и подготовьте набор снова.");
                })) return;
            }
            if (!await desktop.RunWorkAsync("Проверка готовности набора…", async ct =>
            {
                await desktop.RequireAccountAsync(ct, expectedAccount);
                plan = AppSetInstallationPlan.Build(apps, await engine.LocalFiles.ListAsync(), account);
                if (plan.Missing.Count > 0) throw new InvalidOperationException("Не все IPA найдены с метаданными исходного аккаунта. Автоматическая установка набора остановлена.");
            })) return;
            await desktop.StartSetInstallation(plan.Local, target);
        }
        void DeleteSelected()
        {
            var selected = Selected();
            // Удаление только конфигурации, без изменения IPA на диске или приложений на телефоне.
            engine.AppSets.DeleteMany(selected.Select(x => x.Name));
            RefreshSets(); SetStatus = "Удалено наборов: " + selected.Count + ". IPA сохранены.";
        }
        /// <summary>Пересчитывает доступность действий по состоянию выбора и выполняемой операции.</summary>
        public override void RefreshActions()
        {
            int count = Selected().Count;
            AddToSetEnabled = SaveSetEnabled = !busy && desktop.SelectedDownloads().Count > 0;
            EditSetEnabled = !busy && count == 1;
            UseSetEnabled = DeleteSetEnabled = !busy && count > 0;
            InstallSetEnabled = !busy && count > 0 && desktop.Files.DeviceList.SelectedItem != null;
            UseSetLabel = "В очередь скачивания (" + count + ")";
            DeleteSetLabel = "Удалить наборы (" + count + ")";
            desktop.Search.SearchSetEnabled = AppSets.IsEnabled = SetNameEnabled = !busy;
        }
    }
}
