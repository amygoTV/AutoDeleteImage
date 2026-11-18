using System;
using System.Runtime.InteropServices;

namespace AutoDeleteImage
{
    /// <summary>
    /// 检测电脑空闲时间（无用户输入秒数）
    /// </summary>
    public static class IdleDetector
    {
        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        /// <summary>
        /// 获取系统空闲时间（秒）
        /// </summary>
        public static int GetIdleSeconds()
        {
            try
            {
                var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
                if (!GetLastInputInfo(ref lii))
                    return 0;

                uint tickCount = GetTickCount();
                return (int)((tickCount - lii.dwTime) / 1000);
            }
            catch
            {
                return 0;
            }
        }

        [DllImport("kernel32.dll")]
        private static extern uint GetTickCount();

        /// <summary>
        /// 判断是否已空闲超过指定分钟数
        /// </summary>
        public static bool IsIdleForMinutes(int minutes)
        {
            return GetIdleSeconds() >= minutes * 60;
        }
    }
}
