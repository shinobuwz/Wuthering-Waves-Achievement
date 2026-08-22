using System.Text;

namespace Wuwa.Infrastructure;

/// <summary>Small file logger for diagnosing native OCR and game-input integration.</summary>
public static class NativeOcrDiagnostics
{
    private static readonly object Gate = new();
    private const long MaxLogBytes = 5 * 1024 * 1024;

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WutheringWavesAchievement",
        "native-ocr.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var path = LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
                {
                    File.WriteAllText(path, string.Empty, Encoding.UTF8);
                }

                File.AppendAllText(
                    path,
                    $"{DateTimeOffset.Now:O} [T{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics must never affect OCR or the main application.
        }
    }
}
