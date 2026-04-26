using Microsoft.Win32;

namespace TimeTracker2K;

internal static class StartupRegistration
{
    private const string ValueName = "TimeTracker2K";
    private const string LegacyValueName = "LoginDurationTracker";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void MigrateLegacyValue()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        var legacyValue = key.GetValue(LegacyValueName) as string;
        if (!string.IsNullOrWhiteSpace(legacyValue) && key.GetValue(ValueName) is null)
        {
            key.SetValue(ValueName, $"\"{Application.ExecutablePath}\"");
        }

        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(ValueName) as string;
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{Application.ExecutablePath}\"");
            return;
        }

        key.DeleteValue(ValueName, throwOnMissingValue: false);
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }
}
