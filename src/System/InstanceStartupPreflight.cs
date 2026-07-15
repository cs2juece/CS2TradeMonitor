using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Infrastructure.Paths;

namespace CS2TradeMonitor.src.SystemServices
{
    internal static class InstanceStartupPreflight
    {
        public static void EnsureWritable()
        {
            EnsureWritable(InstanceRuntimeContext.Current);
        }

        internal static void EnsureWritable(IInstanceRuntimeContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.EnsureWritable();
        }

        internal static string BuildFailureMessage(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            return "程序目录无法保存用户数据，软件已拒绝启动。\n\n"
                + exception.Message
                + "\n\n请将完整软件目录复制到当前用户拥有写入权限的位置后重新启动。"
                + "\n软件不会提权，也不会回退使用 LocalAppData。";
        }
    }
}
