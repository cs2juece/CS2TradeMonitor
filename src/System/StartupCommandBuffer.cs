namespace CS2TradeMonitor
{
    internal sealed class StartupCommandBuffer
    {
        private readonly object _gate = new();
        private readonly Queue<string[]> _pending = new();
        private Action<string[]>? _handler;

        public void Dispatch(string[] args)
        {
            string[] copy = args?.ToArray() ?? Array.Empty<string>();
            Action<string[]>? handler;
            lock (_gate)
            {
                handler = _handler;
                if (handler == null)
                {
                    _pending.Enqueue(copy);
                    return;
                }
            }

            handler(copy);
        }

        public void Attach(Action<string[]> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            string[][] pending;
            lock (_gate)
            {
                if (_handler != null)
                    throw new InvalidOperationException("启动参数处理器只能绑定一次。");

                _handler = handler;
                pending = _pending.ToArray();
                _pending.Clear();
            }

            foreach (string[] args in pending)
                handler(args);
        }
    }
}
