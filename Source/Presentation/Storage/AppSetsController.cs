using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
namespace IpaDesktop
{
    /// <summary>Управляет наборами в обеих вкладках: сохранением, выбором, предпросмотром и добавлением в очередь.</summary>
    public sealed class AppSetsController : WindowController
    {
        public AppSetsController(Desktop view, Engine engine) : base(view, engine)
        {
            C<ListBox>("AppSets").SelectionChanged += delegate { Preview(); ApplyActions(); };
            On("AddToSet", delegate { Save(C<ComboBox>("SearchSet").Text, "SearchSetStatus"); });
            On("SaveSet", delegate { Save(C<TextBox>("SetName").Text, "SetStatus"); });
            On("EditSet", delegate
            {
                var selected = Selected();
                if (selected.Count != 1) return;
                var original = selected[0];
                Work("Открытие набора…", async delegate(CancellationToken ct)
                {
                    var apps = await Task.Run(delegate { ct.ThrowIfCancellationRequested(); return engine.AppSets.ResolveApps(original); }, ct);
                    ct.ThrowIfCancellationRequested();
                    var editor = AppSetEditor.Create(w, original.Name, apps,
                        delegate(string name, IEnumerable<AppRow> rows) { engine.AppSets.Update(original.Name, name, rows); });
                    if (editor.ShowDialog() == true) RefreshSets();
                });
            });
            On("DeleteSet", DeleteSelected);
            On("UseSet", UseSelected);
            RefreshSets();
        }
        /// <summary>Обе кнопки дополняют набор одинаково; повторное имя не уничтожает прежние приложения.</summary>
        void Save(string name, string status)
        {
            name = (name ?? "").Trim();
            try
            {
                var rows = view.SelectedDownloads();
                if (name.Length == 0) throw new InvalidOperationException("Введите название нового набора или выберите существующий.");
                if (rows.Count == 0) throw new InvalidOperationException("Добавьте приложения в выбранное в поиске.");
                engine.AppSets.AddApps(name, rows.Select(x => new AppRow { ID = x.ID, Name = x.Name }));
                RefreshSets();
                C<ComboBox>("SearchSet").Text = name;
                C<TextBlock>(status).Text = "Набор «" + name + "» сохранён. Повторы по ID исключены.";
                view.RegisterPrivate(name);
            }
            catch (Exception e) { C<TextBlock>(status).Text = e.Message; Error(e); }
        }
        /// <summary>Сохраняет выделение по именам после перезагрузки списка, независимо от ссылок на модели.</summary>
        internal void RefreshSets()
        {
            var list = C<ListBox>("AppSets");
            var selected = new HashSet<string>(Selected().Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
            var picker = C<ComboBox>("SearchSet");
            var name = picker.Text;
            try
            {
                var sets = engine.AppSets.Load();
                list.ItemsSource = sets;
                foreach (var set in sets.Where(x => selected.Contains(x.Name))) list.SelectedItems.Add(set);
                if (list.SelectedItems.Count == 0 && sets.Count > 0) list.SelectedIndex = 0;
                picker.ItemsSource = sets.Select(x => x.Name).ToArray(); picker.Text = name;
                C<TextBlock>("SetsCount").Text = sets.Count == 0 ? "Наборов пока нет" : "Сохранено наборов: " + sets.Count;
                Preview(); ApplyActions();
            }
            catch (Exception e) { C<TextBlock>("SetStatus").Text = e.Message; Error(e); }
        }
        List<AppSet> Selected() { return C<ListBox>("AppSets").SelectedItems.Cast<AppSet>().ToList(); }
        void Preview()
        {
            var selected = Selected();
            var apps = selected.Where(x => x.Apps != null).SelectMany(x => x.Apps).GroupBy(x => x.ID).Select(g => g.First()).ToArray();
            C<TextBlock>("SetPreview").Text = selected.Count == 0 ? "Отметьте один или несколько наборов." :
                "Выбрано наборов: " + selected.Count + " · уникальных приложений: " + apps.Length +
                (selected.Any(x => x.Apps == null) ? " · есть наборы прежнего формата" : "") + Environment.NewLine +
                String.Join(", ", apps.Take(12).Select(x => x.Name)) + (apps.Length > 12 ? "…" : "");
        }
        /// <summary>Сначала разрешает все наборы; при ошибке ни один из них не попадает в очередь частично.</summary>
        void UseSelected()
        {
            var selected = Selected();
            if (selected.Count == 0) return;
            Work("Добавление наборов…", async delegate(CancellationToken ct)
            {
                var apps = await Task.Run(delegate { ct.ThrowIfCancellationRequested(); return engine.AppSets.ResolveMany(selected); }, ct);
                ct.ThrowIfCancellationRequested();
                int added = view.AddSetApps(apps);
                RefreshSets();
                foreach (var set in selected) view.RegisterPrivate(set.Name);
                string message = "Наборов: " + selected.Count + ". Добавлено приложений: " + added + ", уже в очереди: " + (apps.Length - added) + ".";
                C<TextBlock>("SetStatus").Text = message;
                C<TextBlock>("SearchSetStatus").Text = message;
                Log(message);
            });
        }
        void DeleteSelected()
        {
            var selected = Selected();
            // Удаление только конфигурации, без изменения IPA на диске или приложений на телефоне.
            engine.AppSets.DeleteMany(selected.Select(x => x.Name));
            RefreshSets(); C<TextBlock>("SetStatus").Text = "Удалено наборов: " + selected.Count + ". IPA сохранены.";
        }
        /// <summary>Пересчитывает доступность действий по состоянию выбора и выполняемой операции.</summary>
        public override void RefreshActions()
        {
            int count = Selected().Count;
            C<Button>("AddToSet").IsEnabled = C<Button>("SaveSet").IsEnabled = !busy && view.SelectedDownloads().Count > 0;
            C<Button>("EditSet").IsEnabled = !busy && count == 1;
            C<Button>("UseSet").IsEnabled = C<Button>("DeleteSet").IsEnabled = !busy && count > 0;
            C<Button>("UseSet").Content = "В очередь скачивания (" + count + ")";
            C<Button>("DeleteSet").Content = "Удалить наборы (" + count + ")";
            C<ComboBox>("SearchSet").IsEnabled = C<ListBox>("AppSets").IsEnabled = C<TextBox>("SetName").IsEnabled = !busy;
        }
    }
}
