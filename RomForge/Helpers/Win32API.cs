using System.Runtime.InteropServices;

namespace RomForge.Helpers;

public static class Win32API
{
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
