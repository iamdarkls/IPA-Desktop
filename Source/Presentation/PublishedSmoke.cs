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
            desktop.C<Button>("Search").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(100);
            desktop.C<Button>("AddChosen").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(desktop.C<ItemsControl>("ChosenApps").ItemCount!=1)throw new Exception("Published search/selection flow failed.");
            await Task.Delay(50);
            var download=desktop.C<Button>("Download");
            if(!download.GetVisualDescendants().OfType<AppIcon>().Any(x=>x.Kind==IconKind.Download) ||
               !download.GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text=="Скачать (1)"))
                throw new Exception("Published icon or live action label failed.");
            if(ErrorLocalizer.Describe("Invalid HostID").Code != FailureCode.Pairing)throw new Exception("Published error localization failed.");
            if(desktop.C<Button>("CheckConnection") == null || desktop.C<Border>("ConnectionNotice").IsVisible)throw new Exception("Published connection UI failed.");
            // Verify the new compiled bindings and commands in the trimmed shipping binary.
            string[] remaining = null;
            desktop.Model.Queue.Start("Скачивание", new[]{ new QueueSeed("one","Queue one","1.0"), new QueueSeed("two","Queue two","2.0") },
                null,"Apple Account", keys => remaining = keys.ToArray());
            desktop.Model.Queue.Begin(1);desktop.Model.Queue.Result(null);
            desktop.Model.Queue.Begin(2);desktop.Model.Queue.Measure(35,"fixture transfer");desktop.Model.Queue.Stop("Отменено",true);
            desktop.C<TabItem>("QueueTab").IsSelected=true;await Task.Delay(100);
            var queue=desktop.C<DataGrid>("OperationQueue");
            if(!queue.GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text=="Queue two"))throw new Exception("Published queue row binding was trimmed.");
            desktop.Model.Queue.ResumeCommand.Execute(null);
            if(remaining == null || !remaining.SequenceEqual(new[]{"two"}))throw new Exception("Published queue resumed a successful item.");
            desktop.Model.AppendJournal("Journal binding fixture");desktop.Model.ShowJournalCommand.Execute(null);await Task.Delay(50);
            var journal=window.OwnedWindows.OfType<JournalWindow>().Single();
            if(!journal.FindControl<TextBox>("JournalText").Text.Contains("Journal binding fixture"))throw new Exception("Published journal binding was trimmed.");
            journal.Close();
            var more=desktop.C<Button>("MoreAccount");more.Flyout.ShowAt(more);await Task.Delay(50);
            if(!desktop.C<Button>("AccountInfo").IsEffectivelyVisible)throw new Exception("Published More menu lost its actions.");
            more.Flyout.Hide();
            // Exercise a reflection-bound row from the shipping binary, not from a test assembly.
            using(var versions=new AppVersionWindow(window,"Smoke","123",new(){new AppVersionRow{ID="100",Version="1.2.3"}},engine.AppVersions,default))
            {
                var pending=versions.Window.ShowDialog<bool>(window);await Task.Delay(100);
                if(!versions.Window.GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text=="1.2.3"))throw new Exception("Published version table binding was trimmed.");
                versions.Window.Close();await pending;
            }
            using(var login=new AppleLoginWindow(window,engine.AppleLogin,default))
            {
                var pending=login.Window.ShowDialog<bool>(window);await Task.Delay(50);
                if(login.PasswordBox.PasswordChar=='\0')throw new Exception("Published password box is not masked.");
                login.Window.Close();await pending;
            }
            File.WriteAllText(Path.Combine(directory,"smoke-ok.txt"),"PASS: published Avalonia startup, settings/sets migration, generated JSON, row binding, private login UI, search, operation queue, More menu and journal window");
            ((IClassicDesktopStyleApplicationLifetime)App.Current.ApplicationLifetime).Shutdown(0);
        }
        catch(Exception error)
        {
            File.WriteAllText(Path.Combine(directory,"smoke-error.txt"),error.ToString());
            ((IClassicDesktopStyleApplicationLifetime)App.Current.ApplicationLifetime).Shutdown(1);
        }
    }
}
