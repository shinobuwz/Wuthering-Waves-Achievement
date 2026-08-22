using System.Globalization;
using System.Text;

namespace Wuwa.Infrastructure;

/// <summary>Small file logger for diagnosing native OCR and game-input integration.</summary>
public static class NativeOcrDiagnostics
{
    private static readonly object Gate = new();
    private const long MaxLogBytes = 5 * 1024 * 1024;
    private const int RetainedDays = 7;
    private const string LogFilePrefix = "native-ocr-";
    private const string LogFileSuffix = ".log";

    public static string LogDirectory => AppPaths.LogDirectory;

    public static string LogPath => GetLogPath(DateTimeOffset.Now);

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var now = DateTimeOffset.Now;
                var directory = LogDirectory;
                var path = GetLogPath(now);
                Directory.CreateDirectory(directory);
                PruneLogs(directory, now.Date);

                if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
                {
                    File.WriteAllText(path, string.Empty, Encoding.UTF8);
                }

                File.AppendAllText(
                    path,
                    $"{now:O} [T{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics must never affect OCR or the main application.
        }
    }

    private static string GetLogPath(DateTimeOffset timestamp) =>
        Path.Combine(LogDirectory, $"{LogFilePrefix}{timestamp:yyyy-MM-dd}{LogFileSuffix}");

    private static void PruneLogs(string directory, DateTime currentDate)
    {
        var cutoff = currentDate.AddDays(-(RetainedDays - 1));
        foreach (var path in Directory.EnumerateFiles(directory, $"{LogFilePrefix}*{LogFileSuffix}"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith(LogFilePrefix, StringComparison.Ordinal) ||
                !DateTime.TryParseExact(
                    name[LogFilePrefix.Length..],
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var logDate) ||
                logDate.Date >= cutoff)
            {
                continue;
            }

            try { File.Delete(path); } catch { }
        }
    }
}
