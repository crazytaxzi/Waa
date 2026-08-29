using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Waa.App.Infrastructure;

public static class WindowThemeHelper
{
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmUseImmersiveDarkMode = 20;

    public static void SetDarkTitleBar(Window window, bool darkMode)
    {
        ArgumentNullException.ThrowIfNull(window);

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var enabled = darkMode ? 1 : 0;
            var size = Marshal.SizeOf<int>();
            var result = DwmSetWindowAttribute(
                handle,
                DwmUseImmersiveDarkMode,
                ref enabled,
                size);
            if (result != 0)
            {
                _ = DwmSetWindowAttribute(
                    handle,
                    DwmUseImmersiveDarkModeBefore20H1,
                    ref enabled,
                    size);
            }
        }
        catch (DllNotFoundException)
        {
            // WAA still themes its own client area on systems without DWM support.
        }
        catch (EntryPointNotFoundException)
        {
            // WAA still themes its own client area on older Windows builds.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
