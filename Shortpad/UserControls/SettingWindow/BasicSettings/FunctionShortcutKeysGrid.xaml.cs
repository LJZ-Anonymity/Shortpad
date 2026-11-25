using System.Windows.Controls;
using Shortpad.Managers;

namespace Shortpad.UserControls.SettingWindow.BasicSettings
{
    public partial class FunctionShortcutKeysControl : UserControl
    {
        private WeakReference<Shortpad.Windows.MainWindows.SettingWindow> weakSettingWindow; // 弱引用设置窗口
        SettingManager settingManager; // 设置管理器

        public FunctionShortcutKeysControl(Shortpad.Windows.MainWindows.SettingWindow settingWindow)
        {
            InitializeComponent();
            weakSettingWindow = new(settingWindow); // 保存设置窗口
            settingManager = settingWindow._settingManager; // 获取设置管理器
        }
    }
}