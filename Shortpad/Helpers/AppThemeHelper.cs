using System.Runtime.InteropServices;
using System.Windows.Interop;
using Shortpad.Managers;
using System.Windows;
using Shortpad.Windows.Menus;

namespace Shortpad.Helpers
{
    public static class AppThemeHelper
    {
        // 字典位于 Shortpad.UI 程序集中，使用 pack URI 指定
        private const string LightThemeUri = "pack://application:,,,/Shortpad.UI;component/Themes/LightTheme.xaml";
        private const string DarkThemeUri = "pack://application:,,,/Shortpad.UI;component/Themes/DarkTheme.xaml";

        // Windows 10 20H1 之前使用 19，之后（含 Windows 11）使用 20
        private const int DwmUseImmersiveDarkModeOld = 19;
        private const int DwmUseImmersiveDarkMode = 20;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

        private static ResourceDictionary _currentTheme;
        private static bool _initialized;
        private static readonly Dictionary<Window, Action<bool>> _attachedWindows = new();

        #region 应用级主题字典管理

        /// <summary>
        /// 应用启动时调用一次：应用初始主题并订阅后续变化
        /// </summary>
        public static void Initialize(bool isDark)
        {
            ApplyTheme(isDark);

            if (!_initialized)
            {
                AppStateManager.IsDarkModeChanged += ApplyTheme;
                _initialized = true;
            }
        }

        /// <summary>
        /// 用新主题字典替换旧的字典，DynamicResource 会自动刷新
        /// </summary>
        public static void ApplyTheme(bool isDark)
        {
            if (Application.Current == null) return;

            if (!Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() => ApplyTheme(isDark));
                return;
            }

            var merged = Application.Current.Resources.MergedDictionaries;
            var newTheme = new ResourceDictionary
            {
                Source = new Uri(isDark ? DarkThemeUri : LightThemeUri, UriKind.Absolute)
            };

            int index = _currentTheme == null ? -1 : merged.IndexOf(_currentTheme);
            if (index >= 0)
            {
                merged[index] = newTheme;
            }
            else
            {
                merged.Add(newTheme);
            }

            _currentTheme = newTheme;
        }

        #endregion

        #region 窗口非客户区（标题栏）暗色模式

        /// <summary>
        /// 对窗口应用暗色或亮色标题栏。
        /// </summary>
        public static bool ApplyTitleBarTheme(Window window, bool isDark)
        {
            if (window == null) return false;

            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return false;

            int value = isDark ? 1 : 0;
            int hr = DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref value, sizeof(int));
            if (hr != 0)
            {
                hr = DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkModeOld, ref value, sizeof(int));
            }
            return hr == 0;
        }

        /// <summary>
        /// 挂接窗口：句柄就绪时应用当前标题栏主题，并订阅主题变化；窗口关闭时自动取消订阅。
        /// </summary>
        public static void AttachWindow(Window window)
        {
            if (window == null || _attachedWindows.ContainsKey(window)) return;

            Action<bool> onThemeChanged = isDark => ApplyTitleBarTheme(window, isDark);
            AppStateManager.IsDarkModeChanged += onThemeChanged;
            _attachedWindows[window] = onThemeChanged;

            void OnSourceInitialized(object? sender, EventArgs e)
            {
                ApplyTitleBarTheme(window, AppStateManager.IsDarkMode);
            }

            void OnClosed(object? sender, EventArgs e)
            {
                window.SourceInitialized -= OnSourceInitialized;
                window.Closed -= OnClosed;
                DetachWindow(window);
            }

            window.SourceInitialized += OnSourceInitialized;
            window.Closed += OnClosed;

            if (PresentationSource.FromVisual(window) != null)
            {
                ApplyTitleBarTheme(window, AppStateManager.IsDarkMode);
            }
        }

        /// <summary>
        /// 取消窗口的主题订阅
        /// </summary>
        public static void DetachWindow(Window window)
        {
            if (window == null) return;
            if (_attachedWindows.TryGetValue(window, out var handler))
            {
                AppStateManager.IsDarkModeChanged -= handler;
                _attachedWindows.Remove(window);
            }
        }

        #endregion
    }
}