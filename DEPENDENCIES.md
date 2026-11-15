# Зависимости и ограничения совместимости

Проверено 15 ноября 2026 года. В рабочей версии закреплены стабильные пакеты, успешно прошедшие сборку и проверки приложения. Версии задаются в IPA-Desktop.csproj и Tests/Modern/IPA-Desktop.Tests.csproj.

| Компонент | Версия |
|---|---|
| .NET SDK / runtime | 11.0.100 / 11.0.0 |
| Avalonia / Desktop / Inter / Fluent / Headless | 12.1.6 |
| Avalonia.Controls.DataGrid | 12.1.2 |
| SkiaSharp и нативные пакеты | 4.156.1 |
| HarfBuzzSharp и нативные пакеты | 14.4.0.101 |
| MicroCom.Runtime | 0.11.7 |
| Tmds.DBus.Protocol | 0.95.2 |
| System.ServiceProcess.ServiceController / System.Windows.Extensions | 11.0.0 |
| Microsoft.NET.Test.Sdk / xunit.runner.visualstudio | 18.12.0 / 4.0.1 |
| xunit.v3 / Microsoft.Testing.Platform | 4.0.3 / 2.5.2 |
| xunit.analyzers | 2.3.0 |
| actions/setup-dotnet | 6.1.0 |

## Проверенные исключения

**UI-тесты с xUnit 4:** коллекция Desktop UI запускает тесты через `HeadlessUnitTestSession` из `Avalonia.Headless`. Диспетчер выполняет тест и завершает отложенные операции интерфейса до закрытия приложения. `Avalonia.Headless.XUnit 12.1.6` использует старый API обнаружения тестов xUnit 3.

**Avalonia.Angle.Windows.Natives:** версия 2.2.0.20230930 снята с публикации в NuGet (listed=false). Более высокий номер из flat-container не означает актуальный рекомендуемый выпуск. Используется пакет 2.1.27548.20260419, который выбирает Avalonia; стандартный аудит NuGet не предлагает его обновлять.

Вложенные зависимости тестов закреплены явно, чтобы NuGet не выбирал прежние минимальные версии. Их PrivateAssets=all отделяет их от зависимостей распространяемого приложения.

## Инструменты iPhone и сборка

ipatool основан на стабильном Sorvigolova/ipatool 1.3.2 с нашими защитными изменениями (1.3.2.1). Device-tools 1.0.9 использует libplist 2.8.1, libtatsu 1.1.1, закреплённые исходники, проверенный патч чтения placeholder и собственный ipa-device-events; версии источников и проверенных стабильных релизов перечислены в Tools/DeviceTools/pins.json и stable.json. Новых стабильных выпусков этих upstream-проектов при проверке не найдено.

Нативные зависимости MSYS2 устанавливаются сборкой через pacman с обновлением базы; точные установленные версии записываются в msys2-packages.txt исходного архива комплекта. Тег msys2/setup-msys2@v2 на момент проверки указывает на 2.33.0. Остальные GitHub Actions уже актуальны.

## Повторная проверка

```sh
dotnet package list --project IPA-Desktop.csproj --outdated --include-transitive
dotnet package list --project Tests/Modern/IPA-Desktop.Tests.csproj --outdated --include-transitive
python Scripts/desktop.py test --rid linux-x64
```

Новые основные версии графических библиотек проверяются также запуском готового Windows EXE через Scripts/Verify-Package.ps1. Компиляции недостаточно для проверки бинарной совместимости и загрузки нативных DLL.

Текущая целевая платформа — net11.0. Windows API вызываются только в Windows. Unix-сборки содержат собственные нативные инструменты, приватный PTY и исходные архивы. См. Docs/PLATFORMS.md.
