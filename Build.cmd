@echo off
setlocal
cd /d "%~dp0"
dotnet publish IPA-Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts\publish
if errorlevel 1 exit /b 1
echo Build complete: artifacts\publish\IPA-Desktop.exe
