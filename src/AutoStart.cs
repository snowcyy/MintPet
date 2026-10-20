using System;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MintPet
{
    /// <summary>
    /// Per-user "start with Windows" entry. Uses Task Scheduler at logon; this
    /// launches the unsigned exe directly instead of through Explorer's Run key,
    /// which avoids the "unknown publisher" security warning at boot.
    /// </summary>
    internal static class AutoStart
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "MintPet";
        private const string TaskName = "MintPet";

        public static bool IsEnabled()
        {
            return TaskExists() || RunValueExists();
        }

        public static bool SetEnabled(bool enabled)
        {
            if (enabled)
            {
                if (CreateTask())
                {
                    DeleteRunValue();
                    return true;
                }
                return false;
            }

            DeleteTask();
            DeleteRunValue();
            return true;
        }

        /// <summary>
        /// Create the logon task with an elevated helper (one UAC prompt) and drop
        /// the HKCU Run entry. Used by the "修复开机自启（管理员）" menu item.
        /// </summary>
        public static bool RepairWithElevation()
        {
            if (CreateTask())
            {
                DeleteRunValue();
                return true;
            }

            try
            {
                string exe = Application.ExecutablePath;
                string user = Environment.UserDomainName + "\\" + Environment.UserName;
                string args = "/Create /TN \"" + TaskName + "\" /TR \"" + exe +
                              "\" /SC ONLOGON /RU \"" + user +
                              "\" /RL LIMITED /IT /F";

                ProcessStartInfo info = new ProcessStartInfo("schtasks.exe", args);
                info.UseShellExecute = true;
                info.Verb = "runas";
                info.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process process = Process.Start(info))
                {
                    if (process == null) return false;
                    if (!process.WaitForExit(30000))
                    {
                        try { process.Kill(); } catch { }
                        return false;
                    }
                    if (process.ExitCode == 0)
                    {
                        DeleteRunValue();
                        return true;
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        /// <summary>Move an existing HKCU Run entry to Task Scheduler once.</summary>
        public static void Migrate()
        {
            try
            {
                bool task = TaskExists();
                bool run = RunValueExists();
                if (run && !task)
                {
                    if (CreateTask()) DeleteRunValue();
                }
                else if (run && task)
                {
                    DeleteRunValue();
                }
            }
            catch
            {
            }
        }

        private static bool TaskExists()
        {
            return RunSchtasks("/Query /TN \"" + TaskName + "\"");
        }

        private static bool CreateTask()
        {
            string exe = Application.ExecutablePath;
            // The current install path has no spaces, so a single quoted /TR is
            // understood reliably by schtasks.
            string args = "/Create /TN \"" + TaskName + "\" /TR \"" + exe +
                          "\" /SC ONLOGON /RL LIMITED /F";
            return RunSchtasks(args);
        }

        private static void DeleteTask()
        {
            RunSchtasks("/Delete /TN \"" + TaskName + "\" /F");
        }

        private static bool RunSchtasks(string arguments)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("schtasks.exe", arguments);
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process process = Process.Start(info))
                {
                    if (process == null) return false;
                    if (!process.WaitForExit(6000))
                    {
                        try { process.Kill(); } catch { }
                        return false;
                    }
                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool RunValueExists()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    return key != null && key.GetValue(ValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool SetRunValue()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null) return false;
                    key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"", RegistryValueKind.String);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void DeleteRunValue()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key != null) key.DeleteValue(ValueName, false);
                }
            }
            catch
            {
            }
        }
    }
}
