using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class MarketAlertPagePresenter
    {
        public static MarketAlertTextViewModel BuildTestResultStatus(bool delivered)
        {
            return delivered
                ? new MarketAlertTextViewModel("已发送测试预警", Color.FromArgb(0, 150, 80))
                : new MarketAlertTextViewModel("发送失败，请查看日志", Color.FromArgb(200, 80, 60));
        }

        public static void ApplyLabel(Label? label, MarketAlertTextViewModel viewModel)
        {
            if (label == null || label.IsDisposed)
                return;

            label.Text = viewModel.Text;
            label.ForeColor = viewModel.Color;
        }
    }

    internal sealed record MarketAlertTextViewModel(string Text, Color Color);
}
