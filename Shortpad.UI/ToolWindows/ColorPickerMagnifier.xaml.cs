using Color = System.Windows.Media.Color;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;

namespace Shortpad.Windows.ToolWindows
{
    public partial class ColorPickerMagnifier : Window
    {
        #region Win32 API 声明

        [LibraryImport("user32.dll")]
        private static partial IntPtr GetDC(IntPtr hWnd);

        [LibraryImport("user32.dll")]
        private static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [LibraryImport("gdi32.dll")]
        private static partial uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

        [LibraryImport("gdi32.dll")]
        private static partial IntPtr CreateCompatibleDC(IntPtr hdc);

        [LibraryImport("gdi32.dll")]
        private static partial IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [LibraryImport("gdi32.dll")]
        private static partial IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DeleteObject(IntPtr hObject);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DeleteDC(IntPtr hdc);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool BitBlt(IntPtr hObject, int nXDest, int nYDest,
            int nWidth, int nHeight, IntPtr hObjectSource, int nXSrc, int nYSrc, int dwRop);

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [LibraryImport("gdi32.dll")]
        private static partial int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan,
            uint cScanLines, IntPtr lpvBits, ref BITMAPINFOHEADER lpbi, uint uUsage);

        #endregion

        #region 常量声明

        private const int OFFSET_DISTANCE = 20;
        private const int SRCCOPY = 0x00CC0020;
        private const int CAPTURE_SIZE = 25;

        #endregion

        #region 属性声明

        public Color SelectedColor { get; private set; }

        private double _workLeft, _workTop, _workRight, _workBottom;
        private double _dpiScaleX = 1, _dpiScaleY = 1;
        private bool _positionInitialized;
        private double _lastLeft = double.NaN;
        private double _lastTop = double.NaN;
        private Screen? _lastScreen;
        private int _textTick;
        private int _busy;

        #endregion

        public ColorPickerMagnifier()
        {
            InitializeComponent();
            Closed += (_, _) => CompositionTarget.Rendering -= OnRendering;
        }

        private void ColorPickerMagnifier_Loaded(object sender, RoutedEventArgs e)
        {
            UpdatePosition();
            CompositionTarget.Rendering += OnRendering; // 位置跟随交给渲染帧
        }

        private void OnRendering(object? sender, EventArgs e) => UpdatePosition();

        /// <summary>
        /// 更新窗口位置。每帧只做一次轻量的光标位置读取，
        /// 屏幕/DPI 信息只在跨屏时重算。
        /// </summary>
        private void UpdatePosition()
        {
            var mousePos = System.Windows.Forms.Control.MousePosition;
            var screen = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point(mousePos.X, mousePos.Y));

            if (!ReferenceEquals(screen, _lastScreen))
            {
                _lastScreen = screen;

                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget == null) return;

                var m = source.CompositionTarget.TransformFromDevice;
                _dpiScaleX = m.M11;
                _dpiScaleY = m.M22;

                var w = screen.WorkingArea;
                _workLeft = w.Left * _dpiScaleX;
                _workTop = w.Top * _dpiScaleY;
                _workRight = w.Right * _dpiScaleX;
                _workBottom = w.Bottom * _dpiScaleY;
            }

            double x = mousePos.X * _dpiScaleX;
            double y = mousePos.Y * _dpiScaleY;

            double left = x + OFFSET_DISTANCE;
            double top = y + OFFSET_DISTANCE;

            if (left + Width > _workRight) left = x - Width - OFFSET_DISTANCE;
            if (top + Height > _workBottom) top = y - Height - OFFSET_DISTANCE;

            left = Math.Max(_workLeft, left);
            top = Math.Max(_workTop, top);

            if (!_positionInitialized ||
                Math.Abs(left - _lastLeft) > 0.5 ||
                Math.Abs(top - _lastTop) > 0.5)
            {
                Left = left;
                Top = top;
                _lastLeft = left;
                _lastTop = top;
                _positionInitialized = true;
            }
        }

        /// <summary>
        /// 刷新放大图与颜色信息。只负责内容，不负责窗口位置。
        /// </summary>
        public void UpdateColor()
        {
            var mousePos = System.Windows.Forms.Control.MousePosition;

            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
                return;

            int cx = mousePos.X;
            int cy = mousePos.Y;

            Task.Run(() =>
            {
                BitmapSource? bitmap = null;
                Color center = Colors.White;
                try
                {
                    TryCaptureScreenRegion(cx, cy, out bitmap, out center);
                }
                finally
                {
                    Interlocked.Exchange(ref _busy, 0);
                }

                if (bitmap == null) return;

                try
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!IsVisible) return;

                        MagnifierImage.Source = bitmap;
                        SelectedColor = center;

                        if (++_textTick % 3 == 0)
                        {
                            HexTextBlock.Text = $"#{center.R:X2}{center.G:X2}{center.B:X2}";
                            RgbTextBlock.Text = $"RGB({center.R}, {center.G}, {center.B})";
                        }

                        ColorPreviewBorder.Background = new SolidColorBrush(center);
                    }));
                }
                catch
                {
                    // 窗口已关闭时 Dispatcher 可能不可用
                }
            });
        }

        private static bool TryCaptureScreenRegion(int centerX, int centerY,
            out BitmapSource? bitmap, out Color centerColor)
        {
            bitmap = null;
            centerColor = Colors.White;

            IntPtr screenDC = IntPtr.Zero, memDC = IntPtr.Zero, bmp = IntPtr.Zero, oldBmp = IntPtr.Zero;
            try
            {
                int size = CAPTURE_SIZE;
                int half = size / 2;

                screenDC = GetDC(IntPtr.Zero);
                memDC = CreateCompatibleDC(screenDC);
                bmp = CreateCompatibleBitmap(screenDC, size, size);
                oldBmp = SelectObject(memDC, bmp);

                if (!BitBlt(memDC, 0, 0, size, size, screenDC,
                        centerX - half, centerY - half, SRCCOPY))
                    return false;

                var header = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = size,
                    biHeight = -size, // 负值 = 自上而下
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                };

                int stride = size * 4;
                byte[] data = new byte[stride * size];

                var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
                try
                {
                    if (GetDIBits(memDC, bmp, 0, (uint)size,
                            handle.AddrOfPinnedObject(), ref header, 0) == 0)
                        return false;
                }
                finally
                {
                    handle.Free();
                }

                int idx = (half * size + half) * 4;
                centerColor = Color.FromRgb(data[idx + 2], data[idx + 1], data[idx]);

                var src = BitmapSource.Create(size, size, 96, 96,
                    PixelFormats.Bgr32, null, data, stride);
                src.Freeze();
                bitmap = src;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (oldBmp != IntPtr.Zero) SelectObject(memDC, oldBmp);
                if (bmp != IntPtr.Zero) DeleteObject(bmp);
                if (memDC != IntPtr.Zero) DeleteDC(memDC);
                if (screenDC != IntPtr.Zero) _ = ReleaseDC(IntPtr.Zero, screenDC);
            }
        }

        /// <summary>
        /// 读取屏幕某个物理像素的颜色（确认取色时调用）。
        /// </summary>
        internal static (byte r, byte g, byte b) GetPixelColor(int x, int y)
        {
            IntPtr hdc = IntPtr.Zero;
            try
            {
                hdc = GetDC(IntPtr.Zero);
                uint pixel = GetPixel(hdc, x, y);
                if (pixel == 0xFFFFFFFF) return (255, 255, 255);

                return (
                    (byte)(pixel & 0x000000FF),
                    (byte)((pixel & 0x0000FF00) >> 8),
                    (byte)((pixel & 0x00FF0000) >> 16));
            }
            catch
            {
                return (255, 255, 255);
            }
            finally
            {
                if (hdc != IntPtr.Zero) _ = ReleaseDC(IntPtr.Zero, hdc);
            }
        }
    }
}