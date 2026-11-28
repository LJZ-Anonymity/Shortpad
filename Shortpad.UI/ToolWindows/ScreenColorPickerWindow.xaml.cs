using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using DrawingCopyPixelOperation = System.Drawing.CopyPixelOperation;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingBitmap = System.Drawing.Bitmap;
using WpfColor = System.Windows.Media.Color;
using DrawingSize = System.Drawing.Size;
using WpfPoint = System.Windows.Point;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;

namespace Shortpad.Windows.ToolWindows
{
    /// <summary>
    /// 全屏取色窗口：先截取整个虚拟屏幕，再让用户在这张冻结的画面上移动取色。
    /// 模态使用：Pick() 返回选中的颜色，取消返回 null。
    /// </summary>
    public partial class ScreenColorPickerWindow : Window
    {
        private const int CAPTURE_SIZE = 25;
        private const double MAGNIFY_SIZE = 200;

        private BitmapSource? _screenshot;
        private double _dpiScale = 1;
        private byte[] _buffer = [];

        public WpfColor? PickedColor { get; private set; }

        public ScreenColorPickerWindow()
        {
            InitializeComponent();
            CaptureScreen();
        }

        public WpfColor? Pick() => ShowDialog() == true ? PickedColor : null;

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            Focus();
            Keyboard.Focus(this);
            UpdateMagnifier(Mouse.GetPosition(this));
        }

        #region 截屏

        private void CaptureScreen()
        {
            var vs = SystemInformation.VirtualScreen;

            using var bmp = new DrawingBitmap(vs.Width, vs.Height, DrawingPixelFormat.Format32bppArgb);
            using (var g = DrawingGraphics.FromImage(bmp))
            {
                g.CopyFromScreen(vs.Left, vs.Top, 0, 0,
                    new DrawingSize(vs.Width, vs.Height),
                    DrawingCopyPixelOperation.SourceCopy);
            }

            // 直接锁定像素，用 BitmapSource.Create 从原始内存构造，避免 GetHbitmap / DeleteObject
            var data = bmp.LockBits(
                new Rectangle(0, 0, bmp.Width, bmp.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                DrawingPixelFormat.Format32bppArgb);

            try
            {
                var src = BitmapSource.Create(
                    data.Width, data.Height,
                    96, 96,
                    PixelFormats.Bgra32, null,
                    data.Scan0, data.Stride * data.Height, data.Stride);
                src.Freeze();
                _screenshot = src;
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            _dpiScale = GetSystemDpiScale();
            Left = vs.Left / _dpiScale;
            Top = vs.Top / _dpiScale;
            Width = vs.Width / _dpiScale;
            Height = vs.Height / _dpiScale;

            ScreenImage.Source = _screenshot;
        }

        private static double GetSystemDpiScale()
        {
            using var g = DrawingGraphics.FromHwnd(IntPtr.Zero);
            return g.DpiX / 96.0;
        }
        #endregion

        #region 鼠标移动：更新放大镜

        protected override void OnMouseMove(WpfMouseEventArgs e)
        {
            base.OnMouseMove(e);
            UpdateMagnifier(e.GetPosition(this));
        }

        private void UpdateMagnifier(WpfPoint mouseDip)
        {
            if (_screenshot == null) return;

            int px = (int)Math.Floor(mouseDip.X * _dpiScale);
            int py = (int)Math.Floor(mouseDip.Y * _dpiScale);

            if (px < 0 || py < 0 || px >= _screenshot.PixelWidth || py >= _screenshot.PixelHeight)
                return;

            int half = CAPTURE_SIZE / 2;
            int sx = Math.Max(0, Math.Min(_screenshot.PixelWidth - CAPTURE_SIZE, px - half));
            int sy = Math.Max(0, Math.Min(_screenshot.PixelHeight - CAPTURE_SIZE, py - half));

            int localX = px - sx;
            int localY = py - sy;

            int stride = CAPTURE_SIZE * 4;
            int size = stride * CAPTURE_SIZE;
            if (_buffer.Length < size) _buffer = new byte[size];

            _screenshot.CopyPixels(
                new Int32Rect(sx, sy, CAPTURE_SIZE, CAPTURE_SIZE),
                _buffer, stride, 0);

            int idx = (localY * CAPTURE_SIZE + localX) * 4;
            var color = WpfColor.FromRgb(_buffer[idx + 2], _buffer[idx + 1], _buffer[idx]);
            PickedColor = color;

            var mag = BitmapSource.Create(
                CAPTURE_SIZE, CAPTURE_SIZE, 96, 96,
                PixelFormats.Bgr32, null, _buffer, stride);
            mag.Freeze();
            MagnifierImage.Source = mag;

            double scale = MAGNIFY_SIZE / CAPTURE_SIZE;
            double cx = (localX + 0.5) * scale;
            double cy = (localY + 0.5) * scale;

            Canvas.SetLeft(PixelIndicator, cx - PixelIndicator.Width / 2);
            Canvas.SetTop(PixelIndicator, cy - PixelIndicator.Height / 2);

            CrosshairH.X1 = 0; CrosshairH.Y1 = cy;
            CrosshairH.X2 = MAGNIFY_SIZE; CrosshairH.Y2 = cy;
            CrosshairV.X1 = cx; CrosshairV.Y1 = 0;
            CrosshairV.X2 = cx; CrosshairV.Y2 = MAGNIFY_SIZE;

            HexTextBlock.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            RgbTextBlock.Text = $"RGB({color.R}, {color.G}, {color.B})";
            ColorPreviewBorder.Background = new SolidColorBrush(color);

            PlaceMagnifier(mouseDip);
        }

        private void PlaceMagnifier(WpfPoint mouseDip)
        {
            const double offset = 16;

            double w = MagnifierBorder.ActualWidth > 0 ? MagnifierBorder.ActualWidth : 200;
            double h = MagnifierBorder.ActualHeight > 0 ? MagnifierBorder.ActualHeight : 260;

            double left = mouseDip.X + offset;
            double top = mouseDip.Y + offset;

            if (left + w > ActualWidth) left = mouseDip.X - w - offset;
            if (top + h > ActualHeight) top = mouseDip.Y - h - offset;

            left = Math.Max(0, left);
            top = Math.Max(0, top);

            Canvas.SetLeft(MagnifierBorder, left);
            Canvas.SetTop(MagnifierBorder, top);
        }
        #endregion

        #region 确认 / 取消

        protected override void OnMouseLeftButtonDown(WpfMouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (PickedColor.HasValue)
            {
                DialogResult = true;
            }
        }

        protected override void OnMouseRightButtonDown(WpfMouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            DialogResult = false;
        }

        private void OnPreviewKeyDown(object? sender, WpfKeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && PickedColor.HasValue)
            {
                DialogResult = true;
                e.Handled = true;
            }
        }
        #endregion
    }
}