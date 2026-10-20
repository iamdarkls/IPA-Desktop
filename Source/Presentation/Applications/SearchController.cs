using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Microsoft.Win32;

namespace IpaDesktop
{
    /// <summary>Поиск, выбор версий и очередь скачивания; сохраняет снимки ошибок и последнего пакета.</summary>
    public sealed class SearchController : WindowController
    {
        readonly DownloadSelection chosen = new DownloadSelection();
        readonly SearchSession searchSession = new SearchSession();
        readonly DownloadSelection failedDownloads = new DownloadSelection();
        string versionApp;
        List<DownloadItem> lastDownloads = new List<DownloadItem>();
        public SearchController(Desktop view, Engine engine) : base(view, engine)
        {
            C<ComboBox>("VersionList").Items.Add("Последняя версия");
            C<ComboBox>("VersionList").SelectedIndex = 0;
            C<TextBox>("Query").TextChanged += delegate
            {
                searchSession.Change(C<TextBox>("Query").Text);
                if (String.IsNullOrWhiteSpace(C<TextBox>("Query").Text))
                {
                    C<DataGrid>("Apps").ItemsSource = null;
                    ApplyActions();
                }
            };
            On("Search", delegate
            {
                string query = C<TextBox>("Query").Text.Trim();
                if (query.Length == 0) return;
                int request = searchSession.Revision;
                Work("Поиск приложений…", async delegate(CancellationToken ct)
                {
                    if (query.All(Char.IsDigit))
                    {
                        if (searchSession.IsCurrent(request)) C<DataGrid>("Apps").ItemsSource = new[]
                        {
                            new AppRow
                            {
                                Name = "Приложение по ID " + query, ID = query
                            }
                        };
                    } else
                    {
                        var local = await engine.CatalogSearch(query, ct);
                        if (!searchSession.IsCurrent(request)) return;
                        C<DataGrid>("Apps").ItemsSource = local;
                        Log("Найдено в каталогах: " + local.Count);
                        try
                        {
                            var r = await engine.Tool(new[]
                            {
                                "search", query, "--limit", "10"
                            }, ct, false);
                            Engine.Ensure(r);
                            if (!searchSession.IsCurrent(request)) return;
                            C<DataGrid>("Apps").ItemsSource = Engine.MergeApps(local, Engine.ParseApps(r.Output));
                        } catch (OperationCanceledException)
                        {
                            throw;
                        } catch (Exception e)
                        {
                            if (!searchSession.IsCurrent(request)) return;
                            if (local.Count == 0) throw;
                            Log("App Store: " + e.Message + ". Показаны результаты каталога.");
                        }
                    }
                    if (searchSession.IsCurrent(request)) C<DataGrid>("Apps").SelectedIndex = 0;
                });
            });
            C<TextBox>("Query").KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e)
            {
                if (e.Key == System.Windows.Input.Key.Enter && C<Button>("Search").IsEnabled) C<Button>("Search").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            C<DataGrid>("Apps").SelectionChanged += delegate
            {
                versionApp = null;
                C<ComboBox>("VersionList").Items.Clear();
                C<ComboBox>("VersionList").Items.Add("Последняя версия");
                C<ComboBox>("VersionList").SelectedIndex = 0;
                ApplyActions();
            };
            On("Versions", delegate
            {
                var selected = SelectedApps();
                if (selected.Count != 1) throw new Exception("Для списка версий выберите одно приложение.");
                string id = selected[0].ID;
                Work("Получение доступных версий…", async delegate(CancellationToken ct)
                {
                    var r = await engine.Tool(new[]
                    {
                        "list-versions", "-i", id
                    }, ct, false);
                    Engine.Ensure(r);
                    var values = Engine.Get(Engine.Object(r.Output), "externalVersionIdentifiers") as object[];
                    if (values == null) throw new Exception("Список версий не получен: формат ответа не поддерживается.");
                    if (SelectedApps().Count != 1 || SelectedApps()[0].ID != id) throw new Exception("Выбор приложения изменился. Повторите запрос версий.");
                    var combo = C<ComboBox>("VersionList");
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
            On("VersionTable", delegate
            {
                var apps = SelectedApps();
                if (apps.Count != 1) throw new Exception("Для таблицы версий выберите одно приложение.");
                var app = apps[0];
                Work("Получение списка версий…", async ct =>
                {
                    var rows = new List<AppVersionRow>();
                    try { rows = await engine.AppVersions.ListAsync(app.ID, ct); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { Log("Список версий недоступен: " + e.Message + ". Можно указать известный ID вручную."); }
                    ct.ThrowIfCancellationRequested();
                    using (var dialog = new AppVersionWindow(w, app.Name, app.ID, rows, engine.AppVersions, ct))
                    {
                        if (dialog.Window.ShowDialog() == true)
                        {
                            foreach (var id in dialog.SelectedIDs) chosen.AddVariant(app, id);
                            RefreshChosen();
                        }
                    }
                });
            });
            On("VersionInfo", delegate
            {
                var selected = SelectedApps();
                if (selected.Count != 1) throw new Exception("Выберите одно приложение.");
                var row = selected[0];
                var combo = C<ComboBox>("VersionList");
                string version = combo.SelectedIndex > 0 ? Convert.ToString(combo.SelectedItem) : null;
                if (version != null && row.ID != versionApp) throw new Exception("Обновите список версий выбранного приложения.");
                Work("Получение сведений о версии…", async delegate(CancellationToken ct)
                {
                    if (version == null)
                    {
                        var list = await engine.Tool(new[]
                        {
                            "list-versions", "-i", row.ID
                        }, ct, false);
                        Engine.Ensure(list);
                        var ids = Engine.VersionIds(list.Output);
                        if (ids.Count == 0) throw new Exception("Движок не вернул доступных версий приложения.");
                        version = ids[ids.Count - 1];
                        Log("Последняя доступная версия: ID " + version);
                    }
                    var r = await engine.Tool(new[]
                    {
                        "get-version-metadata", "-i", row.ID, "--external-version-id", version
                    }, ct, false);
                    Engine.Ensure(r);
                    var data = Engine.Object(r.Output);
                    AppDialogs.ShowText(w, "Сведения о версии", row.Name + Environment.NewLine + Environment.NewLine +
                        ToolResponse.VersionMetadataText(data) + Environment.NewLine + "ID версии: " + version + Environment.NewLine + "App Store ID: " + row.ID);
                });
            });
            RowClickSelection.Attach(C<DataGrid>("Apps"), delegate(object item)
            {
                if (busy) return;
                var row = item as AppRow;
                if (row == null || chosen.Snapshot().Any(x => x.ID == row.ID)) return;
                var combo = C<ComboBox>("VersionList");
                chosen.AddVariant(row, combo.SelectedIndex > 0 && versionApp == row.ID ? Convert.ToString(combo.SelectedItem) : null);
                RefreshChosen();
            });
            On("AddChosen", delegate
            {
                var rows = SelectedApps();
                var combo = C<ComboBox>("VersionList");
                string version = combo.SelectedIndex > 0 ? Convert.ToString(combo.SelectedItem) : null;
                if (version != null && (rows.Count != 1 || rows[0].ID != versionApp)) throw new Exception("Для конкретной версии выберите одно приложение и обновите список версий.");
                foreach (var row in rows) chosen.AddVariant(row, version);
                RefreshChosen();
            });
            On("ClearChosen", delegate
            {
                chosen.Clear();
                lastDownloads.Clear();
                RefreshChosen();
            });
            SelectionRemove.Attach(C<ItemsControl>("ChosenApps"), delegate(string id)
            {
                if (busy) return;
                chosen.RemoveKey(id);
                RefreshChosen();
            });
            On("Download", delegate { StartDownloads(chosen.Snapshot()); });
            On("RetryDownloads", delegate { StartDownloads(failedDownloads.Snapshot(), false); });
            On("ForgetDownloadErrors", delegate { failedDownloads.Clear(); ApplyActions(); });

        }
        /// <summary>Возвращает текущий выбор, а при пустой очереди — независимый снимок последнего пакета.</summary>
        internal List<DownloadItem> SelectedDownloads()
        {
            return chosen.Count > 0 ? chosen.Snapshot() : lastDownloads.Select(x => new DownloadItem { ID = x.ID, Name = x.Name, VersionID = x.VersionID }).ToList();
        }
        /// <summary>Дополняет очередь ID из наборов и сохраняет уже выбранные версии приложений.</summary>
        internal int AddSetApps(IEnumerable<AppRow> apps)
        {
            int added = chosen.AddMissing(apps);
            RefreshChosen();
            C<TabItem>("SearchTab").IsSelected = true;
            return added;
        }
        /// <summary>Запускает пакет и обновляет отдельные списки успешных и неудачных запросов.</summary>
        void StartDownloads(List<DownloadItem> rows, bool remember = true)
        {
            if (rows.Count == 0) throw new Exception("Добавьте приложения в выбранное.");
            if (remember) lastDownloads = rows.Select(x => new DownloadItem { ID = x.ID, Name = x.Name, VersionID = x.VersionID }).ToList();
            Work("Скачивание IPA…", async delegate(CancellationToken ct)
            {
                view.StartReport("Скачивание", rows.Count, delegate { StartDownloads(failedDownloads.Snapshot(), false); });
                var result = await BatchRunner.Run(rows,
                    (row, token) => engine.ApplicationDownloads.DownloadAsync(row, token),
                    (row, index) => { view.BeginQueue("Скачивание", index, rows.Count, row.Name); Log("В очереди: " + row.Label); },
                    row =>
                    {
                        view.ReportItem(row.Name, null);
                        chosen.RemoveKey(row.Key);
                        failedDownloads.RemoveKey(row.Key);
                        RefreshChosen();
                    },
                    (row, error) =>
                    {
                        view.ReportItem(row.Name, error);
                        failedDownloads.AddVariant(new AppRow { ID = row.ID, Name = row.Name }, row.VersionID);
                        Log("Не удалось скачать " + row.Name + ": " + error.Message);
                    }, RefreshLocalFiles, ct);
                view.CompleteQueue(result.Succeeded, result.Failed.Count);
                if (result.Failed.Count > 0) throw new Exception("Не удалось скачать: " + String.Join(", ", result.Failed.Select(x => x.Name)) + ". Кнопка «Повторить ошибки» повторит только эти приложения.");
            });
        }
        /// <summary>Пересчитывает доступность действий по состоянию выбора и выполняемой операции.</summary>
        public override void RefreshActions()
        {
            C<Button>("RetryDownloads").IsEnabled = !busy && failedDownloads.Count > 0;
            C<Button>("RetryDownloads").Content = "Повторить ошибки (" + failedDownloads.Count + ")";
            C<Button>("RetryDownloads").ToolTip = String.Join(Environment.NewLine, failedDownloads.Snapshot().Select(x => x.Label));
            C<Button>("ForgetDownloadErrors").IsEnabled = !busy && failedDownloads.Count > 0;
            C<Button>("AddChosen").IsEnabled = !busy && C<DataGrid>("Apps").SelectedItems.Count > 0;
            C<Button>("ClearChosen").IsEnabled = !busy && (chosen.Count > 0 || lastDownloads.Count > 0);
            C<Button>("Download").IsEnabled = !busy && chosen.Count > 0;
            C<ItemsControl>("ChosenApps").IsEnabled = !busy;
            C<Button>("VersionTable").IsEnabled = !busy && C<DataGrid>("Apps").SelectedItems.Count == 1;
            C<Button>("VersionInfo").IsEnabled = !busy && C<DataGrid>("Apps").SelectedItems.Count == 1;
        }
        void RefreshChosen()
        {
            C<ItemsControl>("ChosenApps").ItemsSource = chosen.Snapshot();
            C<TextBlock>("ChosenCount").Text = chosen.Count == 0 ? "Добавьте приложения из результатов поиска" : "Выбрано: " + chosen.Count + " · сохраняются при новом поиске";
            C<Button>("Download").Content = "Скачать (" + chosen.Count + ")";
            if (chosen.Count == 0 && lastDownloads.Count > 0) C<TextBlock>("SearchSetStatus").Text = "Для набора: приложения последней загрузки (" + lastDownloads.Count + ").";
            else C<TextBlock>("SearchSetStatus").Text = chosen.Count > 0 ? "Для набора выбрано: " + chosen.Count + ". Введите название или выберите набор." : "Выберите приложения, затем введите название набора.";
            ApplyActions();
        }
        List<AppRow> SelectedApps()
        {
            var rows = C<DataGrid>("Apps").SelectedItems.Cast<AppRow>().ToList();
            if (rows.Count == 0) throw new Exception("Выберите приложение в таблице.");
            return rows;
        }
    }
}
