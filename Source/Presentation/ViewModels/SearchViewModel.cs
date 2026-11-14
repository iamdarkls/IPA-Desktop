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
    /// <summary>Поиск, выбор версий и очередь скачивания; сохраняет снимки ошибок и последнего пакета.</summary>
    public sealed partial class SearchViewModel : FeatureViewModel

    {
        readonly DownloadSelection chosen = new DownloadSelection();
        readonly SearchSession searchSession = new SearchSession();
        readonly DownloadSelection failedDownloads = new DownloadSelection();
        public OperationCommand AddRowCommand { get; private set; }
        public OperationCommand RemoveChosenCommand { get; private set; }
        string versionApp;
        List<DownloadItem> lastDownloads = new List<DownloadItem>();
        readonly IAppToolClient tools;
        readonly IAppCatalog catalog;
        readonly IAppVersions versions;
        readonly IApplicationDownloads downloads;
        readonly IAccountProtection protection;
        public SearchViewModel(DesktopViewModel desktop, IAppToolClient tools, IAppCatalog catalog, IAppVersions versions, IApplicationDownloads downloads, IAccountProtection protection) : base(desktop) { this.tools = tools; this.catalog = catalog; this.versions = versions; this.downloads = downloads; this.protection = protection; }
        internal void Initialize()
        {
            VersionList.Items.Add("Последняя версия");
            VersionList.SelectedIndex = 0;
            PropertyChanged += (_, change) =>
            {
                if (change.PropertyName != nameof(Query)) return;
                searchSession.Change(Query);
                if (String.IsNullOrWhiteSpace(Query)) { Apps.Items = null; ApplyActions(); }
            };
            SearchCommand = Command(delegate
            {
                string query = (Query ?? "").Trim();
                if (query.Length == 0) return;
                int request = searchSession.Revision;
                Work("Поиск приложений…", async delegate (CancellationToken ct)
                {
                    if (query.All(Char.IsDigit))
                    {
                        if (searchSession.IsCurrent(request)) Apps.Items = new[]
                        {
                            new AppRow
                            {
                                Name = "Приложение по ID " + query, ID = query
                            }
                        };
                    }
                    else
                    {
                        var local = await catalog.SearchAsync(query, ct);
                        if (!searchSession.IsCurrent(request)) return;
                        Apps.Items = local;
                        Log("Найдено в каталогах: " + local.Count);
                        try
                        {
                            var r = await tools.RunAsync(new[]
                            {
                                "search", query, "--limit", "10"
                            }, ct, false);
                            ToolResponse.Ensure(r);
                            if (!searchSession.IsCurrent(request)) return;
                            Apps.Items = ToolResponse.MergeApps(local, ToolResponse.ParseApps(r.Output));
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception e)
                        {
                            if (!searchSession.IsCurrent(request)) return;
                            if (local.Count == 0) throw;
                            Log("App Store: " + e.Message + ". Показаны результаты каталога.");
                        }
                    }
                    if (searchSession.IsCurrent(request)) Apps.SelectedIndex = 0;
                });
            });
            Apps.SelectionChanged += delegate
            {
                versionApp = null;
                VersionList.Items.Clear();
                VersionList.Items.Add("Последняя версия");
                VersionList.SelectedIndex = 0;
                ApplyActions();
            };
            VersionsCommand = Command(delegate
            {
                var selected = SelectedApps();
                if (selected.Count != 1) throw new Exception("Для списка версий выберите одно приложение.");
                string id = selected[0].ID;
                Work("Получение доступных версий…", async delegate (CancellationToken ct)
                {
                    var r = await tools.RunAsync(new[]
                    {
                        "list-versions", "-i", id
                    }, ct, false);
                    ToolResponse.Ensure(r);
                    var values = ToolResponse.Get(ToolResponse.Object(r.Output), "externalVersionIdentifiers") as object[];
                    if (values == null) throw new Exception("Список версий не получен: формат ответа не поддерживается.");
                    if (SelectedApps().Count != 1 || SelectedApps()[0].ID != id) throw new Exception("Выбор приложения изменился. Повторите запрос версий.");
                    var combo = VersionList;
                    combo.Items.Clear();
                    combo.Items.Add("Последняя версия");
                    foreach (var v in values.Reverse())
                    {
                        var s = Convert.ToString(v);
                        if (s.Length > 0 && s.All(Char.IsDigit)) combo.Items.Add(s);
                    }
                    combo.SelectedIndex = 0;
                    versionApp = id;
                    Log("Версий: " + values.Length + ". В списке отображаются идентификаторы Apple.");
                });
            });
            VersionTableCommand = Command(delegate
            {
                var apps = SelectedApps();
                if (apps.Count != 1) throw new Exception("Для таблицы версий выберите одно приложение.");
                var app = apps[0];
                Work("Получение списка версий…", async ct =>
                {
                    var rows = new List<AppVersionRow>();
                    try { rows = await versions.ListAsync(app.ID, ct); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { Log("Список версий недоступен: " + e.Message + ". Можно указать известный ID вручную."); }
                    ct.ThrowIfCancellationRequested();
                    var selected = await desktop.Interaction.SelectVersions(app.Name, app.ID, rows, versions, ct);
                    if (selected != null) { foreach (var id in selected) chosen.AddVariant(app, id); RefreshChosen(); }
                });
            });
            VersionInfoCommand = Command(delegate
            {
                var selected = SelectedApps();
                if (selected.Count != 1) throw new Exception("Выберите одно приложение.");
                var row = selected[0];
                var combo = VersionList;
                string version = combo.SelectedIndex > 0 ? Convert.ToString(combo.SelectedItem) : null;
                if (version != null && row.ID != versionApp) throw new Exception("Обновите список версий выбранного приложения.");
                Work("Получение сведений о версии…", async delegate (CancellationToken ct)
                {
                    if (version == null)
                    {
                        var list = await tools.RunAsync(new[]
                        {
                            "list-versions", "-i", row.ID
                        }, ct, false);
                        ToolResponse.Ensure(list);
                        var ids = ToolResponse.VersionIds(list.Output);
                        if (ids.Count == 0) throw new Exception("Движок не вернул доступных версий приложения.");
                        version = ids[ids.Count - 1];
                        Log("Последняя доступная версия: ID " + version);
                    }
                    var r = await tools.RunAsync(new[]
                    {
                        "get-version-metadata", "-i", row.ID, "--external-version-id", version
                    }, ct, false);
                    ToolResponse.Ensure(r);
                    var data = ToolResponse.Object(r.Output);
                    await desktop.Interaction.ShowText("Сведения о версии", row.Name + Environment.NewLine + Environment.NewLine +
                        ToolResponse.VersionMetadataText(data) + Environment.NewLine + "ID версии: " + version + Environment.NewLine + "App Store ID: " + row.ID);
                });
            });
            AddRowCommand = new OperationCommand(item =>
            {
                if (busy) return Task.CompletedTask;
                var row = item as AppRow;
                if (row == null || chosen.Snapshot().Any(x => x.ID == row.ID)) return Task.CompletedTask;
                var combo = VersionList;
                chosen.AddVariant(row, combo.SelectedIndex > 0 && versionApp == row.ID ? Convert.ToString(combo.SelectedItem) : null);
                RefreshChosen(); return Task.CompletedTask;
            }, Error);
            AddChosenCommand = Command(delegate
            {
                var rows = SelectedApps();
                var combo = VersionList;
                string version = combo.SelectedIndex > 0 ? Convert.ToString(combo.SelectedItem) : null;
                if (version != null && (rows.Count != 1 || rows[0].ID != versionApp)) throw new Exception("Для конкретной версии выберите одно приложение и обновите список версий.");
                foreach (var row in rows) chosen.AddVariant(row, version);
                RefreshChosen();
            });
            ClearChosenCommand = Command(delegate
            {
                chosen.Clear();
                lastDownloads.Clear();
                RefreshChosen();
            });
            RemoveChosenCommand = new OperationCommand(key =>
            {
                if (!busy) { chosen.RemoveKey(Convert.ToString(key)); RefreshChosen(); }
                return Task.CompletedTask;
            }, Error);
            DownloadCommand = Command(delegate { StartDownloads(chosen.Snapshot()); });
            RetryDownloadsCommand = Command(delegate { StartDownloads(failedDownloads.Snapshot(), false); });
            ForgetDownloadErrorsCommand = Command(delegate { failedDownloads.Clear(); ApplyActions(); });

        }
        /// <summary>Возвращает текущий выбор, а при пустой очереди — независимый снимок последнего пакета.</summary>
        internal void ResetClientSelection()
        {
            chosen.Clear(); lastDownloads.Clear(); failedDownloads.Clear();
            Apps.SelectedItems.Clear();
            Query = ""; SearchSet = ""; SearchSetStatus = "";
            RefreshChosen();
        }
        internal List<DownloadItem> SelectedDownloads()
        {
            return chosen.Count > 0 ? chosen.Snapshot() : lastDownloads.Select(x => new DownloadItem { ID = x.ID, Name = x.Name, VersionID = x.VersionID }).ToList();
        }
        /// <summary>Дополняет очередь ID из наборов и сохраняет уже выбранные версии приложений.</summary>
        internal int AddSetApps(IEnumerable<AppRow> apps)
        {
            int added = chosen.AddMissing(apps);
            RefreshChosen();
            desktop.SelectedTab = 0;
            return added;
        }
        /// <summary>Запускает пакет и обновляет отдельные списки успешных и неудачных запросов.</summary>
        internal void StartDownloads(List<DownloadItem> rows, bool remember = true, QueueBatchViewModel resumeBatch = null)
        {
            if (rows.Count == 0) throw new Exception("Добавьте приложения в выбранное.");
            if (remember) lastDownloads = rows.Select(x => new DownloadItem { ID = x.ID, Name = x.Name, VersionID = x.VersionID }).ToList();
            Work("Скачивание IPA…", async delegate (CancellationToken ct)
            {
                var accountEmail = await desktop.RequireAccountAsync(ct, resumeBatch?.AccountKey);
                desktop.StartReport("Скачивание", rows.Count, async () => { StartDownloads(failedDownloads.Snapshot(), false); await desktop.ActiveOperation; });
                QueueBatchViewModel batch = null;
                batch = desktop.Queue.Start("Скачивание", rows.Select(row => new QueueSeed(row.Key, row.Name,
                    row.VersionID == null ? "Последняя доступная" : "ID " + row.VersionID)), null, "Apple Account · " + DesktopViewModel.AccountHint(accountEmail),
                    keys => StartDownloads(rows.Where(row => keys.Contains(row.Key)).ToList(), false, batch), resumeBatch, desktop.AccountKey);
                var result = await BatchRunner.Run(rows,
                    (row, token) => downloads.DownloadAsync(row, token),
                    (row, index) => { desktop.BeginQueue("Скачивание", index, rows.Count, row.Name); Log("В очереди: " + row.Label); },
                    row =>
                    {
                        desktop.ReportItem(row.Name, null);
                        chosen.RemoveKey(row.Key);
                        failedDownloads.RemoveKey(row.Key);
                        RefreshChosen();
                    },
                    (row, error) =>
                    {
                        desktop.ReportItem(row.Name, error);
                        failedDownloads.AddVariant(new AppRow { ID = row.ID, Name = row.Name }, row.VersionID);
                        Log("Не удалось скачать " + row.Name + ": " + error.Message);
                    }, RefreshLocalFiles, ct);
                desktop.CompleteQueue(result.Succeeded, result.Failed.Count);
                if (result.Failed.Count > 0) throw new Exception("Не удалось скачать: " + String.Join(", ", result.Failed.Select(x => x.Name)) + ". Кнопка «Повторить ошибки» повторит только эти приложения.");
            });
        }
        /// <summary>Пересчитывает доступность действий по состоянию выбора и выполняемой операции.</summary>
        public override void RefreshActions()
        {
            SearchEmptyVisible = Apps.Items == null || !Apps.Items.Cast<object>().Any();
            RetryDownloadsEnabled = !busy && failedDownloads.Count > 0 && !protection.RequiresLogin;
            RetryDownloadsLabel = "Повторить ошибки (" + failedDownloads.Count + ")";
            RetryDownloadsTip = String.Join(Environment.NewLine, failedDownloads.Snapshot().Select(x => x.Label));
            ForgetDownloadErrorsEnabled = !busy && failedDownloads.Count > 0;
            AddChosenEnabled = !busy && Apps.SelectedItems.Count > 0;
            ClearChosenEnabled = !busy && (chosen.Count > 0 || lastDownloads.Count > 0);
            DownloadEnabled = !busy && chosen.Count > 0 && !protection.RequiresLogin;
            ChosenAppsEnabled = !busy;
            VersionTableEnabled = !busy && Apps.SelectedItems.Count == 1;
            VersionInfoEnabled = !busy && Apps.SelectedItems.Count == 1;
        }
        void RefreshChosen()
        {
            ChosenAppsOptions = chosen.Snapshot();
            SearchSelectionVisible = chosen.Count > 0;
            ChosenCount = chosen.Count == 0 ? "Добавьте приложения из результатов поиска" : "Выбрано: " + chosen.Count + " · сохраняются при новом поиске";
            DownloadLabel = "Скачать (" + chosen.Count + ")";
            if (chosen.Count == 0 && lastDownloads.Count > 0) SearchSetStatus = "Для набора: приложения последней загрузки (" + lastDownloads.Count + ").";
            else SearchSetStatus = chosen.Count > 0 ? "Для набора выбрано: " + chosen.Count + ". Введите название или выберите набор." : "Выберите приложения, затем введите название набора.";
            ApplyActions();
        }
        List<AppRow> SelectedApps()
        {
            var rows = Apps.SelectedItems.Cast<AppRow>().ToList();
            if (rows.Count == 0) throw new Exception("Выберите приложение в таблице.");
            return rows;
        }
    }
}
