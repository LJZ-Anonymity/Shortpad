using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Diagnostics;
using Shortpad.Managers;
using Shortpad.Helpers;
using System.Windows;
using System.IO;

namespace Shortpad.Windows.Menus
{
    public partial class CustomMenu : BaseMenuWindow
    {
        private readonly App app = (App.Current as App);

        public CustomMenu()
        {
            InitializeComponent();
        }

        // 重写基类的窗口加载方法
        protected override void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            base.OnWindowLoaded(sender, e);
            base.SetWindowBottomLeftNearMouse(); // 使用基类方法设置窗口左下角在鼠标位置附近
            SetRunModeText(); // 设置运行模式文本
        }

        // 设置运行模式文本
        private void SetRunModeText()
        {
            var text = AppStateManager.Pause ? "恢复" : "暂停";
            PauseShortpadTextBlock.Text = text;
        }

        // 弹出面板窗口
        private void ShowMainWindow(object sender, RoutedEventArgs e)
        {
            app.ShowMainWindow(sender, e);
            base.Visibility = Visibility.Hidden;
        }

        // 打开设置窗口
        private void OpenSettingWindow(object sender, RoutedEventArgs e) => WindowHelper.OpenTargetWindow("SettingWindow");
        // 打开动作管理窗口
        private void OpenActionPageManageWindow(object sender, RoutedEventArgs e) => WindowHelper.OpenTargetWindow("ActionPageManageWindow");

        // 暂停Shortpad
        private void PauseShortpad(object sender, RoutedEventArgs e)
        {
            base.Dispatcher.Invoke(new Action(() =>
            {
                app.PauseShortpad(sender, e);
            }));
            base.Visibility = Visibility.Hidden; // 隐藏当前窗口
        }

        private void SwitchTheme(object sender, RoutedEventArgs e)
        {
            AppStateManager.IsDarkMode = !AppStateManager.IsDarkMode;
            base.Visibility = Visibility.Hidden; // 隐藏当前窗口
        }

        // 重启应用
        public void Restart(object sender, RoutedEventArgs e)
        {
            base.Visibility = Visibility.Hidden;
            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string directory = Path.GetDirectoryName(exePath);
            string exeName = Path.GetFileNameWithoutExtension(exePath) + ".exe";
            SingleInstanceManager.ReleaseMutex();
            app.Shutdown();
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(directory, exeName),
                UseShellExecute = true
            });
        }

        // 退出应用
        private void Exit(object sender, RoutedEventArgs e)
        {
            base.Visibility = Visibility.Hidden;
            app.Shutdown();
        }

        // 重写基类的失焦处理方法
        protected override void HandleDeactivated()
        {
            CloseWithAnimation();
            base.HandleDeactivated();
        }

        // 关闭窗口前释放资源
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
        }
    }
}