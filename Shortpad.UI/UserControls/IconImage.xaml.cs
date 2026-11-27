using UserControl = System.Windows.Controls.UserControl;
using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
using System.Windows.Media;
using System.Windows;

namespace Shortpad.UI.UserControls
{
    public partial class IconImage : UserControl
    {
        public IconImage()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty SourceProperty =
            DependencyProperty.Register(nameof(Source), typeof(ImageSource), typeof(IconImage),
                new PropertyMetadata(null));

        public static readonly DependencyProperty ColorProperty =
            DependencyProperty.Register(nameof(Color), typeof(Brush), typeof(IconImage),
                new PropertyMetadata(Brushes.Black));

        public static readonly DependencyProperty StretchProperty =
            DependencyProperty.Register(nameof(Stretch), typeof(Stretch), typeof(IconImage),
                new PropertyMetadata(Stretch.Uniform));

        /// <summary>
        /// 图标图片，XAML 中可直接写路径，如 "/Resources/Images/app_enabled.png"
        /// </summary>
        public ImageSource Source
        {
            get => (ImageSource)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        /// <summary>
        /// 图标颜色，支持 DynamicResource
        /// </summary>
        public Brush Color
        {
            get => (Brush)GetValue(ColorProperty);
            set => SetValue(ColorProperty, value);
        }

        /// <summary>
        /// 填充方式，默认 Uniform 防变形
        /// </summary>
        public Stretch Stretch
        {
            get => (Stretch)GetValue(StretchProperty);
            set => SetValue(StretchProperty, value);
        }
    }
}