using System;
using System.Collections.Generic;

namespace CS2TradeMonitor.src.Core.Lifecycle;

public sealed class CompositeDisposable : IDisposable
{
    private readonly object _sync = new();
    private List<IDisposable>? _items = new();

    public T Add<T>(T disposable) where T : IDisposable
    {
        if (disposable is null)
            throw new ArgumentNullException(nameof(disposable));

        bool disposeNow;
        lock (_sync)
        {
            disposeNow = _items is null;
            if (!disposeNow)
                _items!.Add(disposable);
        }

        if (disposeNow)
            SafeDispose(disposable);

        return disposable;
    }

    public void Clear()
    {
        List<IDisposable> snapshot;
        lock (_sync)
        {
            if (_items is null || _items.Count == 0)
                return;

            snapshot = _items;
            _items = new List<IDisposable>();
        }

        DisposeAll(snapshot);
    }

    public void Dispose()
    {
        List<IDisposable>? snapshot;
        lock (_sync)
        {
            snapshot = _items;
            _items = null;
        }

        if (snapshot is not null)
            DisposeAll(snapshot);
    }

    private static void DisposeAll(List<IDisposable> snapshot)
    {
        for (int i = snapshot.Count - 1; i >= 0; i--)
            SafeDispose(snapshot[i]);
    }

    private static void SafeDispose(IDisposable disposable)
    {
        try
        {
            disposable.Dispose();
        }
        catch
        {
            // Dispose paths must stay non-fatal during window shutdown.
        }
    }
}
