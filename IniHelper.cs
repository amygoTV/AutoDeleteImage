using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoDeleteImage
{
    /// <summary>
    /// INI 配置文件读写辅助类
    /// </summary>
    public static class IniHelper
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern int WritePrivateProfileString(string section, string key, string val, string filePath);

        private static readonly string _iniPath;

        static IniHelper()
        {
            _iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
        }

        public static string IniPath => _iniPath;

        public static string GetValue(string section, string key, string defaultValue = "")
        {
            var retVal = new StringBuilder(255);
            GetPrivateProfileString(section, key, defaultValue, retVal, 255, _iniPath);
            return retVal.ToString();
        }

        public static void SetValue(string section, string key, string value)
        {
            WritePrivateProfileString(section, key, value, _iniPath);
        }

        public static int GetInt(string section, string key, int defaultValue = 0)
        {
            try
            {
                return int.Parse(GetValue(section, key, defaultValue.ToString()));
            }
            catch
            {
                return defaultValue;
            }
        }

        public static bool GetBool(string section, string key, bool defaultValue = false)
        {
            try
            {
                return bool.Parse(GetValue(section, key, defaultValue.ToString()));
            }
            catch
            {
                return defaultValue;
            }
        }

        public static double GetDouble(string section, string key, double defaultValue = 0)
        {
            try
            {
                return double.Parse(GetValue(section, key, defaultValue.ToString()));
            }
            catch
            {
                return defaultValue;
            }
        }

        public static void SetValue(string section, string key, int value)
        {
            WritePrivateProfileString(section, key, value.ToString(), _iniPath);
        }

        public static void SetValue(string section, string key, double value)
        {
            WritePrivateProfileString(section, key, value.ToString(), _iniPath);
        }

        public static void SetValue(string section, string key, bool value)
        {
            WritePrivateProfileString(section, key, value.ToString(), _iniPath);
        }
    }
}
