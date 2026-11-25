using Shortpad.Helpers;
using System.Windows;

namespace Shortpad.Windows.ToolWindows
{
    public partial class LoadingWindow : Window
    {
        public LoadingWindow()
        {
            InitializeComponent();
            AppThemeHelper.AttachWindow(this);
        }

        // 关闭窗口前释放资源
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e); // 调用基类的 OnClosed 方法
        }
    }
}