using System.Diagnostics;
using Microsoft.Win32;

namespace Quicker.Helpers
{
    /// <summary>
    /// 基于当前用户注册表 Run 键的开机自启动辅助类（无需管理员权限）
    /// </summary>
    public static class AutoStartHelper
    {
        private const string RunRegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppValueName = "Quicker";
        private const string AutoRunArgument = "-autorun";

        /// <summary>
        /// 获取当前可执行文件路径
        /// </summary>
        public static string GetCurrentExePath()
        {
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                return exePath;
            }

            using var process = Process.GetCurrentProcess();
            return process.MainModule?.FileName ?? string.Empty;
        }

        /// <summary>
        /// 构造写入注册表的启动命令（带引号防空格 + -autorun 参数）
        /// </summary>
        public static string BuildStartupCommand(string? exePath = null)
        {
            string targetPath = string.IsNullOrWhiteSpace(exePath) ? GetCurrentExePath() : exePath;
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                throw new InvalidOperationException("无法获取当前应用程序的可执行文件路径。");
            }

            return $"\"{targetPath}\" {AutoRunArgument}";
        }

        /// <summary>
        /// 查询是否已配置自启动。
        /// </summary>
        /// <param name="requireExactPathMatch">
        /// 为 true 时，要求注册表中的路径与当前程序路径一致；为 false 时，仅判断是否存在该启动项。
        /// </param>
        public static bool IsAutoStartEnabled(bool requireExactPathMatch = true)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: false);
                if (key == null)
                {
                    return false;
                }

                if (key.GetValue(AppValueName) is not string rawValue || string.IsNullOrWhiteSpace(rawValue))
                {
                    return false;
                }

                if (!requireExactPathMatch)
                {
                    return true;
                }

                string currentExe = GetCurrentExePath();
                if (string.IsNullOrWhiteSpace(currentExe))
                {
                    return true;
                }

                string expectedCommand = BuildStartupCommand(currentExe);
                return string.Equals(rawValue.Trim(), expectedCommand.Trim(), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 开启或关闭开机自启动
        /// </summary>
        /// <param name="enable">true 为开启，false 为关闭</param>
        /// <param name="errorMessage">失败时的错误说明（可供调用方记录日志或弹窗提示）</param>
        public static bool SetAutoStart(bool enable, out string? errorMessage)
        {
            errorMessage = null;

            try
            {
                if (enable)
                {
                    string command = BuildStartupCommand();
                    using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunRegistryKeyPath, writable: true)
                        ?? throw new InvalidOperationException("无法打开或创建注册表 Run 键。");

                    key.SetValue(AppValueName, command, RegistryValueKind.String);
                }
                else
                {
                    using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: true);
                    key?.DeleteValue(AppValueName, throwOnMissingValue: false);
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"写入自启动注册表失败（可能被安全软件拦截）：{ex.Message}";
                return false;
            }
        }
    }
}