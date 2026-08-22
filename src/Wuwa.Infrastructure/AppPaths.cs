namespace Wuwa.Infrastructure;

/// <summary>Portable paths for files owned by the application.</summary>
public static class AppPaths
{
    public static string ApplicationDirectory => AppContext.BaseDirectory;

    public static string DataDirectory => Path.Combine(ApplicationDirectory, "data");

    public static string LogDirectory => Path.Combine(ApplicationDirectory, "log");

    public static string WebView2Directory => Path.Combine(DataDirectory, "webview2");
}
