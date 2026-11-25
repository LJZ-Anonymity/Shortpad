using System.Windows;

namespace Shortpad.UI.Styles
{
    public static class ButtonStyles
    {
        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.RegisterAttached(
                "CornerRadius",
                typeof(CornerRadius),
                typeof(ButtonStyles),
                new FrameworkPropertyMetadata(new CornerRadius(5)));

        public static CornerRadius GetCornerRadius(DependencyObject obj)
            => (CornerRadius)obj.GetValue(CornerRadiusProperty);

        public static void SetCornerRadius(DependencyObject obj, CornerRadius value)
            => obj.SetValue(CornerRadiusProperty, value);
    }
}