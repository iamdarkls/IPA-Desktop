# Зависимости и ограничения совместимости

Проверено 6 ноября 2026 года. В рабочей версии закреплены стабильные пакеты, успешно прошедшие сборку и проверки приложения. Версии задаются в IPA-Desktop.csproj и Tests/Modern/IPA-Desktop.Tests.csproj.

| Компонент | Версия |
|---|---|
| .NET SDK / runtime | 10.0.403 / 10.0.14 |
| Avalonia / Desktop / Fluent / Headless | 12.1.5 |
| Avalonia.Controls.DataGrid | 12.1.2 |
| SkiaSharp и нативные пакеты | 4.154.0 |
| HarfBuzzSharp и нативные пакеты | 14.2.1.400 |
| MicroCom.Runtime | 0.11.7 |
| Tmds.DBus.Protocol | 0.95.2 |
| System.ServiceProcess.ServiceController / System.Windows.Extensions | 10.0.14 |
| Microsoft.NET.Test.Sdk / xunit.runner.visualstudio | 18.11.0 / 4.0.1 |
| xunit.v3 / Microsoft.Testing.Platform | 3.2.2 / 1.9.1 |
| xunit.analyzers | 2.2.0 |
| actions/setup-dotnet | 6.1.0 |

## Проверенные исключения

**xUnit 4.0.2:** сборка успешна после перехода на новый ParallelizationAttribute, но Avalonia.Headless.XUnit 12.1.5 не обнаруживает UI-тесты: MissingMethodException в Xunit.v3.TestIntrospectionHelper.GetTestCaseDetails. Поэтому остаются xUnit 3.2.2 и совместимая с ним ветка Microsoft.Testing.Platform 1.9.1. Обновление допускается после поддержки нового API адаптером Avalonia; отключать или заменять UI-тесты ради номера версии не нужно.

**Avalonia.Angle.Windows.Natives:** версия 2.2.0.20230930 снята с публикации в NuGet (listed=false). Более высокий номер из flat-container не означает актуальный рекомендуемый выпуск. Используется пакет 2.1.27548.20260419, который выбирает Avalonia; стандартный аудит NuGet не предлагает его обновлять.

Вложенные зависимости тестов закреплены явно, чтобы NuGet не выбирал прежние минимальные версии. Их PrivateAssets=all отделяет их от зависимостей распространяемого приложения.

## Инструменты iPhone и сборка

ipatool основан на стабильном Sorvigolova/ipatool 1.3.1 с нашими защитными изменениями (1.3.1.1). Device-tools 1.0.8 использует libplist 2.8.1, libtatsu 1.1.0, закреплённые исходники, проверенный патч чтения placeholder и собственный ipa-device-events; версии источников и проверенных стабильных релизов перечислены в Tools/DeviceTools/pins.json и stable.json. Новых стабильных выпусков этих upstream-проектов при проверке не найдено.

Нативные зависимости MSYS2 устанавливаются сборкой через pacman с обновлением базы; точные установленные версии записываются в msys2-packages.txt исходного архива комплекта. Тег msys2/setup-msys2@v2 на момент проверки указывает на 2.33.0. Остальные GitHub Actions уже актуальны.

## Повторная проверка

```sh
dotnet list IPA-Desktop.csproj package --outdated --include-transitive
dotnet list Tests/Modern/IPA-Desktop.Tests.csproj package --outdated --include-transitive
dotnet build Tests/Modern/IPA-Desktop.Tests.csproj -c Release
dotnet test Tests/Modern/IPA-Desktop.Tests.csproj -c Release --no-build --no-restore
```

Новые основные версии графических библиотек проверяются также запуском готового Windows EXE через Scripts/Verify-Package.ps1. Компиляции недостаточно для проверки бинарной совместимости и загрузки нативных DLL.

В версии 1.0 целевая платформа .NET изменена на net10.0; Windows API вызываются только в Windows. Unix-сборки содержат собственные нативные инструменты, приватный PTY и исходные архивы. См. Docs/PLATFORMS.md.
