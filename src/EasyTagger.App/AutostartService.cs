using Microsoft.Win32;

namespace EasyTagger.App;

static class AutostartService
{
    const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Easy Tagger";

    public static bool IsEnabled => ReadCommand() is not null;

    public static bool TrySet(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key == null)
                return false;
            if (enabled)
                key.SetValue(ValueName, TrayCommand, RegistryValueKind.String);
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    public static void RefreshPath()
    {
        var current = ReadCommand();
        if (current is not null && !string.Equals(current, TrayCommand, StringComparison.OrdinalIgnoreCase))
            TrySet(true);
    }

    static string TrayCommand => $"\"{ExePath()}\" --tray";

    static string ExePath() =>
        Environment.ProcessPath
        ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
        ?? "";

    static string? ReadCommand()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            var value = key?.GetValue(ValueName) as string;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return null;
        }
    }
}
