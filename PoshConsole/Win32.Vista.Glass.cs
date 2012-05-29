using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows;

///
/// This actually lets you make windows that are fully glass enabled. In fact, if you 
/// also put these properties on the window, you would get a fully transparent window 
/// with it's "shape" defined entirely by whatever control(s) you put in it:
///     AllowsTransparency="True"  WindowStyle="None" (or make it a popup?)
///     

namespace Win32.Vista
{
    [Serializable, StructLayout(LayoutKind.Sequential)]
    struct MARGINS
    {
        public MARGINS(System.Windows.Thickness t)
        {
            LeftWidth = (int)t.Left;
            RightWidth = (int)t.Right;
            TopHeight = (int)t.Top;
            BottomHeight = (int)t.Bottom;
        }
        public int LeftWidth;
        public int RightWidth;
        public int TopHeight;
        public int BottomHeight;
    }

    public class Glass
    {

        [DllImport("dwmapi.dll", PreserveSig = false)]
        static extern void DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS margins);

        [DllImport("dwmapi.dll", PreserveSig = false)]
        static extern bool DwmIsCompositionEnabled();

        /// <summary>
        /// Extends the glass frame.
        /// </summary>
        /// <param name="window">The window.</param>
        /// <param name="margin">The margin.</param>
        /// <returns></returns>
        public static bool ExtendGlassFrame(System.Windows.Window window, Thickness margin)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version.Major >= 6 && DwmIsCompositionEnabled())
            {
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                    throw new InvalidOperationException("The Window must be shown before extending glass.");

                // Set the background to transparent from both the WPF and Win32 perspectives
                window.Background = Brushes.Transparent;
                HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;

                MARGINS margins = new MARGINS(margin);
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
                return true;
            } else return false;

        }
    }

}
