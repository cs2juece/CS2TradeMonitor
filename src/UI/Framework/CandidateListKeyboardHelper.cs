using System;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class CandidateListKeyboardHelper
    {
        public static bool HandleKeyDown(ListBox? list, KeyEventArgs e, Action accept, Action clear)
        {
            if (e.KeyCode == Keys.Enter)
            {
                accept();
                e.SuppressKeyPress = true;
                return true;
            }

            if (e.KeyCode == Keys.Escape)
            {
                clear();
                e.SuppressKeyPress = true;
                return true;
            }

            if (list == null || !list.Visible || list.Items.Count == 0)
                return false;

            if (e.KeyCode == Keys.Down)
            {
                int next = list.SelectedIndex < 0
                    ? 0
                    : Math.Min(list.Items.Count - 1, list.SelectedIndex + 1);
                list.SelectedIndex = next;
                e.SuppressKeyPress = true;
                return true;
            }

            if (e.KeyCode == Keys.Up)
            {
                int next = list.SelectedIndex < 0
                    ? list.Items.Count - 1
                    : Math.Max(0, list.SelectedIndex - 1);
                list.SelectedIndex = next;
                e.SuppressKeyPress = true;
                return true;
            }

            return false;
        }
    }
}
