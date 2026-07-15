using CS2TradeMonitor.src.UI.Controls;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class DataPageStatusRenderer
    {
        public static void ApplyDataSourceStatus(
            DataSourceStatusViewModel status,
            Label? cardStatus,
            Label? lastRefresh,
            Label? source,
            Label? interval)
        {
            SetStatus(cardStatus, status.CardStatus);
            SetStatus(lastRefresh, status.LastRefresh);
            SetStatus(source, status.Source);
            SetStatus(interval, status.Interval);
        }

        public static Color StatusToneColor(DataSourceStatusTone tone)
        {
            return tone switch
            {
                DataSourceStatusTone.Success => Color.FromArgb(0, 180, 80),
                DataSourceStatusTone.Warning => UIColors.TextWarn,
                DataSourceStatusTone.Muted => UIColors.TextSub,
                _ => UIColors.TextMain
            };
        }

        private static void SetStatus(Label? label, DataSourceStatusText status)
        {
            if (label == null)
                return;

            label.Text = status.Text;
            label.ForeColor = StatusToneColor(status.Tone);
        }
    }
}
