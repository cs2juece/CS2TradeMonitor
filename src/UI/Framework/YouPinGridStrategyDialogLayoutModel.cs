using System.Drawing;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal readonly record struct YouPinGridStrategyDialogLayout(
        Rectangle Title,
        Rectangle Subtitle,
        Rectangle InventoryCard,
        Rectangle SettingsCard,
        Rectangle CancelButton,
        Rectangle SaveButton);

    internal static class YouPinGridStrategyDialogLayoutModel
    {
        internal static YouPinGridStrategyDialogLayout Build(
            Size clientSize,
            Size cancelButtonSize,
            Size saveButtonSize,
            float scaleFactor)
        {
            float scale = scaleFactor > 0F ? scaleFactor : 1F;
            int S(int value) => (int)(value * scale);

            int pad = S(22);
            int gap = S(14);
            int contentTop = S(82);
            int contentHeight = S(560);
            int leftWidth = S(302);
            var inventoryCard = new Rectangle(pad, contentTop, leftWidth, contentHeight);
            var settingsCard = new Rectangle(
                inventoryCard.Right + gap,
                contentTop,
                Math.Max(1, clientSize.Width - pad - inventoryCard.Right - gap),
                contentHeight);
            int buttonTop = S(656);
            var saveButton = new Rectangle(
                clientSize.Width - pad - saveButtonSize.Width,
                buttonTop,
                saveButtonSize.Width,
                saveButtonSize.Height);
            var cancelButton = new Rectangle(
                saveButton.Left - S(10) - cancelButtonSize.Width,
                buttonTop,
                cancelButtonSize.Width,
                cancelButtonSize.Height);

            return new YouPinGridStrategyDialogLayout(
                new Rectangle(pad, S(16), Math.Max(1, clientSize.Width - pad * 2), S(32)),
                new Rectangle(pad, S(48), Math.Max(1, clientSize.Width - pad * 2), S(24)),
                inventoryCard,
                settingsCard,
                cancelButton,
                saveButton);
        }
    }
}
