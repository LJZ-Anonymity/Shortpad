using System.Windows;

namespace Shortpad.UI.Styles.Combobox
{
    /// <summary>
    /// ComboBox 相关的附加属性。
    /// 新模板已不再依赖 DisplayText；如果项目中没有任何地方引用它，可以直接删除本文件。
    /// </summary>
    public static class ComboBoxHelper
    {
        public static readonly DependencyProperty DisplayTextProperty =
            DependencyProperty.RegisterAttached(
                "DisplayText",
                typeof(string),
                typeof(ComboBoxHelper),
                new PropertyMetadata(null));

        public static string GetDisplayText(DependencyObject obj)
        {
            return (string)obj.GetValue(DisplayTextProperty);
        }

        public static void SetDisplayText(DependencyObject obj, string value)
        {
            obj.SetValue(DisplayTextProperty, value);
        }
    }
}