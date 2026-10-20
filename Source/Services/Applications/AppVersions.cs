using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace IpaDesktop
{
    /// <summary>Получает список быстро, а дорогие сведения — только для выбранных пользователем версий.</summary>
    public sealed class AppVersions
    {
        readonly IAppToolClient tool;
        public AppVersions(IAppToolClient tool) { this.tool = tool; }
        public static List<string> ParseManual(string text)
        {
            var ids = (text ?? "").Split(new[] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
            if (ids.Count == 0 || ids.Count > 50 || ids.Any(x => !AppSetStore.ValidId(x))) throw new ArgumentException("Введите от 1 до 50 числовых ID версий через запятую или пробел (до 20 цифр каждый).");
            return ids;
        }
        public async Task<List<AppVersionRow>> ListAsync(string appId, CancellationToken ct)
        {
            if (!AppSetStore.ValidId(appId)) throw new ArgumentException("Некорректный App Store ID.");
            var result = await tool.RunAsync(new[] { "list-versions", "-i", appId }, ct, false);
            ToolResponse.Ensure(result);
            return ToolResponse.VersionIds(result.Output).Distinct().Reverse().Select(x => new AppVersionRow { ID = x }).ToList();
        }
        public async Task ReadAsync(string appId, AppVersionRow row, CancellationToken ct)
        {
            if (!AppSetStore.ValidId(appId) || !AppSetStore.ValidId(row.ID)) throw new ArgumentException("Некорректный ID.");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    var result = await tool.RunAsync(new[] { "get-version-metadata", "-i", appId, "--external-version-id", row.ID }, timeout.Token, false);
                    ToolResponse.Ensure(result); var data = ToolResponse.Object(result.Output);
                    row.Version = ToolResponse.Get(data, "displayVersion") as string ?? "не указана";
                    row.MinimumOS = ToolResponse.Get(data, "minimumOSVersion") as string ?? "не указана";
                    var platforms = ToolResponse.Get(data, "supportedPlatforms") as object[];
                    row.Platforms = platforms == null ? "не указаны" : String.Join(", ", platforms.OfType<string>().Where(x => !String.IsNullOrWhiteSpace(x)).Distinct());
                    if (String.IsNullOrWhiteSpace(row.Platforms)) row.Platforms = "не указаны";
                    row.State = "Готово";
                }
                catch (OperationCanceledException) { ct.ThrowIfCancellationRequested(); throw new System.IO.IOException("Сведения не получены за 30 секунд. Можно повторить."); }
            }
        }
    }
}
