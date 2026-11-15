using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop;

public sealed partial class FilesViewModel
{
    /// <summary>Подтверждает выбранное устройство и запускает последовательную установку снимка очереди.</summary>
    internal Task StartInstall(List<LocalRow> rows, QueueBatchViewModel resumeBatch = null, string expectedDevice = null,
        string expectedAccount = null, string accountEmail = null, IReadOnlyDictionary<string, string> sources = null, int cacheCount = 0) =>
        desktop.RunAsyncCommand(() => StartInstallCore(rows, resumeBatch, expectedDevice, expectedAccount ?? resumeBatch?.AccountKey, accountEmail, sources, cacheCount));
    async Task StartInstallCore(List<LocalRow> rows, QueueBatchViewModel resumeBatch, string expectedDevice,
        string expectedAccount, string accountEmail, IReadOnlyDictionary<string, string> sources, int cacheCount)
    {
        var device = DeviceList.SelectedItem as DeviceRow;
        if (device == null || rows.Count == 0) throw new Exception("Выберите устройство и IPA для установки.");
        if (busy) return;
        if (expectedDevice != null && !String.Equals(ConnectionKey(device), expectedDevice, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Выбран другой iPhone. Повторите подготовку набора.");
        if (resumeBatch != null && !CanResume(resumeBatch.TargetKey)) throw new Exception("Для продолжения восстановите связь с тем же iPhone.");
        if (resumeBatch != null && !desktop.CanUseBatchAccount(resumeBatch)) return;
        if (expectedAccount != null && expectedAccount != desktop.AccountKey) throw new InvalidOperationException("Apple Account сменился. Подготовьте набор заново.");
        string recoveryDetail = "";
        if (resumeBatch != null)
        {
            var uncertain = resumeBatch.Items.Where(item => item.State is QueueItemState.Uncertain or QueueItemState.Interrupted)
                .ToDictionary(item => item.Seed.Key, StringComparer.Ordinal);
            if (rows.Any(row => uncertain.ContainsKey(row.Path)))
            {
                int verified = 0;
                var remaining = new List<string>();
                if (!await desktop.RunWorkAsync("Сверка результатов на исходном iPhone…", async ct =>
                {
                    ILookup<string, LibraryApp> installed;
                    try
                    {
                        await deviceService.ValidateReadyAsync(device, ct);
                        installed = (await deviceService.ApplicationsAsync(device, ct))
                            .ToLookup(app => app.BundleID, StringComparer.OrdinalIgnoreCase);
                    }
                    catch (DeviceConnectionException error) { MarkConnectionLost(device, error); throw; }
                    foreach (var row in rows)
                    {
                        if (!uncertain.TryGetValue(row.Path, out var item)) continue;
                        var difference = DeviceApplications.Compare(row, installed[row.BundleID].FirstOrDefault(), requireBuild: true);
                        if (difference != null) { remaining.Add(row.DisplayName + ": " + difference); continue; }
                        desktop.Queue.AcceptInstalled(item, automatic: true);
                        ResolveVerified(row.Path);
                        verified++;
                    }
                    desktop.ShowRecoveredInstallation(resumeBatch);
                })) return;
                rows = rows.Where(row => !uncertain.TryGetValue(row.Path, out var item) || item.State != QueueItemState.Succeeded).ToList();
                if (rows.Count == 0) return;
                if (verified > 0) recoveryDetail += "\nПроверено и пропущено: " + verified + ".";
                if (remaining.Count > 0)
                    recoveryDetail += "\nТребуют ручного повтора:\n" + String.Join("\n", remaining);
            }
        }
        var account = desktop.CurrentAccountEmail;
        var mismatched = rows.Where(row => account != null && LocalFileAccounts.Normalize(row.AppleAccount).Length != 0 &&
            !LocalFileAccounts.Matches(row, account)).ToList();
        if (mismatched.Count > 0)
        {
            bool canPrepare = desktop.CanPrepareInstallCopies(mismatched);
            var message = "Текущий аккаунт: " + account + "\n\n" + SummarizeInstall(mismatched) +
                "\n\nЭти IPA собраны для других аккаунтов. iPhone может запросить исходный аккаунт при установке или запуске." +
                (canPrepare ? "\nМожно подготовить копии той же сборки для текущего аккаунта." :
                    "\nПодготовка недоступна: для части файлов не указан точный ID сборки или требуется заново войти.");
            var choice = await desktop.Interaction.ChooseInstallAccount(message, canPrepare);
            if (choice == InstallAccountChoice.Cancel) return;
            if (choice == InstallAccountChoice.PrepareForCurrent)
            {
                var copies = await desktop.PrepareInstallCopies(mismatched);
                if (copies == null) return;
                rows = rows.Select(row => copies.GetValueOrDefault(row.Path, row)).DistinctBy(row => row.Path).ToList();
                resumeBatch = null; // Prepared copies are new files with their own queue identity.
            }
        }
        var summary = sources == null ? SummarizeInstall(rows) : String.Join(Environment.NewLine, rows.Select(row =>
            row.DisplayName + " · " + (row.Version ?? "версия не указана") + " · " + sources[row.Path]));
        int unknown = rows.Count(row => LocalFileAccounts.Normalize(row.AppleAccount).Length == 0);
        if (unknown > 0) summary += "\n\nВладелец не определён для IPA: " + unknown + ".";
        var header = "Установить IPA (" + rows.Count + ") на " + device.DisplayLabel + "?";
        if (accountEmail != null || expectedAccount != null)
            header += "\nApple Account: " + (accountEmail ?? account);
        if (resumeBatch?.Items.Any(row => row.State is QueueItemState.Uncertain or QueueItemState.Interrupted) == true)
            header += "\nТолько выбранные оставшиеся IPA будут установлены.";
        header += recoveryDetail;
        if (!await desktop.Interaction.Confirm("Установка", header + "\n\n" + summary)) return;
        if (DeviceList.SelectedItem is not DeviceRow selected || ConnectionKey(selected) != ConnectionKey(device))
            throw new InvalidOperationException("Выбранный iPhone изменился. Выберите исходное устройство и повторите.");
        if (resumeBatch != null && !desktop.Queue.CanResumeBatch(resumeBatch)) return;
        await desktop.RunWorkAsync("Установка IPA…", async delegate (CancellationToken ct)
        {
            if (expectedAccount != null) await desktop.RequireAccountAsync(ct, expectedAccount);
            desktop.StartReport("Установка", rows.Count, null);
            QueueBatchViewModel batch = null;
            batch = desktop.Queue.Start("Установка", rows.Select(row => Seed(row)), ConnectionKey(device), device.DisplayLabel,
                keys => _ = desktop.RunAsyncCommand(() => desktop.ResumeStoredBatch(batch, keys)), resumeBatch, expectedAccount, cacheCount);
            desktop.AttachInstallationReport(batch);
            try
            {
                await CheckSupport(ct);
                await EnsureConnection(device, ct);
                var result = await BatchRunner.Run(rows,
                    (row, token) => deviceService.InstallAsync(device, row, ShowConnection, token),
                    (row, index) => { desktop.BeginQueue("Установка", index, rows.Count, row.DisplayName); Log("Установка " + row.DisplayName); },
                    row => { desktop.ReportItem(row.DisplayName, null); installChosen.Remove(row.Path); failedInstalls.Remove(row.Path); RefreshInstallChosen(); },
                    (row, error) => { desktop.ReportItem(row.DisplayName, error); failedInstalls.Add(row); Log("Ошибка установки " + row.DisplayName + ": " + error.Message); },
                    null, ct);
                desktop.CompleteQueue(result.Succeeded, result.Failed.Count);
                if (result.Failed.Count > 0) throw new Exception("Не удалось установить: " + String.Join(", ", result.Failed.Select(x => x.DisplayName)) + ". Продолжить можно из очереди.");
            }
            catch (DeviceConnectionException error)
            {
                MarkConnectionLost(device, error);
                throw;
            }
        });
    }
    /// <summary>В подтверждении различает одноимённые копии разных версий и аккаунтов.</summary>
    static string SummarizeInstall(List<LocalRow> rows) =>
        String.Join(Environment.NewLine, rows.Take(8).Select(SelectionLabel)) +
        (rows.Count > 8 ? Environment.NewLine + "… и ещё " + (rows.Count - 8) : "");

    static string SelectionLabel(LocalRow row)
    {
        return row.DisplayName + (String.IsNullOrWhiteSpace(row.Version) ? "" : " · " + row.Version) + " · " +
            (LocalFileAccounts.Normalize(row.AppleAccount).Length == 0 ? "владелец не определён" : row.AppleAccount);
    }
}
