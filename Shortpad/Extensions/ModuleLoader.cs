using Shortpad.Contracts.Constants;
using Shortpad.Contracts.Hosting;
using Shortpad.Contracts.Models;
using System.Diagnostics;
using System.Text.Json;
using Shortpad.Managers;
using System.IO.Pipes;
using System.Windows;
using System.Text;
using System.IO;

namespace Shortpad.Extensions
{
    /// <summary>
    /// 通过外部宿主进程加载扩展，防止扩展崩溃牵连主程序
    /// 线程模型：
    /// - _lifecycleGate 串行化 LoadModule / UnloadModule / UnloadAll 的整个流程（允许长时间等待，但不在持锁期间 BeginInvoke/Invoke 到 UI）
    /// - _hostsLock 只保护 _hosts 字典的增删查，锁内不做任何 IO 或等待
    /// - _isShuttingDown 在 UnloadAll 开始时置位，之后任何新宿主都不会再登记
    /// </summary>
    public class ModuleLoader
    {
        private static readonly TimeSpan GateWaitTimeout = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(3);
        private const int CommandTimeoutMs = 5000;

        private readonly Dictionary<string, ExtensionHostHandle> _hosts = new();
        private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
        private readonly ToastManager _toast = new();
        private readonly object _hostsLock = new();
        private volatile bool _isShuttingDown;

        /// <summary>
        /// 加载并激活扩展；若宿主进程已在运行且连接正常，则直接向其发送 activate 指令
        /// </summary>
        public void LoadModule(string modulePath)
        {
            if (_isShuttingDown) return;

            if (!_lifecycleGate.Wait(GateWaitTimeout))
            {
                _toast.Show("扩展正在加载或卸载，请稍后再试", ToastType.Warning);
                return;
            }

            try
            {
                if (_isShuttingDown) return;
                LoadModuleCore(modulePath);
            }
            catch (Exception ex)
            {
                _toast.Show($"加载或激活模块时出错：{Brief(ex.Message, "未知错误")}", ToastType.Error);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        /// <summary>
        /// 卸载单个扩展的宿主进程
        /// </summary>
        public void UnloadModule(string moduleName)
        {
            bool gateHeld = _lifecycleGate.Wait(GateWaitTimeout);
            try
            {
                ExtensionHostHandle? handle = RemoveHost(moduleName);
                if (handle is not null)
                {
                    ShutdownHandles(
                        new List<(string Name, ExtensionHostHandle Handle)> { (moduleName, handle) },
                        DateTime.UtcNow + ShutdownBudget);
                }
            }
            finally
            {
                if (gateHeld) _lifecycleGate.Release();
            }
        }

        /// <summary>
        /// 关闭所有扩展宿主
        /// 整体受时间预算限制，内部异常全部吞掉，保证调用方（OnExit）能继续后续清理
        /// </summary>
        public void UnloadAll()
        {
            // 先置位：之后的 LoadModule 直接返回，正在进行的加载在登记时会被拒绝
            _isShuttingDown = true;

            bool gateHeld = _lifecycleGate.Wait(GateWaitTimeout);
            try
            {
                List<(string Name, ExtensionHostHandle Handle)> snapshot;
                lock (_hostsLock)
                {
                    snapshot = _hosts.Select(kv => (Name: kv.Key, Handle: kv.Value)).ToList();
                    _hosts.Clear();
                }

                ShutdownHandles(snapshot, DateTime.UtcNow + ShutdownBudget);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModuleLoader] UnloadAll 异常: {ex}");
            }
            finally
            {
                if (gateHeld) _lifecycleGate.Release();
            }
        }

        /// <summary>
        /// 调用方需已持有 _lifecycleGate
        /// </summary>
        private void LoadModuleCore(string modulePath)
        {
            string assemblyName = Path.GetFileNameWithoutExtension(modulePath);

            ExtensionHostHandle? existing = GetHost(assemblyName);
            if (existing is not null)
            {
                if (IsHandleAlive(existing))
                {
                    try
                    {
                        ActivateHandle(existing);
                        return;
                    }
                    catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                    {
                        // 连接已不可用：丢弃旧句柄，下面重建
                    }
                    // TimeoutException（宿主忙）和 InvalidOperationException（扩展自身激活失败）
                    // 不销毁宿主，交给外层 catch 提示用户
                }

                DiscardHost(assemblyName, existing);
            }

            StartNewHost(assemblyName, modulePath);
        }

        /// <summary>
        /// 启动新宿主并完成激活。调用方需已持有 _lifecycleGate
        /// 任何一步失败都会清理进程和管道，并撤销登记
        /// </summary>
        private void StartNewHost(string assemblyName, string modulePath)
        {
            string hostPath = ResolveHostExecutable();
            string pipeName = $"{HostProtocol.PipePrefix}{Guid.NewGuid():N}";

            Process process = StartHostProcess(hostPath, modulePath, pipeName);
            ExtensionHostHandle? handle = null;
            bool registered = false;

            try
            {
                // 先订阅退出事件，再开启事件通知，避免进程恰好在间隙中退出而漏掉事件
                process.Exited += (_, _) => OnProcessExited(assemblyName, process);
                process.EnableRaisingEvents = true;

                handle = ConnectToHost(pipeName, process);
                RequestMetadata(handle);

                // 先登记再激活：激活期间若应用开始退出，UnloadAll 仍能看到并关闭这个宿主
                if (!TryRegisterHost(assemblyName, handle))
                {
                    throw new InvalidOperationException("应用正在退出，已取消加载扩展");
                }
                registered = true;

                ActivateHandle(handle);
                handle.Ready = true;
            }
            catch
            {
                if (handle is not null)
                {
                    if (registered) RemoveHostIfCurrent(assemblyName, handle);
                    handle.Dispose(); // 关闭管道并结束进程
                }
                else
                {
                    try { process.Kill(true); } catch { }
                    try { process.Dispose(); } catch { }
                }

                throw;
            }
        }

        /// <summary>
        /// 宿主进程退出事件（线程池线程触发）
        /// 只在仍登记的情况下处理，并且只对已激活成功的宿主提示
        /// </summary>
        private void OnProcessExited(string assemblyName, Process process)
        {
            int exitCode;
            try { exitCode = process.ExitCode; }
            catch { exitCode = -1; }

            ExtensionHostHandle? removed = null;
            bool shouldWarn = false;

            lock (_hostsLock)
            {
                if (_hosts.TryGetValue(assemblyName, out var h) && ReferenceEquals(h.Process, process))
                {
                    _hosts.Remove(assemblyName);
                    removed = h;
                    shouldWarn = h.Ready && !_isShuttingDown;
                }
            }

            if (removed is null) return;

            removed.Dispose();

            if (shouldWarn)
            {
                _toast.Show($"警告：扩展 {assemblyName} 意外退出（{DescribeExitCode(exitCode)}）", ToastType.Warning);
            }
        }

        private void ActivateHandle(ExtensionHostHandle handle)
        {
            var activateResult = SendCommand(handle, new HostCommand("activate"));
            if (!activateResult.Ok)
            {
                throw new InvalidOperationException(Brief(activateResult.Error, "激活失败"));
            }
        }

        private ModuleMetadata RequestMetadata(ExtensionHostHandle handle)
        {
            var response = SendCommand(handle, new HostCommand("metadata"));
            if (!response.Ok)
            {
                throw new InvalidOperationException($"读取扩展元数据失败：{Brief(response.Error, "未知错误")}");
            }

            return response.Metadata
                   ?? throw new InvalidOperationException("无法读取扩展元数据");
        }

        #region 宿主登记表（_hosts）的访问，所有操作都在 _hostsLock 内完成

        private ExtensionHostHandle? GetHost(string name)
        {
            lock (_hostsLock)
            {
                return _hosts.TryGetValue(name, out var h) ? h : null;
            }
        }

        private ExtensionHostHandle? RemoveHost(string name)
        {
            lock (_hostsLock)
            {
                if (_hosts.TryGetValue(name, out var h))
                {
                    _hosts.Remove(name);
                    return h;
                }
                return null;
            }
        }

        private bool TryRegisterHost(string name, ExtensionHostHandle handle)
        {
            lock (_hostsLock)
            {
                if (_isShuttingDown) return false;
                _hosts[name] = handle;
                return true;
            }
        }

        private void RemoveHostIfCurrent(string name, ExtensionHostHandle handle)
        {
            lock (_hostsLock)
            {
                if (_hosts.TryGetValue(name, out var current) && ReferenceEquals(current, handle))
                {
                    _hosts.Remove(name);
                }
            }
        }

        private void DiscardHost(string name, ExtensionHostHandle handle)
        {
            RemoveHostIfCurrent(name, handle);
            handle.Dispose();
        }

        #endregion

        /// <summary>
        /// 按时间预算关闭一组宿主：先尽量发送 deactivate / shutdown，之后无论成功与否都 Dispose（会结束进程）
        /// 单个宿主的任何异常都只记录，不影响其它宿主
        /// </summary>
        private static void ShutdownHandles(List<(string Name, ExtensionHostHandle Handle)> items, DateTime deadline)
        {
            foreach (var (name, handle) in items)
            {
                try
                {
                    if (IsHandleAlive(handle))
                    {
                        TrySendBeforeDeadline(handle, "deactivate", deadline);
                        TrySendBeforeDeadline(handle, "shutdown", deadline);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ModuleLoader] 关闭扩展 {name} 时出错: {ex}");
                }
                finally
                {
                    handle.Dispose();
                }
            }
        }

        private static void TrySendBeforeDeadline(ExtensionHostHandle handle, string action, DateTime deadline)
        {
            double remainingMs = (deadline - DateTime.UtcNow).TotalMilliseconds;
            if (remainingMs <= 0) return;

            int timeoutMs = (int)Math.Min(remainingMs, CommandTimeoutMs);
            try
            {
                SendCommand(handle, new HostCommand(action), timeoutMs);
            }
            catch (Exception ex)
            {
                // 超时或连接已断开：交由后续 Dispose 强制结束
                Debug.WriteLine($"[ModuleLoader] 发送 {action} 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 进程存活、管道连通、读取循环未结束，才认为句柄可复用
        /// </summary>
        private static bool IsHandleAlive(ExtensionHostHandle handle)
        {
            if (handle.ConnectionLost) return false;
            if (handle.Process is null || IsProcessExited(handle.Process)) return false;

            try { return handle.Pipe.IsConnected; }
            catch { return false; }
        }

        private static bool IsProcessExited(Process process)
        {
            try { return process.HasExited; }
            catch (InvalidOperationException) { return true; } // 进程对象已释放
        }

        /// <summary>
        /// 连接到扩展宿主并启动后台监听
        /// 失败时会释放自己创建的管道
        /// </summary>
        private static ExtensionHostHandle ConnectToHost(string pipeName, Process process)
        {
            var client = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.WriteThrough);

            try
            {
                var timeoutMs = (int)TimeSpan.FromSeconds(10).TotalMilliseconds;
                var sw = Stopwatch.StartNew();
                while (!client.IsConnected && sw.ElapsedMilliseconds < timeoutMs)
                {
                    if (IsProcessExited(process))
                    {
                        throw new IOException($"扩展宿主已退出：{DescribeProcessExit(process)}");
                    }
                    try { client.Connect(200); } catch (TimeoutException) { }
                }

                if (!client.IsConnected)
                {
                    throw new TimeoutException("连接扩展宿主超时");
                }

                var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true);
                var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };

                var handle = new ExtensionHostHandle(client, reader, writer) { Process = process };

                _ = Task.Run(async () =>
                {
                    try
                    {
                        while (true)
                        {
                            string? line;
                            try
                            {
                                line = await reader.ReadLineAsync();
                            }
                            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                            {
                                break; // 管道已断开或已释放，退出循环防止空转吃满 CPU
                            }

                            if (line == null) break;

                            try
                            {
                                using var doc = JsonDocument.Parse(line);
                                var root = doc.RootElement;
                                if (root.ValueKind != JsonValueKind.Object) continue;

                                if (root.TryGetProperty("Action", out var action) &&
                                    action.ValueKind == JsonValueKind.String &&
                                    action.GetString() == "toast")
                                {
                                    var cmd = root.Deserialize<HostCommand>();
                                    if (cmd != null)
                                    {
                                        // ToastManager.Show 内部已切换到 UI 线程，可直接在后台调用
                                        ToastType type = Enum.TryParse<ToastType>(cmd.ToastType, out var t)
                                            ? t : ToastType.Common;
                                        new ToastManager().Show(cmd.Message ?? "", type);
                                    }
                                }
                                else
                                {
                                    var response = root.Deserialize<HostResult>();
                                    if (response != null)
                                    {
                                        handle.LastResponse = response;
                                        handle.ResponseEvent.Set();
                                    }
                                }
                            }
                            catch (JsonException)
                            {
                                // 单行格式错误，忽略
                            }
                        }
                    }
                    finally
                    {
                        // 连接结束：标记失效，并唤醒正在等待响应的 SendCommand，让它立刻得到"连接已断开"
                        handle.ConnectionLost = true;
                        try { handle.ResponseEvent.Set(); } catch (ObjectDisposedException) { }
                    }
                });

                return handle;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        private static Process StartHostProcess(string hostPath, string modulePath, string pipeName)
        {
            var psi = new ProcessStartInfo
            {
                FileName = hostPath,
                ArgumentList = { modulePath, pipeName },
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = Path.GetDirectoryName(hostPath) ?? AppContext.BaseDirectory
            };

            var process = Process.Start(psi)
                          ?? throw new InvalidOperationException("无法启动扩展宿主进程");

            if (process.HasExited)
            {
                string reason = DescribeProcessExit(process);
                process.Dispose();
                throw new InvalidOperationException($"扩展宿主启动失败：{reason}");
            }

            return process;
        }

        /// <summary>
        /// 宿主没有重定向标准输出/错误，所以只能根据退出码给出原因，详细信息在宿主日志里。
        /// </summary>
        private static string DescribeProcessExit(Process process)
        {
            try { return DescribeExitCode(process.ExitCode); }
            catch { return "退出原因未知，详见 logs/ExtensionHost.log"; }
        }

        /// <summary>
        /// 退出码约定与 ExtensionHost/Program.cs 保持一致。
        /// </summary>
        private static string DescribeExitCode(int exitCode) => exitCode switch
        {
            1 => "宿主参数错误或发生致命异常，详见 logs/ExtensionHost.log",
            2 => "扩展模块加载失败，可能与主程序版本不匹配，详见 logs/ExtensionHost.log",
            3 => "宿主通信异常，详见 logs/ExtensionHost.log",
            _ => $"退出码 {exitCode}，详见 logs/ExtensionHost.log"
        };

        /// <summary>
        /// 只取错误信息的第一行并限制长度，避免把整段堆栈塞进提示里。
        /// </summary>
        private static string Brief(string? text, string fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;

            string first = text.Trim().Split('\n')[0].TrimEnd('\r');
            return first.Length > 200 ? first[..200] + "…" : first;
        }

        /// <summary>
        /// 发送命令并等待响应。调用方需保证 handle 未被释放。
        /// 锁等待和响应等待都受 timeoutMs 限制，避免被卡住的宿主拖死调用方。
        /// </summary>
        private static HostResult SendCommand(ExtensionHostHandle handle, HostCommand command, int timeoutMs = CommandTimeoutMs)
        {
            if (!Monitor.TryEnter(handle.SyncRoot, timeoutMs))
            {
                throw new TimeoutException("扩展宿主忙，无法发送命令");
            }

            try
            {
                if (handle.Process is not null && IsProcessExited(handle.Process))
                {
                    throw new IOException($"扩展宿主已退出：{DescribeProcessExit(handle.Process)}");
                }

                if (handle.ConnectionLost)
                {
                    throw new IOException("扩展宿主连接已断开");
                }

                handle.ResponseEvent.Reset();
                handle.LastResponse = null;

                handle.Writer.WriteLine(JsonSerializer.Serialize(command));

                if (!handle.ResponseEvent.Wait(timeoutMs))
                {
                    throw new TimeoutException("扩展宿主响应超时");
                }

                var response = handle.LastResponse;
                if (response is null)
                {
                    if (handle.ConnectionLost)
                    {
                        throw new IOException("扩展宿主连接已断开");
                    }

                    throw new InvalidOperationException("扩展宿主无响应");
                }

                return response;
            }
            finally
            {
                Monitor.Exit(handle.SyncRoot);
            }
        }

        private string ResolveHostExecutable()
        {
            string exeName = HostProtocol.HostExecutableName;
            if (!exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                exeName += ".exe";

            string directPath = Path.Combine(AppContext.BaseDirectory, exeName);
            if (File.Exists(directPath))
            {
                return directPath;
            }

#if DEBUG
            string baseDir = AppContext.BaseDirectory;
            string[] candidates =
            {
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\ExtensionHost\bin\Debug\net8.0-windows10.0.17763.0", exeName)),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\ExtensionHost\bin\Debug\net8.0-windows10.0.17763.0", exeName)),
            };
            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }
#endif

            throw new FileNotFoundException(
                $"未找到扩展宿主 {exeName}，请确认它和主程序在同一目录。");
        }

        private sealed class ExtensionHostHandle : IDisposable
        {
            public ExtensionHostHandle(NamedPipeClientStream pipe, StreamReader reader, StreamWriter writer)
            {
                Pipe = pipe;
                Reader = reader;
                Writer = writer;
            }

            public Process? Process { get; set; }
            public NamedPipeClientStream Pipe { get; }
            public StreamReader Reader { get; }
            public StreamWriter Writer { get; }
            public object SyncRoot { get; } = new();

            public ManualResetEventSlim ResponseEvent { get; } = new(false);
            public HostResult? LastResponse { get; set; }

            /// <summary>
            /// 读取循环已结束（管道断开或已释放）
            /// </summary>
            public volatile bool ConnectionLost;

            /// <summary>
            /// 已完成首次激活
            /// 只有 Ready 的宿主意外退出时才提示用户
            /// </summary>
            public volatile bool Ready;

            public void Dispose()
            {
                try { Pipe.Dispose(); } catch { }
                try { Reader.Dispose(); } catch { }
                try { Writer.Dispose(); } catch { }
                try { ResponseEvent.Dispose(); } catch { }
                try { Process?.Kill(true); } catch { }
                try { Process?.Dispose(); } catch { }
            }
        }
    }
}