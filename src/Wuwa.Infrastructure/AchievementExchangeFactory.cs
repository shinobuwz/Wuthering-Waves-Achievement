using Wuwa.Core;

namespace Wuwa.Infrastructure;

public static class AchievementExchangeFactory
{
    public static IAchievementImportSource CreateImport(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => new JsonAchievementExchange(path),
        ".xlsx" or ".tsv" or ".txt" => new ExcelAchievementExchange(path),
        _ => throw new NotSupportedException($"Unsupported exchange file extension '{Path.GetExtension(path)}'.")
    };

    public static IAchievementExportSink CreateExport(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => new JsonAchievementExchange(path),
        ".xlsx" or ".tsv" => new ExcelAchievementExchange(path),
        _ => throw new NotSupportedException($"Unsupported exchange file extension '{Path.GetExtension(path)}'.")
    };
}
