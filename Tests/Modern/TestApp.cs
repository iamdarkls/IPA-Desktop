using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Xunit;
[assembly: Xunit.v3.Parallelization(MaxThreads = 4)]
namespace IpaDesktop.Tests;
public static class TestApp
{
 public static AppBuilder BuildAvaloniaApp() => Program.CreateAppBuilder().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
[CollectionDefinition("Desktop UI", DisableParallelization = true)]
public sealed class DesktopUiCollection : ICollectionFixture<DesktopUiSession> { }

public sealed class DesktopUiSession : IAsyncDisposable
{
 private readonly HeadlessUnitTestSession session =
     HeadlessUnitTestSession.StartNew(typeof(TestApp), AvaloniaTestIsolationLevel.PerTest);

 public Task Run(Func<Task> test) => session.Dispatch(async () =>
 {
  await test();
  // Drain pending layout and render jobs before Avalonia tears down the test application.
  Dispatcher.UIThread.RunJobs();
  return true;
 }, TestContext.Current.CancellationToken);

 public ValueTask DisposeAsync() => session.DisposeAsync();
}
public sealed class TempFolder : IDisposable
{
 public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ipa-desktop-test-" + Guid.NewGuid().ToString("N"));
 public TempFolder() => Directory.CreateDirectory(Path);
 public void Dispose() { try { Directory.Delete(Path,true); } catch(IOException) { } }
}
