using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
[assembly: AvaloniaTestApplication(typeof(IpaDesktop.Tests.TestApp))]
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
namespace IpaDesktop.Tests;
public static class TestApp
{
 public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
public sealed class WindowsFactAttribute : Xunit.FactAttribute
{
 public WindowsFactAttribute([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath=null, [System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber=0) : base(sourceFilePath,sourceLineNumber) { if (!OperatingSystem.IsWindows()) Skip = "Windows integration test"; }
}
public sealed class TempFolder : IDisposable
{
 public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ipa-net10-test-" + Guid.NewGuid().ToString("N"));
 public TempFolder() => Directory.CreateDirectory(Path);
 public void Dispose() { try { Directory.Delete(Path,true); } catch(IOException) { } }
}
