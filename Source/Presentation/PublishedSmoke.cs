using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace IpaDesktop;

/// <summary>Runs the published, trimmed app against an explicitly isolated fixture directory in CI.</summary>
internal static class PublishedSmoke
{
    private static async Task ExpectUi(Func<bool> check, string message)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!check())
        {
            if (timeout.Elapsed >= TimeSpan.FromSeconds(3)) throw new InvalidOperationException(message);
            await Task.Delay(20);
        }
    }

    internal static async Task Run(Desktop desktop, Engine engine, string directory)
    {
        var window=desktop.Window;
        try
        {
            var stateFile=Path.Combine(directory,"state.json");
            var state=JsonCodec.Deserialize<State>(File.ReadAllText(stateFile));
            if(state.Current!="legacy" || state.SoundsEnabled || state.AutoUpdate)throw new Exception("Existing settings changed during migration.");
            engine.Save();
            var restored=JsonCodec.Deserialize<State>(File.ReadAllText(stateFile));
            if(restored.DeviceCurrent!="devices")throw new Exception("Device state lost in modern JSON round trip.");
            var sets=engine.AppSets.Load();
            if(sets.Count!=1 || sets[0].Apps.Length!=1 || sets[0].Apps[0].ID!="123")throw new Exception("Existing sets were not read.");
            var parsed=ToolResponse.Object("{\"success\":true,\"externalVersionIdentifiers\":[\"100\",\"200\"]}");
            if(!Equals(ToolResponse.Get(parsed,"success"),true) || Engine.VersionIds(JsonCodec.Serialize(parsed)).Count!=2)throw new Exception("Untyped JSON adapter changed.");
            var release=JsonCodec.Deserialize<Release>("{\"tag_name\":\"v1.0.0\",\"assets\":[{\"name\":\"fixture\",\"size\":10}]}");
            if(release.assets.Length!=1 || release.assets[0].size!=10)throw new Exception("Release JSON metadata was trimmed.");
            desktop.C<TextBox>("Query").Text="123";
            desktop.Model.Search.SearchCommand.Execute(null);
            await Task.Delay(100);
            desktop.Model.Search.AddChosenCommand.Execute(null);
            if(desktop.C<ItemsControl>("ChosenApps").ItemCount!=1)throw new Exception("Published search/selection flow failed.");
            await Task.Delay(50);
            var download=desktop.C<Button>("Download");
            if(!download.GetVisualDescendants().OfType<AppIcon>().Any(x=>x.Kind==IconKind.Download) ||
               !download.GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text=="Скачать (1)"))
                throw new Exception("Published icon or live action label failed.");
            if(ErrorLocalizer.Describe("Invalid HostID").Code != FailureCode.Pairing)throw new Exception("Published error localization failed.");
            if(desktop.C<Button>("CheckConnection") == null || desktop.C<Border>("ConnectionNotice").IsVisible)throw new Exception("Published connection UI failed.");
            desktop.Model.Device.Select("fixture",false,true);
            desktop.Model.Device.ShowSnapshot("fixture",new DeviceSnapshot("iPhone16,2","18.0",1073741824,"Служба установки отвечает"));
            desktop.Model.Updates.DeviceUpdateStatus="Fixture: tools verified";
            await Task.Delay(50);
            if(!desktop.C<TextBlock>("PhoneSummary").Text.Contains("iPhone16,2") || desktop.C<TextBlock>("DeviceUpdateStatus").Text!="Fixture: tools verified")
                throw new Exception("Published phone summary or tool update bindings failed.");
            // The macOS upgrade test starts with an operation history from the previous release.
            desktop.SetAccountIdentity("smoke@example.com");
            var operations = desktop.Model.Queue;
            var previousKeys = operations.Items.Select(item => item.Seed.Key).ToArray();
            var pendingBefore = operations.PendingCount;
            var fixtureId = Guid.NewGuid().ToString("N");
            var completedKey = "smoke-completed-" + fixtureId;
            var pendingKey = "smoke-pending-" + fixtureId;
            var pendingName = "Smoke pending " + fixtureId[..8];
            string[] remaining = null;
            operations.Start("Скачивание", new[] { new QueueSeed(completedKey, "Smoke completed", "1.0"),
                new QueueSeed(pendingKey, pendingName, "2.0") }, null, "Apple Account",
                keys => remaining = keys.ToArray(), accountKey: desktop.Model.AccountKey);
            operations.Begin(1); operations.Result(null);
            operations.Begin(2); operations.Measure(35, "fixture transfer"); operations.Stop("Отменено", true);
            var queueTab = desktop.C<TabItem>("QueueTab");
            queueTab.IsSelected = true;
            var expectedBadge = (pendingBefore + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await ExpectUi(() => queueTab.GetVisualDescendants().OfType<TextBlock>()
                .Any(text => text.Name == "PART_Count" && text.Text == expectedBadge),
                "Published queue counter binding failed. Expected " + expectedBadge + " including restored items.");
            var queue = desktop.C<DataGrid>("OperationQueue");
            await ExpectUi(() => queue.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == pendingName),
                "Published queue row binding was trimmed.");
            queue.SelectedItem = operations.Items.Single(item => item.Seed.Key == pendingKey);
            operations.ResumeCommand.Execute(null);
            if (remaining == null || !remaining.SequenceEqual(new[] { pendingKey }))
                throw new Exception("Published queue resumed a successful item.");
            desktop.Model.AppendJournal("Journal binding fixture");desktop.Model.ShowJournalCommand.Execute(null);await Task.Delay(50);
            var journal=window.OwnedWindows.OfType<JournalWindow>().Single();
            if(!journal.FindControl<TextBox>("JournalText").Text.Contains("Journal binding fixture"))throw new Exception("Published journal binding was trimmed.");
            journal.Close();
            var archive=new QueueArchiveStore(engine.Root).Load();
            var savedKeys = archive.Batches.SelectMany(batch => batch.Items).Select(item => item.Seed.Key);
            if (!savedKeys.SequenceEqual(previousKeys.Concat(new[] { completedKey, pendingKey })))
                throw new Exception("Published queue did not preserve existing and new operations.");
            var library=new AppBrowserWindow(window,desktop.Model,"Library fixture",false,()=>true);library.Show(window);
            library.SetRows(new(){new LibraryApp{Name="Published library",ID="123",BundleID="com.fixture",Version="1.0"}},"fixture");await Task.Delay(50);
            if(!library.GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text=="Published library"))throw new Exception("Published library binding was trimmed.");
            library.GetVisualDescendants().OfType<TextBox>().Single(x => x.PlaceholderText?.StartsWith("Поиск по названию") == true).Text = "does not match";
            if(library.Apps.ItemsSource.Cast<LibraryApp>().Any())throw new Exception("Published library search did not update from the input.");
            library.Close();
            var direct=new ProxyConfiguration(()=>new State{ProxyMode="Direct"});
            if(direct.EnvironmentChanges()["NO_PROXY"]!="*")throw new Exception("Published proxy environment failed.");
            var more=desktop.C<Button>("MoreAccount");more.Flyout.ShowAt(more);await Task.Delay(50);
            if(!desktop.C<Button>("AccountInfo").IsEffectivelyVisible)throw new Exception("Published More menu lost its actions.");
            more.Flyout.Hide();
            // Exercise compiled dialog bindings in the trimmed shipping binary.
            using(var versions=new AppVersionWindow(window,"Smoke","123",new(){new AppVersionRow{ID="100",Version="1.2.3"}},engine.AppVersions,default))
            {
                var pending=versions.Window.ShowDialog<bool>(window);await Task.Delay(100);
                if(!versions.Window.GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text=="1.2.3"))throw new Exception("Published version table binding was trimmed.");
                versions.Window.GetVisualDescendants().OfType<TextBox>().Single(x => x.PlaceholderText?.StartsWith("ID версий") == true).Text = "200";
                await versions.Model.InsertCommand.ExecuteAsync();
                if(!versions.Model.Versions.Items.Any(x => x.ID == "200"))throw new Exception("Published manual version input did not update.");
                versions.Window.Close();await pending;
            }
            string savedSetName = null;
            var setEditor = AppSetEditor.Create(window, "Legacy set", sets[0].Apps, (name, _) => savedSetName = name);
            var edit = setEditor.ShowDialog<bool>(window);
            await Task.Delay(100);
            if (!setEditor.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == "Fixture"))
                throw new Exception("Published set editor did not render the app name.");
            setEditor.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "DraftName").Text = "Updated fixture";
            await ((AppSetEditorViewModel)setEditor.DataContext).SaveCommand.ExecuteAsync();
            if (!await edit || savedSetName != "Updated fixture")
                throw new Exception("Published set editor did not save the bound name.");
            using(var login=new AppleLoginWindow(window,engine.AppleLogin,default))
            {
                var pending=login.Window.ShowDialog<bool>(window);await Task.Delay(50);
                if(login.PasswordBox.PasswordChar=='\0')throw new Exception("Published password box is not masked.");
                login.Window.Close();await pending;
            }
            File.WriteAllText(Path.Combine(directory,"smoke-ok.txt"),"PASS: published Avalonia startup, settings/sets migration, generated JSON, compiled dialog bindings, private login UI, search, operation queue, More menu and journal window");
            ((IClassicDesktopStyleApplicationLifetime)App.Current.ApplicationLifetime).Shutdown(0);
        }
        catch(Exception error)
        {
            File.WriteAllText(Path.Combine(directory,"smoke-error.txt"),error.ToString());
            ((IClassicDesktopStyleApplicationLifetime)App.Current.ApplicationLifetime).Shutdown(1);
        }
    }
}
