using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RomForge.Core.Services;

public class UsbDetector
{
    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DBT_DEVTYP_VOLUME = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_HDR
    {
        public int dbch_size;
        public int dbch_devicetype;
        public int dbch_reserved;
    }

    public event Action? DeviceChanged;

    public void Register(Window window)
    {
        var windowHandle = new WindowInteropHelper(window).Handle;
        var source = HwndSource.FromHwnd(windowHandle);
        source?.AddHook(HwndHandler);
    }

    private IntPtr HwndHandler(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DEVICECHANGE)
        {
            int changeType = wParam.ToInt32();

            if (changeType == DBT_DEVICEARRIVAL || changeType == DBT_DEVICEREMOVECOMPLETE)
            {
                if (lParam != IntPtr.Zero)
                {
                    var hdr = Marshal.PtrToStructure<DEV_BROADCAST_HDR>(lParam);

                    if (hdr.dbch_devicetype == DBT_DEVTYP_VOLUME)
                        DeviceChanged?.Invoke();
                }
            }
        }

        return IntPtr.Zero;
    }
}