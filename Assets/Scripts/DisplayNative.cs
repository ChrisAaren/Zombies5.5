#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Zombies55
{
    // Win32 view of the game window and the monitors: the real window rectangle (including the frame) and the
    // desktop mode of every monitor. Used by DisplayManager to check that the window fits, and by the display test hook.
    static class DisplayNative
    {
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MONITORINFOEX
        {
            public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY; public int dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
        delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT r, IntPtr data);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc p, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc p, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr h, ref MONITORINFOEX mi);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);

        static IntPtr s_Hwnd;

        static IntPtr Hwnd()
        {
            if (s_Hwnd != IntPtr.Zero) return s_Hwnd;
            uint me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            IntPtr found = IntPtr.Zero;
            EnumWindows((h, l) =>
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (pid != me || !IsWindowVisible(h)) return true;
                var sb = new StringBuilder(64); GetClassName(h, sb, 64);
                if (sb.ToString() == "UnityWndClass") { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            s_Hwnd = found;
            return found;
        }

        // The visible window rectangle (extended frame bounds, without the invisible resize borders).
        public static bool TryGetWindowRect(out RectInt rect)
        {
            rect = default; var h = Hwnd(); if (h == IntPtr.Zero) return false;
            RECT r;
            if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf<RECT>()) != 0 && !GetWindowRect(h, out r)) return false;
            rect = new RectInt(r.left, r.top, r.right - r.left, r.bottom - r.top);
            return true;
        }

        public static bool TryGetFrame(out Vector2Int frame)
        {
            frame = default; var h = Hwnd(); if (h == IntPtr.Zero) return false;
            if (!GetWindowRect(h, out var w) || !GetClientRect(h, out var c)) return false;
            frame = new Vector2Int((w.right - w.left) - (c.right - c.left), (w.bottom - w.top) - (c.bottom - c.top));
            return true;
        }

        public static string RawWindowRect()
        {
            var h = Hwnd(); if (h == IntPtr.Zero) return "no window";
            GetWindowRect(h, out var w); DwmGetWindowAttribute(h, 9, out var d, Marshal.SizeOf<RECT>());
            return "GetWindowRect (" + w.left + "," + w.top + " " + (w.right - w.left) + "x" + (w.bottom - w.top) + ") DWM visible (" + d.left + "," + d.top + " " + (d.right - d.left) + "x" + (d.bottom - d.top) + ")";
        }

        // Every distinct (width, height, refresh Hz) the monitor's graphics mode list offers.
        public static List<Vector3Int> Modes(string device)
        {
            var list = new List<Vector3Int>();
            var seen = new HashSet<long>();
            for (int i = 0; i < 4000; i++)
            {
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                if (!EnumDisplaySettings(device, i, ref dm)) break;
                if (dm.dmBitsPerPel < 24) continue;
                long key = ((long)dm.dmPelsWidth << 40) | ((long)dm.dmPelsHeight << 20) | (long)dm.dmDisplayFrequency;
                if (seen.Add(key)) list.Add(new Vector3Int(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency));
            }
            return list;
        }

        public struct MonitorRect { public string Device; public RectInt Bounds; public RectInt Work; public bool Primary; public int W, H, Hz; }

        public static List<MonitorRect> Monitors()
        {
            var list = new List<MonitorRect>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hm, IntPtr hdc, ref RECT rc, IntPtr data) =>
            {
                var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                if (GetMonitorInfo(hm, ref mi))
                {
                    var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                    EnumDisplaySettings(mi.szDevice, -1, ref dm);
                    list.Add(new MonitorRect
                    {
                        Device = mi.szDevice,
                        Bounds = new RectInt(mi.rcMonitor.left, mi.rcMonitor.top, mi.rcMonitor.right - mi.rcMonitor.left, mi.rcMonitor.bottom - mi.rcMonitor.top),
                        Work = new RectInt(mi.rcWork.left, mi.rcWork.top, mi.rcWork.right - mi.rcWork.left, mi.rcWork.bottom - mi.rcWork.top),
                        Primary = (mi.dwFlags & 1) != 0, W = dm.dmPelsWidth, H = dm.dmPelsHeight, Hz = dm.dmDisplayFrequency,
                    });
                }
                return true;
            }, IntPtr.Zero);
            return list;
        }
    }
}
#endif
