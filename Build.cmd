@echo off
setlocal
cd /d "%~dp0"
if exist "Source\" (
 powershell.exe -NoProfile -ExecutionPolicy Bypass -File Scripts\Sync-Sources.ps1
 if errorlevel 1 exit /b 1
)
set "IPA_FRAMEWORK=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%IPA_FRAMEWORK%\csc.exe" (
 echo Windows x64 and .NET Framework 4.x are required.
 pause
 exit /b 1
)
"%IPA_FRAMEWORK%\csc.exe" /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ /out:IPA-Desktop.exe /win32icon:Assets\App.ico @Scripts\Resources.rsp /reference:"%IPA_FRAMEWORK%\WPF\PresentationFramework.dll" /reference:"%IPA_FRAMEWORK%\WPF\PresentationCore.dll" /reference:"%IPA_FRAMEWORK%\WPF\WindowsBase.dll" /reference:"%IPA_FRAMEWORK%\System.Xaml.dll" /reference:"%IPA_FRAMEWORK%\Microsoft.VisualBasic.dll" /reference:"%IPA_FRAMEWORK%\System.ServiceProcess.dll" /reference:"%IPA_FRAMEWORK%\System.Net.Http.dll" /reference:"%IPA_FRAMEWORK%\System.Web.Extensions.dll" /reference:"%IPA_FRAMEWORK%\System.IO.Compression.dll" /reference:"%IPA_FRAMEWORK%\System.IO.Compression.FileSystem.dll" Generated\Core.cs Generated\IpaMetadata.cs Generated\WindowTheme.cs Generated\GuiUpdater.cs Generated\App.cs
if errorlevel 1 (
 echo Build failed. Please send the error text.
 pause
 exit /b 1
)
echo Build complete.
