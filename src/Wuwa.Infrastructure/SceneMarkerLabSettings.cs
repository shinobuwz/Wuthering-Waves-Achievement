namespace Wuwa.Infrastructure;

/// <summary>Controls whether the internal marker-lab entry point is exposed.</summary>
public static class SceneMarkerLabSettings
{
    public const string EnvironmentVariableName = "WUWA_SCENE_MARKER_LAB";

    public static bool IsEnabled(bool isDebugBuild, string? configuredValue)
    {
        if (isDebugBuild) return true;
        if (configuredValue is null) return false;
        var normalized = configuredValue.Trim();
        return normalized.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}
