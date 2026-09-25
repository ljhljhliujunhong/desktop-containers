using Microsoft.Win32;

namespace DesktopContainers;

public static class StartupService
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "DesktopContainers";

    public static bool Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return false;
            if (!enabled)
            {
                key.DeleteValue(ValueName, false);
                return true;
            }

            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe)) return false;
            key.SetValue(ValueName, "\"" + exe + "\" --startup");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("startup", ex);
            return false;
        }
    }
}
