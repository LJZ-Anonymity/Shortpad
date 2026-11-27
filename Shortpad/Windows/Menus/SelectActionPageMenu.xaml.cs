using Shortpad.Windows.MainWindows.MainWindow;
using System.Windows.Media.Imaging;
using Shortpad.Contracts.Models;
using System.Windows.Controls;
using Shortpad.Database.Core;
using Shortpad.Managers;
using Shortpad.Helpers;
using Shortpad.Models;
using System.Windows;

namespace Shortpad.Windows.Menus
{
    public partial class SelectActionPageMenu : BaseMenuWindow
    {
        private ActionPageDatabase db3 = new(); // 动作页数据库

        public SelectActionPageMenu()
        {
            InitializeComponent();
        }

        // 重写基类的窗口加载方法
        protected override void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            base.OnWindowLoaded(sender, e); // 调用基类方法处理动画
            base.SetWindowPositionNearMouse(); // 设置窗口位置

            var sceneData = db3.GetAllSceneData(); // 获取所有场景数据
            foreach (var data in sceneData) // 遍历所有场景
            {
                if (data.SceneCount == 0 || data.SceneTag == "_global") continue; // 跳过没有动作页的场景和全局场景
                IncreaseMenuSize(); // 增加菜单大小
                Button button = GenerateButton(data); // 生成切换动作页按钮
                Grid grid = new(); // 创建按钮容器
                button.Content = grid; // 设置按钮内容
                Image image = GenerateImage(data); // 生成按钮图片
                grid.Children.Add(image);
                TextBlock textBlock = GenerateTextBlock(data); // 生成按钮文字
                grid.Children.Add(textBlock); // 添加文字到按钮容器
                ChangeSceneButtonStackPanel.Children.Add(button); // 添加按钮到按钮堆栈
                button.Click += ChangeActionPage; // 绑定切换动作页事件
            }
        }

        // 增加菜单大小
        private void IncreaseMenuSize()
        {
            MainGrid.Height += 25;
            base.Height += 25;
        }

        /// <summary>
        /// 生成切换动作页按钮
        /// </summary>
        /// <param name="data"> 场景数据 </param>
        private Button GenerateButton(SceneData data)
        {
            Button button = new()
            {
                Style = (Style)base.FindResource("MenuButton"),
                ToolTip = data.SceneTag,
                Name = data.SceneTag,
                Tag = data.SceneName
            };
            return button;
        }

        /// <summary>
        /// 生成按钮图标
        /// </summary>
        /// <param name="data"> 场景数据 </param>
        private Image GenerateImage(SceneData data)
        {
            Image image = new() { Style = (Style)base.FindResource("SelectButtonImage") };
            try
            {
                image.Source = new BitmapImage(new Uri(data.SceneIconPath, UriKind.RelativeOrAbsolute));
            }
            catch (System.IO.IOException)
            {
                var toast = new ToastManager();
                toast.Show("加载场景" + data.SceneName + " 图标失败。", ToastType.Error);
                image.Source = new BitmapImage(new Uri("/Resources/Images/app_enabled.png", UriKind.Relative));
            }
            return image;
        }

        /// <summary>
        /// 生成按钮文字
        /// </summary>
        /// <param name="data"> 场景数据 </param>
        private TextBlock GenerateTextBlock(SceneData data)
        {
            TextBlock textBlock = new()
            {
                Style = (Style)base.FindResource("MenuButtonTextBlock"),
                Text = db3.GetSceneTitle(data),
            };
            return textBlock;
        }

        // 切换动作页
        private void ChangeActionPage(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            MainWindow mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            if (mainWindow != null)
                mainWindow.OnCommonStyleChanged(button.Name); // 切换动作页时更新样式
            base.Close();
        }

        // 重写基类的失焦处理方法
        protected override void HandleDeactivated()
        {
            WindowHelper.SetMainWindowFocused();
            base.HandleDeactivated();
        }

        // 关闭窗口前释放资源
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
        }
    }
}