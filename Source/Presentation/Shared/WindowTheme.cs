using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace IpaDesktop
{
    /// <summary>Применяет оформление заголовка Windows через DWM, учитывая доступность API системы.</summary>
    public static class WindowTheme
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        /// <summary>Устанавливает тёмный заголовок и цвета рамки, если версия Windows поддерживает нужные атрибуты.</summary>
        public static void Apply(Window window)
        {
            if (SystemParameters.HighContrast) return;
            var hwnd = new WindowInteropHelper(window).Handle;
            int dark = 1, caption = 0x1F1510, text = 0xF8F1ED, border = 0x3A2D23;
            try
            {
                if (DwmSetWindowAttribute(hwnd, 20, ref dark, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref dark, 4);
                DwmSetWindowAttribute(hwnd, 35, ref caption, 4);
                DwmSetWindowAttribute(hwnd, 36, ref text, 4);
                DwmSetWindowAttribute(hwnd, 34, ref border, 4);
            } catch (DllNotFoundException)
            {
            } catch (EntryPointNotFoundException)
            {
            }
        }
    }
}
