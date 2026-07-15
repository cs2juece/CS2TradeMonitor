using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class PhoneAlertPageStatusRenderer
    {
        private readonly Dictionary<string, Label> _channelStatusLabels;

        public PhoneAlertPageStatusRenderer(Dictionary<string, Label> channelStatusLabels)
        {
            _channelStatusLabels = channelStatusLabels ?? throw new ArgumentNullException(nameof(channelStatusLabels));
        }

        public void ApplySummary(
            PhoneAlertSummaryRenderTargets targets,
            PhoneAlertSummaryViewModel view,
            bool enabled,
            string? overrideText = null)
        {
            ArgumentNullException.ThrowIfNull(targets);
            ArgumentNullException.ThrowIfNull(view);

            if (targets.SummaryLabel == null)
                return;

            if (!string.IsNullOrWhiteSpace(overrideText))
            {
                targets.SummaryLabel.Text = overrideText;
                targets.SummaryLabel.ForeColor = UIColors.TextSub;
                return;
            }

            if (targets.EnabledCheck != null && targets.EnabledCheck.Checked != enabled)
                targets.EnabledCheck.Checked = enabled;

            SetLabel(targets.BindStatusLabel, view.BindStatus);
            SetLabel(targets.EnabledStatusLabel, view.EnabledStatus);
            SetLabel(targets.LastSendLabel, view.LastSend);
            SetLabel(targets.FailureLabel, view.Failure);
            targets.SummaryLabel.Text = view.Summary.Text;
            targets.SummaryLabel.ForeColor = view.Summary.Color;
            SetLabel(targets.SecretConfigStatusLabel, view.SecretConfig);
        }

        public void ApplyChannelStatus(PhoneAlertChannelConfig channel, PhoneAlertTextViewModel status)
        {
            ApplyChannelStatus(channel, status.Text, status.Color);
        }

        public void ApplyChannelStatus(PhoneAlertChannelConfig channel, string text, Color color)
        {
            if (_channelStatusLabels.TryGetValue(channel.Id, out Label? label) && !label.IsDisposed)
            {
                label.Text = text;
                label.ForeColor = color;
            }
        }

        public void ApplySecretConfigStatus(Label? label, PhoneAlertTextViewModel status)
        {
            SetLabel(label, status);
        }

        private static void SetLabel(Label? label, PhoneAlertTextViewModel status)
        {
            PhoneAlertPageControls.SetLabel(label, status.Text, status.Color);
        }
    }

    internal sealed record PhoneAlertSummaryRenderTargets(
        Label? SummaryLabel,
        LiteCheck? EnabledCheck,
        Label? BindStatusLabel,
        Label? EnabledStatusLabel,
        Label? LastSendLabel,
        Label? FailureLabel,
        Label? SecretConfigStatusLabel);
}
