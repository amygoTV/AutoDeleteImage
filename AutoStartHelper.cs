using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace AutoDeleteImage
{
    /// <summary>
    /// 开机自启动设置（通过注册表 HKCU\...\Run）
    /// </summary>
    public static class AutoStartHelper
    {
        private const string RegPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "EasyFindImage";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(RegPath, false))
                    {
                        if (key == null) return false;
                        var val = key.GetValue(AppName);
                        return val != null;
                    }
                }
                catch
                {
                    return false;
                }
            }
        }

        public static void SetEnabled(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegPath, true))
                {
                    if (key == null) return;

                    if (enable)
                    {
                        string exePath = Assembly.GetExecutingAssembly().Location;
                        key.SetValue(AppName, $"\"{exePath}\"");
                    }
                    else
                    {
                        key.DeleteValue(AppName, false);
                    }
                }
            }
            catch
            {
                // 忽略权限等异常
            }
        }
    }
}
