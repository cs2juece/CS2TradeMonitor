using System;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class VirtualListPanelRenderQueue
    {
        private bool _renderQueued;

        public void Queue(Control owner, Func<bool> isUnavailable, Func<bool> shouldRender, Action renderNow)
        {
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentNullException.ThrowIfNull(isUnavailable);
            ArgumentNullException.ThrowIfNull(shouldRender);
            ArgumentNullException.ThrowIfNull(renderNow);

            if (isUnavailable() || !owner.IsHandleCreated)
                return;

            if (owner.InvokeRequired)
            {
                try
                {
                    owner.BeginInvoke(new Action(() => Queue(owner, isUnavailable, shouldRender, renderNow)));
                }
                catch
                {
                    // owner 释放或句柄销毁时丢弃本次虚拟列表渲染投递。
                }

                return;
            }

            if (_renderQueued)
                return;

            _renderQueued = true;
            try
            {
                owner.BeginInvoke(new Action(() => ProcessQueuedRender(isUnavailable, shouldRender, renderNow)));
            }
            catch
            {
                _renderQueued = false;
            }
        }

        internal void ProcessQueuedRender(Func<bool> isUnavailable, Func<bool> shouldRender, Action renderNow)
        {
            _renderQueued = false;
            if (!isUnavailable() && shouldRender())
                renderNow();
        }
    }
}
