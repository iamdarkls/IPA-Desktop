using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace IpaDesktop;

public sealed partial class DesktopViewModel
{
    internal bool CanPrepareInstallCopies(List<LocalRow> rows) =>
        AccountKey != null && !protection.RequiresLogin &&
        rows.All(row => AppSetRules.ValidId(row.AppStoreID) && AppSetRules.ValidId(row.ExternalVersionID));

    internal async Task<Dictionary<string, LocalRow>> PrepareInstallCopies(List<LocalRow> rows)
    {
        if (!CanPrepareInstallCopies(rows)) return null;
        var expectedAccount = AccountKey;
        var copies = new Dictionary<string, LocalRow>(StringComparer.Ordinal);
        return await RunWorkAsync("Подготовка IPA для текущего аккаунта…", async ct =>
        {
            var email = await RequireAccountAsync(ct, expectedAccount);
            var variants = rows.GroupBy(row => (row.AppStoreID, row.ExternalVersionID)).ToList();
            StartReport("Скачивание", variants.Count, () => Task.CompletedTask);
            QueueBatchViewModel batch = null;
            batch = Queue.Start("Скачивание", variants.Select(group =>
                new QueueSeed(group.Key.AppStoreID + "@" + group.Key.ExternalVersionID, group.First().DisplayName, "ID " + group.Key.ExternalVersionID)),
                null, "Apple Account · " + AccountHint(email), keys => _ = RunAsyncCommand(() => ResumeStoredBatch(batch, keys)), null, expectedAccount);
            var result = await BatchRunner.Run(variants, async (group, token) =>
            {
                var original = group.First();
                var path = await downloads.DownloadFileAsync(new DownloadItem {
                    ID = original.AppStoreID, VersionID = original.ExternalVersionID, Name = original.DisplayName
                }, token);
                var copy = (await localApps.ListAsync(token)).FirstOrDefault(row => row.Path == path);
                if (copy == null || !LocalFileAccounts.Matches(copy, email) ||
                    copy.AppStoreID != original.AppStoreID || copy.ExternalVersionID != original.ExternalVersionID ||
                    group.Any(row => !String.IsNullOrEmpty(row.BundleID) && row.BundleID != copy.BundleID))
                    throw new InvalidDataException("Не удалось подтвердить владельца или точную сборку новой копии IPA.");
                foreach (var row in group) copies.Add(row.Path, copy);
            }, (group, index) => BeginQueue("Скачивание", index, variants.Count, group.First().DisplayName),
                group => ReportItem(group.First().DisplayName, null),
                (group, error) => ReportItem(group.First().DisplayName, error), null, ct);
            CompleteQueue(result.Succeeded, result.Failed.Count);
            if (result.Failed.Count != 0)
                throw new InvalidOperationException("Не все копии подтверждены. Установка не запущена; подробности в очереди.");
        }) ? copies : null;
    }
}
