using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using WinRT.Interop;

namespace AppleMusicRpc.Services;

public static class IconService
{
    public static IconId GetApplicationIconId()
    {
        // Resource ID 32512 is the default application icon assigned by Visual Studio
        IntPtr iconResourceId = new(32512);

        IntPtr hModule = NativeMethods.GetModuleHandle(null);
        if (hModule == IntPtr.Zero) return default;

        IntPtr hIcon = NativeMethods.LoadIcon(hModule, iconResourceId);
        if (hIcon == IntPtr.Zero) return default;

        return Win32Interop.GetIconIdFromIcon(hIcon);
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll", EntryPoint = "LoadIconW")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr LoadIcon(IntPtr hModule, IntPtr lpIconName);
    }
}
