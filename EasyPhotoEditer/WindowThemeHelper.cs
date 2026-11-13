using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EasyPhotoEditer
{
    public static class WindowThemeHelper
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        /// <summary>
        /// ウィンドウのタイトルバーにダークテーマを適用する
        /// </summary>
        public static void ApplyDarkTitleBar(Window window)
        {
            window.SourceInitialized += (s, e) =>
            {
                try
                {
                    var hwnd = new WindowInteropHelper(window).Handle;
                    if (hwnd == IntPtr.Zero) return;

                    int useDark = 1;
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));

                    // Windows 11 向け: キャプション背景色を #1E1E1E (COLORREF: 0x00BBGGRR) に設定
                    int captionColor = 0x001E1E1E;
                    DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
                }
                catch
                {
                    // 非対応OS環境等では安全にスキップ
                }
            };
        }
    }
}
