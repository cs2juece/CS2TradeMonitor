using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class MainPanelTabBar
    {
        private readonly Action<string> _selectTab;
        private readonly Dictionary<string, LiteButton> _buttons = new(StringComparer.OrdinalIgnoreCase);
        private FlowLayoutPanel? _flow;

        public MainPanelTabBar(Action<string> selectTab)
        {
            _selectTab = selectTab;
            Wrapper = new Panel
            {
                Height = UIUtils.S(42),
                Padding = new Padding(0, 0, 0, UIUtils.S(8)),
                BackColor = Color.Transparent
            };
        }

        public Panel Wrapper { get; }

        public void Attach(Control container)
        {
            _flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent
            };
            Wrapper.Controls.Add(_flow);
            container.Controls.Add(Wrapper);
            container.Controls.SetChildIndex(Wrapper, 1);
            EnsureButtons();
        }

        public void Show()
        {
            Wrapper.Visible = true;
        }

        public void UpdateSelection(string activeTab)
        {
            EnsureButtons();
            foreach (KeyValuePair<string, LiteButton> pair in _buttons)
            {
                pair.Value.IsActive = string.Equals(pair.Key, activeTab, StringComparison.OrdinalIgnoreCase);
            }
        }

        private void EnsureButtons()
        {
            if (_flow == null || _buttons.Count > 0)
                return;

            _flow.SuspendLayout();
            try
            {
                foreach (MainPanelTabOption option in MainPanelTabKeys.Options)
                    AddButton(option.Key, option.Text);
            }
            finally
            {
                _flow.ResumeLayout(false);
            }
        }

        private void AddButton(string key, string text)
        {
            if (_flow == null)
                return;

            var button = new LiteButton(text, false)
            {
                Width = UIUtils.S(MainPanelTabKeys.GetLogicalButtonWidth(text)),
                Height = UIUtils.S(30),
                Margin = UIUtils.S(new Padding(0, 0, 10, 0))
            };
            button.Click += (_, __) => _selectTab(key);
            _buttons[key] = button;
            _flow.Controls.Add(button);
        }
    }
}
