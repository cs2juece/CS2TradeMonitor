namespace CS2TradeMonitor.Infrastructure.Diagnostics
{
    internal static class DetailedDiagnosticOperationContext
    {
        private static readonly AsyncLocal<string?> CurrentValue = new();

        public static string? CurrentOperationId => CurrentValue.Value;

        public static IDisposable Begin(string operationId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
            string? previous = CurrentValue.Value;
            CurrentValue.Value = operationId;
            return new RestoreScope(previous);
        }

        private sealed class RestoreScope : IDisposable
        {
            private readonly string? _previous;
            private bool _disposed;

            public RestoreScope(string? previous) => _previous = previous;

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                CurrentValue.Value = _previous;
            }
        }
    }
}
