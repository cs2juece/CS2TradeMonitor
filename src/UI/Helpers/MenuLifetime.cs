using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.src.UI.Helpers
{
    internal static class MenuLifetime
    {
        public static void PostAfterMenuMessage(Control? invoker, Action action, string source)
        {
            if (action == null)
            {
                return;
            }

            void Run()
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Error("Menu", source + " failed.", ex);
                }
            }

            PostAfterMenuMessageCore(invoker, Run);
        }

        public static void PostAfterMenuMessage(Control? invoker, Func<Task> action, string source)
        {
            if (action == null)
            {
                return;
            }

            async void Run()
            {
                try
                {
                    await action();
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Error("Menu", source + " failed.", ex);
                }
            }

            PostAfterMenuMessageCore(invoker, Run);
        }

        public static void DisposeLater(ContextMenuStrip? menu, Control? invoker, string source)
        {
            if (menu == null || menu.IsDisposed)
            {
                return;
            }

            if (menu.Visible)
            {
                ToolStripDropDownClosedEventHandler? closedHandler = null;
                closedHandler = (_, __) =>
                {
                    try
                    {
                        menu.Closed -= closedHandler;
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    BeginDispose(menu, invoker, source + ".Closed");
                };

                try
                {
                    menu.Closed += closedHandler;
                }
                catch (ObjectDisposedException)
                {
                    // 菜单已经释放时不再挂关闭事件。
                }

                return;
            }

            BeginDispose(menu, invoker, source);
        }

        private static void BeginDispose(ContextMenuStrip menu, Control? invoker, string source)
        {
            void DisposeNow()
            {
                try
                {
                    if (menu.IsDisposed)
                    {
                        return;
                    }

                    if (menu.Visible)
                    {
                        DisposeLater(menu, invoker, source + ".Visible");
                        return;
                    }

                    PostWhenIdle(() =>
                    {
                        try
                        {
                            if (!menu.IsDisposed && !menu.Visible)
                            {
                                menu.Dispose();
                            }
                            else if (!menu.IsDisposed)
                            {
                                DisposeLater(menu, invoker, source + ".StillVisible");
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            // 延迟释放执行前菜单可能已被系统释放。
                        }
                    });
                }
                catch (ObjectDisposedException)
                {
                    // 投递延迟释放前 invoker 已释放，菜单生命周期已结束。
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Info("Menu", $"{source}: deferred menu dispose failed: {ex.Message}");
                }
            }

            try
            {
                if (invoker != null && !invoker.IsDisposed && invoker.IsHandleCreated)
                {
                    invoker.BeginInvoke((Action)DisposeNow);
                    return;
                }
            }
            catch (ObjectDisposedException)
            {
                // invoker 释放时无法立即投递，改走后续 idle 兜底。
            }
            catch (InvalidOperationException)
            {
                // invoker 句柄不可用时无法立即投递，改走后续 idle 兜底。
            }

            EventHandler? idleHandler = null;
            idleHandler = (_, __) =>
            {
                System.Windows.Forms.Application.Idle -= idleHandler;
                DisposeNow();
            };
            System.Windows.Forms.Application.Idle += idleHandler;
        }

        private static void PostAfterMenuMessageCore(Control? invoker, Action action)
        {
            void QueueIdle()
            {
                PostWhenIdle(action);
            }

            try
            {
                if (invoker != null && !invoker.IsDisposed && invoker.IsHandleCreated)
                {
                    invoker.BeginInvoke((Action)QueueIdle);
                    return;
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                // 应用退出期间 Idle 事件可能不可挂，菜单释放已尽力处理。
            }

            QueueIdle();
        }

        private static void PostWhenIdle(Action action)
        {
            EventHandler? idleHandler = null;
            idleHandler = (_, __) =>
            {
                System.Windows.Forms.Application.Idle -= idleHandler;
                action();
            };
            System.Windows.Forms.Application.Idle += idleHandler;
        }
    }
}
