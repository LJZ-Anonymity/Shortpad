using System.Windows.Media.Imaging;
using Shortpad.Contracts.Models;
using Shortpad.UI.UserControls;
using System.Windows.Controls;
using System.Windows.Media;
using Shortpad.Managers;
using Shortpad.Models;
using System.Windows;

namespace Shortpad.Helpers
{
    internal static class SceneIconHelper
    {
        public static readonly IReadOnlyList<string> DefaultSceneTypes = new[] { "_global", "common", "taskbar", "desktop" };
        private const string DefaultImagePath = "pack://application:,,,/Resources/Images/app_enabled.png";
        private const string ResourcePrefix = "/Resources/Images/";
        private const string PackPrefix = "pack://application:,,,";

        public static bool IsDefaultScene(string key) => !string.IsNullOrEmpty(key) && DefaultSceneTypes.Contains(key);

        public static FrameworkElement CreateSceneImage(SceneData sceneData, bool isMenu, bool showErrorToast = true)
        {
            ImageSource source;
            try
            {
                source = ResolveSceneImageSource(sceneData);
            }
            catch (Exception ex)
            {
                if (showErrorToast)
                    new ToastManager().Show("加载场景图片失败：" + ex.Message, ToastType.Error);
                source = LoadDefaultImage();
            }
            return CreateImageControl(sceneData, source, isMenu);
        }

        public static ImageSource ResolveSceneImageSource(SceneData sceneData)
        {
            if (string.IsNullOrEmpty(sceneData.SceneIconPath))
                return LoadDefaultImage();

            try
            {
                string uriString = sceneData.SceneIconPath.StartsWith(ResourcePrefix)
                    ? PackPrefix + sceneData.SceneIconPath
                    : sceneData.SceneIconPath;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(uriString, UriKind.RelativeOrAbsolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return LoadDefaultImage();
            }
        }

        private static ImageSource LoadDefaultImage()
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(DefaultImagePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private static FrameworkElement CreateImageControl(SceneData sceneData, ImageSource source, bool isMenu)
        {
            int size = isMenu ? 16 : 24;
            if (IsDefaultScene(sceneData.SceneName))
            {
                var icon = new IconImage
                {
                    Margin = new Thickness(5, 0, 0, 0),
                    Source = source,
                    Width = size,
                    Height = size
                };
                icon.SetResourceReference(IconImage.ColorProperty, "ImageBrush");
                return icon;
            }
            return new Image
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0),
                Source = source,
                Width = size,
                Height = size
            };
        }
    }
}