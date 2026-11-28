using ColorConverter = System.Windows.Media.ColorConverter;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using UserControl = System.Windows.Controls.UserControl;
using Rectangle = System.Windows.Shapes.Rectangle;
using TextBox = System.Windows.Controls.TextBox;
using Color = System.Windows.Media.Color;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Point = System.Windows.Point;
using Shortpad.Windows.ToolWindows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;

namespace Shortpad.UI.UserControls
{
    public partial class ColorPicker : UserControl
    {
        /// <summary>
        /// 颜色更新来源。不同来源决定是否需要反推色相、是否需要移动色板小球。
        /// </summary>
        private enum ColorUpdateSource
        {
            External,   // 外部赋值 / 初始化
            Canvas,     // 拖动色板
            HueSlider,  // 拖动色相滑块
            Rgb,        // RGB 滑块或数值框
            Hex,        // HEX 输入框
            Picker      // 屏幕取色
        }

        private ColorPickerMagnifier? _magnifierWindow;
        private Color _currentColor = Colors.White;
        private DispatcherTimer? _magnifierTimer;
        private LowLevelMouseHook? _mouseHook;
        private bool _updatingControls;
        private LinearGradientBrush? _hueBrush;
        private bool _isColorPicking;
        private double _saturation = 1;
        private Rectangle? _colorRect;
        private double _value = 1;
        private double _hue;

        /// <summary>
        /// 当前这次 SelectedColor 变化的来源，由 SetColor 写入、OnSelectedColorChanged 读取后复位。
        /// </summary>
        private ColorUpdateSource _updateSource = ColorUpdateSource.External;

        public event EventHandler<ColorChangedEventArgs>? SelectedColorChanged;

        public static readonly DependencyProperty SelectedColorProperty =
            DependencyProperty.Register("SelectedColor", typeof(Color), typeof(ColorPicker),
                new FrameworkPropertyMetadata(Colors.White,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnSelectedColorChanged));

        public Color SelectedColor
        {
            get => (Color)GetValue(SelectedColorProperty);
            set => SetValue(SelectedColorProperty, value);
        }

        private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var picker = (ColorPicker)d;
            picker._currentColor = (Color)e.NewValue;

            var source = picker._updateSource;
            picker._updateSource = ColorUpdateSource.External;

            picker.ApplyColorToControls(source);
            picker.RaiseSelectedColorChanged((Color)e.OldValue, (Color)e.NewValue);
        }

        /// <summary>
        /// 唯一的颜色写入入口。所有输入点都通过它修改颜色。
        /// </summary>
        private void SetColor(Color color, ColorUpdateSource source)
        {
            _updateSource = source;
            try
            {
                SelectedColor = color;
            }
            finally
            {
                _updateSource = ColorUpdateSource.External;
            }
        }

        private void RaiseSelectedColorChanged(Color oldColor, Color newColor)
        {
            SelectedColorChanged?.Invoke(this, new ColorChangedEventArgs(oldColor, newColor));
        }

        public ColorPicker()
        {
            InitializeComponent();
            Loaded += ColorPicker_Loaded;
        }

        private void ColorPicker_Loaded(object? sender, RoutedEventArgs e)
        {
            if (_colorRect == null &&
                ColorCanvas.Children.Count > 0 &&
                ColorCanvas.Children[0] is Rectangle rect)
            {
                _colorRect = rect;
            }

            ApplyColorToControls(ColorUpdateSource.External);
            UpdateColorThumbPosition();
        }

        /// <summary>
        /// 强制刷新颜色选择器的所有控件状态。
        /// </summary>
        public void ResetColorControls(Color color)
        {
            bool same = _currentColor == color && SelectedColor == color;
            SetColor(color, ColorUpdateSource.External);
            if (same)
            {
                // 颜色没变，DP 不会回调，手动同步一次
                ApplyColorToControls(ColorUpdateSource.External);
            }
        }

        /// <summary>
        /// 根据来源把 _currentColor 同步到各控件。
        /// </summary>
        private void ApplyColorToControls(ColorUpdateSource source)
        {
            if (RedSlider == null || GreenSlider == null || BlueSlider == null ||
                AlphaSlider == null || HueSlider == null || ColorThumb == null ||
                HexValue == null || RedValue == null || GreenValue == null ||
                BlueValue == null || AlphaValue == null)
            {
                return;
            }

            _updatingControls = true;
            try
            {
                RedSlider.Value = _currentColor.R;
                GreenSlider.Value = _currentColor.G;
                BlueSlider.Value = _currentColor.B;
                AlphaSlider.Value = _currentColor.A;

                // 色板拖动和色相拖动时，S/V 或 H 是"事实来源"，
                // 不能再用 RGB 反推，否则取整误差会让小球和色相抖动。
                bool syncHue = source is not (ColorUpdateSource.Canvas or ColorUpdateSource.HueSlider);
                bool syncThumb = syncHue;

                if (syncHue)
                {
                    RgbToHsv(_currentColor.R, _currentColor.G, _currentColor.B,
                             out double newHue, out double s, out double v);
                    _saturation = s;
                    _value = v;

                    // 灰色/黑色时色相无意义，保留原 _hue，避免色相滑块跳回红色
                    if (s > 0 && v > 0)
                    {
                        _hue = newHue;
                    }

                    HueSlider.Value = _hue;
                    UpdateHueColorRect(_hue);
                }

                if (syncThumb &&
                    ColorCanvas.ActualWidth > 0 &&
                    ColorCanvas.ActualHeight > 0)
                {
                    UpdateColorThumbPosition();
                }

                // HEX 来源保留用户输入格式，其余来源统一写标准格式
                if (source != ColorUpdateSource.Hex)
                {
                    UpdateHexValue();
                }

                UpdateRgbTexts();
            }
            finally
            {
                _updatingControls = false;
            }
        }

        private void UpdateRgbTexts()
        {
            SetTextIfChanged(RedValue, _currentColor.R.ToString());
            SetTextIfChanged(GreenValue, _currentColor.G.ToString());
            SetTextIfChanged(BlueValue, _currentColor.B.ToString());
            SetTextIfChanged(AlphaValue, _currentColor.A.ToString());
        }

        private static void SetTextIfChanged(TextBox? box, string text)
        {
            if (box != null && box.Text != text)
            {
                box.Text = text;
            }
        }

        private void UpdateHexValue()
        {
            if (HexValue == null) return;
            SetTextIfChanged(HexValue, $"#{_currentColor.A:X2}{_currentColor.R:X2}{_currentColor.G:X2}{_currentColor.B:X2}");
        }

        /// <summary>
        /// 更新色相矩形的颜色（复用画刷，不重复 new）。
        /// </summary>
        private void UpdateHueColorRect(double hue)
        {
            if (_colorRect == null) return;

            var brush = _hueBrush;
            if (brush == null)
            {
                brush = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 0)
                };
                brush.GradientStops.Add(new GradientStop(Colors.White, 0));
                brush.GradientStops.Add(new GradientStop(Colors.Red, 1));

                _colorRect.Fill = brush;
                _hueBrush = brush;
            }

            brush.GradientStops[1].Color = GetColorFromHue(hue);
        }

        private void UpdateColorThumbPosition()
        {
            if (ColorCanvas == null || ColorThumb == null ||
                ColorCanvas.ActualWidth <= 0 || ColorCanvas.ActualHeight <= 0)
            {
                return;
            }

            double x = _saturation * ColorCanvas.ActualWidth;
            double y = (1 - _value) * ColorCanvas.ActualHeight;

            ColorThumb.SetValue(Canvas.LeftProperty,
                Math.Max(0, Math.Min(ColorCanvas.ActualWidth - ColorThumb.Width / 2, x - ColorThumb.Width / 2)));
            ColorThumb.SetValue(Canvas.TopProperty,
                Math.Max(0, Math.Min(ColorCanvas.ActualHeight - ColorThumb.Height / 2, y - ColorThumb.Height / 2)));
        }

        // ---------------- 输入点：全部只写 SetColor ----------------

        private void Slider_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingControls || sender is not Slider slider) return;

            int value = (int)slider.Value;
            var c = _currentColor;
            Color newColor;

            if (slider == RedSlider)
                newColor = Color.FromArgb(c.A, (byte)value, c.G, c.B);
            else if (slider == GreenSlider)
                newColor = Color.FromArgb(c.A, c.R, (byte)value, c.B);
            else if (slider == BlueSlider)
                newColor = Color.FromArgb(c.A, c.R, c.G, (byte)value);
            else if (slider == AlphaSlider)
                newColor = Color.FromArgb((byte)value, c.R, c.G, c.B);
            else
                return;

            SetColor(newColor, ColorUpdateSource.Rgb);
        }

        private void Value_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_updatingControls || sender is not TextBox box) return;
            if (string.IsNullOrEmpty(box.Text)) return;
            if (!int.TryParse(box.Text, out int value)) return;

            value = Math.Max(0, Math.Min(255, value));

            var c = _currentColor;
            Color newColor;

            if (box == RedValue)
                newColor = Color.FromArgb(c.A, (byte)value, c.G, c.B);
            else if (box == GreenValue)
                newColor = Color.FromArgb(c.A, c.R, (byte)value, c.B);
            else if (box == BlueValue)
                newColor = Color.FromArgb(c.A, c.R, c.G, (byte)value);
            else if (box == AlphaValue)
                newColor = Color.FromArgb((byte)value, c.R, c.G, c.B);
            else
                return;

            SetColor(newColor, ColorUpdateSource.Rgb);
        }

        private void HueSlider_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingControls || sender is not Slider slider) return;

            _hue = slider.Value;
            UpdateHueColorRect(_hue);

            HsvToRgb(_hue, _saturation, _value, out byte r, out byte g, out byte b);
            SetColor(Color.FromArgb(_currentColor.A, r, g, b), ColorUpdateSource.HueSlider);
        }

        private void ColorCanvas_MouseDown(object? sender, MouseButtonEventArgs e)
        {
            if (e.RightButton == MouseButtonState.Pressed)
            {
                StartScreenColorPicking();
            }
            else if (e.LeftButton == MouseButtonState.Pressed)
            {
                if (_isColorPicking)
                {
                    ConfirmColorPick();
                }
                else
                {
                    ColorCanvas.CaptureMouse();
                    UpdateColorFromCanvasPosition(e.GetPosition(ColorCanvas));
                }
            }
        }

        private void ColorCanvas_MouseMove(object? sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && ColorCanvas.IsMouseCaptured)
            {
                UpdateColorFromCanvasPosition(e.GetPosition(ColorCanvas));
            }
        }

        private void ColorCanvas_MouseUp(object? sender, MouseButtonEventArgs e)
        {
            if (ColorCanvas.IsMouseCaptured)
            {
                ColorCanvas.ReleaseMouseCapture();
                UpdateColorFromCanvasPosition(e.GetPosition(ColorCanvas));
            }
        }

        private void UpdateColorFromCanvasPosition(Point position)
        {
            double canvasWidth = ColorCanvas.ActualWidth;
            double canvasHeight = ColorCanvas.ActualHeight;

            if (canvasWidth <= 0 || canvasHeight <= 0) return;

            double x = Math.Max(0, Math.Min(canvasWidth, position.X));
            double y = Math.Max(0, Math.Min(canvasHeight, position.Y));

            _saturation = x / canvasWidth;
            _value = 1 - y / canvasHeight;

            // 小球直接跟手，不经过 RGB 反推，避免取整抖动
            _updatingControls = true;
            try
            {
                ColorThumb.SetValue(Canvas.LeftProperty, x - ColorThumb.Width / 2);
                ColorThumb.SetValue(Canvas.TopProperty, y - ColorThumb.Height / 2);
            }
            finally
            {
                _updatingControls = false;
            }

            HsvToRgb(_hue, _saturation, _value, out byte r, out byte g, out byte b);
            SetColor(Color.FromArgb(_currentColor.A, r, g, b), ColorUpdateSource.Canvas);
        }

        private void HexValue_LostFocus(object? sender, RoutedEventArgs e)
        {
            if (_updatingControls || HexValue == null) return;

            string hexText = HexValue.Text.Trim();
            if (!hexText.StartsWith('#'))
            {
                hexText = "#" + hexText;
            }

            if (IsValidHexColor(hexText))
            {
                try
                {
                    Color newColor;
                    if (hexText.Length == 9)
                    {
                        newColor = (Color)ColorConverter.ConvertFromString(hexText);
                    }
                    else if (hexText.Length == 7)
                    {
                        var baseColor = (Color)ColorConverter.ConvertFromString(hexText);
                        newColor = Color.FromArgb(_currentColor.A, baseColor.R, baseColor.G, baseColor.B);
                    }
                    else if (hexText.Length == 4)
                    {
                        string fullHex = "#" + hexText[1] + hexText[1] + hexText[2] + hexText[2] + hexText[3] + hexText[3];
                        var baseColor = (Color)ColorConverter.ConvertFromString(fullHex);
                        newColor = Color.FromArgb(_currentColor.A, baseColor.R, baseColor.G, baseColor.B);
                    }
                    else
                    {
                        return;
                    }

                    SetColor(newColor, ColorUpdateSource.Hex);
                }
                catch
                {
                    // 无效输入保持原状
                }
            }

            // 回写用户输入格式（补全 #）
            SetTextIfChanged(HexValue, hexText);
        }

        [GeneratedRegex(@"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
        private static partial Regex HexColorRegex();

        private static bool IsValidHexColor(string hexColor) => HexColorRegex().IsMatch(hexColor);

        // ---------------- 屏幕取色 ----------------

        private void StartScreenColorPicking()
        {
            if (_isColorPicking) return;
            _isColorPicking = true;

            try
            {
                var win = new ScreenColorPickerWindow { Owner = Window.GetWindow(this) };
                var picked = win.Pick();
                if (picked is Color c)
                {
                    SetColor(c, ColorUpdateSource.Picker);
                }
            }
            finally
            {
                _isColorPicking = false;
            }
        }

        private void OnGlobalLeftButtonDown()
        {
            // 钩子回调必须尽快返回，实际处理排到消息循环之后
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isColorPicking) ConfirmColorPick();
            }), DispatcherPriority.Input);
        }

        private void OnGlobalRightButtonDown()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isColorPicking) StopScreenColorPicking();
            }), DispatcherPriority.Input);
        }

        private void MagnifierTimer_Tick(object? sender, EventArgs e)
        {
            if (_magnifierWindow == null || !_isColorPicking) return;
            _magnifierWindow.UpdateColor();
        }

        private void ConfirmColorPick()
        {
            if (!_isColorPicking || _magnifierWindow == null) return;

            // 实时取色，不依赖缓存
            var mp = System.Windows.Forms.Control.MousePosition;
            var (r, g, b) = ColorPickerMagnifier.GetPixelColor(mp.X, mp.Y);

            SetColor(Color.FromRgb(r, g, b), ColorUpdateSource.Picker);

            StopScreenColorPicking();
        }

        private void StopScreenColorPicking()
        {
            if (!_isColorPicking && _magnifierWindow == null && _mouseHook == null) return;
            _isColorPicking = false;

            try
            {
                _magnifierTimer?.Stop();
            }
            finally
            {
                _magnifierTimer = null;

                try { _mouseHook?.Stop(); }
                catch { }
                _mouseHook = null;

                try { _magnifierWindow?.Close(); }
                catch { }
                _magnifierWindow = null;

                Mouse.Capture(null);
                PreviewMouseLeftButtonDown -= ColorPicker_PreviewMouseLeftButtonDown;
                PreviewMouseRightButtonDown -= ColorPicker_PreviewMouseRightButtonDown;
            }
        }

        private void ColorPicker_PreviewMouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
        {
            if (_isColorPicking)
            {
                ConfirmColorPick();
                e.Handled = true;
            }
        }

        private void ColorPicker_PreviewMouseRightButtonDown(object? sender, MouseButtonEventArgs e)
        {
            if (_isColorPicking)
            {
                StopScreenColorPicking();
                e.Handled = true;
            }
        }

        private void ColorPreview_MouseRightButtonDown(object? sender, MouseButtonEventArgs e)
        {
            StartScreenColorPicking();
            e.Handled = true;
        }

        // ---------------- 颜色换算 ----------------

        private static Color GetColorFromHue(double hue)
        {
            HsvToRgb(hue, 1, 1, out byte r, out byte g, out byte b);
            return Color.FromRgb(r, g, b);
        }

        private static void RgbToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
        {
            double red = r / 255.0;
            double green = g / 255.0;
            double blue = b / 255.0;

            double max = Math.Max(red, Math.Max(green, blue));
            double min = Math.Min(red, Math.Min(green, blue));
            double delta = max - min;

            v = max;
            s = max == 0 ? 0 : delta / max;

            if (delta == 0)
            {
                h = 0;
            }
            else
            {
                if (max == red)
                    h = ((green - blue) / delta) % 6;
                else if (max == green)
                    h = (blue - red) / delta + 2;
                else
                    h = (red - green) / delta + 4;

                h *= 60;
                if (h < 0) h += 360;
            }
        }

        private static void HsvToRgb(double h, double s, double v, out byte r, out byte g, out byte b)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;
            double red, green, blue;

            if (h >= 0 && h < 60)
            {
                red = c; green = x; blue = 0;
            }
            else if (h >= 60 && h < 120)
            {
                red = x; green = c; blue = 0;
            }
            else if (h >= 120 && h < 180)
            {
                red = 0; green = c; blue = x;
            }
            else if (h >= 180 && h < 240)
            {
                red = 0; green = x; blue = c;
            }
            else if (h >= 240 && h < 300)
            {
                red = x; green = 0; blue = c;
            }
            else
            {
                red = c; green = 0; blue = x;
            }

            r = (byte)Math.Round((red + m) * 255);
            g = (byte)Math.Round((green + m) * 255);
            b = (byte)Math.Round((blue + m) * 255);
        }
    }

    /// <summary>
    /// 低级鼠标钩子（WH_MOUSE_LL）。回调在安装线程（UI 线程）的消息循环中执行，
    /// 只抛出事件，调用方应通过 Dispatcher.BeginInvoke 把实际处理排到后面。
    /// </summary>
    internal sealed partial class LowLevelMouseHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial IntPtr SetWindowsHookExW(int idHook, IntPtr lpfn, IntPtr hMod, uint dwThreadId);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool UnhookWindowsHookEx(IntPtr hhk);

        [LibraryImport("user32.dll")]
        private static partial IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial IntPtr GetModuleHandleW(string? lpModuleName);

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        private HookProc? _proc; // 必须保持引用，防止委托被 GC
        private IntPtr _handle;

        public event Action? LeftButtonDown;
        public event Action? RightButtonDown;

        public void Start()
        {
            if (_handle != IntPtr.Zero) return;

            _proc = HookCallback;
            var procPtr = Marshal.GetFunctionPointerForDelegate(_proc);
            _handle = SetWindowsHookExW(WH_MOUSE_LL, procPtr, GetModuleHandleW(null), 0);

            if (_handle == IntPtr.Zero)
            {
                _proc = null;
                throw new InvalidOperationException(
                    "安装鼠标钩子失败",
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            }
        }

        public void Stop()
        {
            if (_handle == IntPtr.Zero) return;
            UnhookWindowsHookEx(_handle);
            _handle = IntPtr.Zero;
            _proc = null;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_LBUTTONDOWN) LeftButtonDown?.Invoke();
                else if (msg == WM_RBUTTONDOWN) RightButtonDown?.Invoke();
            }

            return CallNextHookEx(_handle, nCode, wParam, lParam);
        }

        public void Dispose() => Stop();
    }

    public class ColorChangedEventArgs(Color oldColor, Color newColor) : EventArgs
    {
        public Color OldColor { get; } = oldColor;
        public Color NewColor { get; } = newColor;
    }
}