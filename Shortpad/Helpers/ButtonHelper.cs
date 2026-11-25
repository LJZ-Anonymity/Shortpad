using System.Windows;

namespace Shortpad.Helpers
{
    public static class ButtonHelper
    {
        public static readonly DependencyProperty IsSelectedProperty =
            DependencyProperty.RegisterAttached(
                "IsSelected",
                typeof(bool),
                typeof(ButtonHelper),
                new PropertyMetadata(false));

        public static void SetIsSelected(DependencyObject element, bool value)
            => element.SetValue(IsSelectedProperty, value);

        public static bool GetIsSelected(DependencyObject element)
            => (bool)element.GetValue(IsSelectedProperty);
    }
}