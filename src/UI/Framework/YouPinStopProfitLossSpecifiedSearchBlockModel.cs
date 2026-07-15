using System;
using System.Drawing;
using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class YouPinStopProfitLossSpecifiedSearchBlockModel
    {
        public static int BuildPanelHeight(bool hasCandidates)
        {
            return UIUtils.S(hasCandidates ? 172 : 68);
        }

        public static YouPinStopProfitLossSpecifiedSearchBlockLayout BuildLayout(
            int panelWidth,
            Padding panelPadding,
            int inputHeight,
            int addButtonHeight,
            int clearButtonHeight,
            bool suggestionVisible,
            int suggestionHeight)
        {
            int inputLeft = Math.Min(UIUtils.S(230), Math.Max(panelPadding.Left + UIUtils.S(140) + UIUtils.S(12), panelWidth / 5));
            int gap = UIUtils.S(10);
            int buttonWidth = UIUtils.S(72);
            int buttonsWidth = buttonWidth * 2 + gap * 2;
            int inputWidth = Math.Max(UIUtils.S(300), panelWidth - inputLeft - panelPadding.Right - buttonsWidth);
            var labelBounds = new Rectangle(
                panelPadding.Left,
                UIUtils.S(8),
                Math.Min(UIUtils.S(170), inputLeft - panelPadding.Left - UIUtils.S(12)),
                UIUtils.S(28));
            var inputBounds = new Rectangle(inputLeft, UIUtils.S(8), inputWidth, inputHeight);
            var addButtonBounds = new Rectangle(inputBounds.Right + gap, UIUtils.S(7), buttonWidth, addButtonHeight);
            var clearButtonBounds = new Rectangle(addButtonBounds.Right + gap, UIUtils.S(7), buttonWidth, clearButtonHeight);
            var suggestionBounds = new Rectangle(inputLeft, inputBounds.Bottom + UIUtils.S(4), inputWidth, suggestionHeight);
            int statusTop = suggestionVisible
                ? suggestionBounds.Bottom + UIUtils.S(2)
                : inputBounds.Bottom + UIUtils.S(6);
            var statusBounds = new Rectangle(inputLeft, statusTop, Math.Max(1, panelWidth - inputLeft - panelPadding.Right), UIUtils.S(20));

            return new YouPinStopProfitLossSpecifiedSearchBlockLayout(
                labelBounds,
                inputBounds,
                addButtonBounds,
                clearButtonBounds,
                suggestionBounds,
                statusBounds);
        }
    }

    internal readonly record struct YouPinStopProfitLossSpecifiedSearchBlockLayout(
        Rectangle LabelBounds,
        Rectangle InputBounds,
        Rectangle AddButtonBounds,
        Rectangle ClearButtonBounds,
        Rectangle SuggestionBounds,
        Rectangle StatusBounds);
}
