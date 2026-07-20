using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using System.Drawing;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed record YouPinInventoryStorageSummaryModel(
        string StatusText,
        string StorableText,
        string StoredText,
        string TakeOutText,
        bool IsBusy);

    internal sealed record YouPinInventoryStorageActionModel(
        string ButtonText,
        string HintText,
        bool Enabled);

    internal sealed record YouPinInventoryStorageToolbarLayout(
        int Height,
        bool UsesTwoRows,
        Rectangle StoreTab,
        Rectangle TakeOutTab,
        Rectangle SearchInput,
        Rectangle SelectAll,
        Rectangle StorageUnit);

    internal sealed record YouPinInventoryStorageRetryModel(
        bool ShouldRetry,
        int DelayMs,
        string StatusText);

    internal sealed record YouPinInventoryStoragePageLayout(
        int BottomPadding,
        int InventoryCardHeight,
        int TotalContentHeight,
        bool RequiresScroll);

    internal static class YouPinInventoryStoragePageModel
    {
        public static YouPinInventoryStorageToolbarLayout BuildToolbarLayout(
            int availableWidth,
            float scaleFactor)
        {
            int width = Math.Max(1, availableWidth);
            float scale = scaleFactor > 0 ? scaleFactor : 1F;
            int S(int value) => Math.Max(1, (int)(value * scale));

            int controlHeight = S(34);
            int tabWidth = S(92);
            int tabGap = S(6);
            int gap = S(10);
            int firstRowTop = S(8);
            var storeTab = new Rectangle(0, firstRowTop, tabWidth, controlHeight);
            var takeOutTab = new Rectangle(storeTab.Right + tabGap, firstRowTop, tabWidth, controlHeight);

            if (width >= S(660))
            {
                int comboWidth = Math.Min(S(250), Math.Max(S(170), width / 4));
                var storageUnit = new Rectangle(width - comboWidth, firstRowTop, comboWidth, controlHeight);
                int selectWidth = S(132);
                var selectAll = new Rectangle(
                    storageUnit.Left - gap - selectWidth,
                    S(13),
                    selectWidth,
                    S(24));
                int searchLeft = takeOutTab.Right + gap;
                int searchRight = selectAll.Left - gap;
                var searchInput = new Rectangle(
                    searchLeft,
                    firstRowTop,
                    Math.Max(1, searchRight - searchLeft),
                    controlHeight);
                return new YouPinInventoryStorageToolbarLayout(
                    S(52),
                    false,
                    storeTab,
                    takeOutTab,
                    searchInput,
                    selectAll,
                    storageUnit);
            }

            int compactSearchLeft = takeOutTab.Right + gap;
            var compactSearch = new Rectangle(
                compactSearchLeft,
                firstRowTop,
                Math.Max(1, width - compactSearchLeft),
                controlHeight);
            int secondRowTop = S(50);
            int compactSelectWidth = Math.Min(S(132), Math.Max(1, width / 3));
            var compactSelect = new Rectangle(0, secondRowTop + S(5), compactSelectWidth, S(24));
            int compactComboLeft = compactSelect.Right + gap;
            var compactStorageUnit = new Rectangle(
                compactComboLeft,
                secondRowTop,
                Math.Max(1, width - compactComboLeft),
                controlHeight);
            return new YouPinInventoryStorageToolbarLayout(
                S(92),
                true,
                storeTab,
                takeOutTab,
                compactSearch,
                compactSelect,
                compactStorageUnit);
        }

        public static bool ShouldKeepWritePending(
            bool currentlyPending,
            bool refreshSucceeded,
            YouPinInventoryStorageViewState? refreshedState)
        {
            return currentlyPending
                && (!refreshSucceeded || refreshedState == null || refreshedState.Access.IsBusy);
        }

        public static YouPinInventoryStorageSummaryModel BuildSummary(
            YouPinInventoryStorageViewState? state)
        {
            if (state == null)
            {
                return new YouPinInventoryStorageSummaryModel(
                    "等待读取悠悠库存",
                    "—",
                    "—",
                    "—",
                    false);
            }

            YouPinInventoryStorageAccess access = state.Access;
            return new YouPinInventoryStorageSummaryModel(
                access.IsBusy ? "悠悠正在同步库存" : state.Message,
                $"{access.StorableCount} 件",
                $"{access.StoredCount} 件",
                $"{access.TakeOutCount} 件",
                access.IsBusy);
        }

        public static string BuildEmptyText(
            YouPinInventoryStorageDirection direction,
            bool isLoading,
            bool hasLoadedState,
            int sourceItemCount,
            int filteredItemCount,
            string? keyword,
            bool hasStorageUnit,
            bool isQueryPending = false,
            bool hasRefreshError = false)
        {
            if (isQueryPending)
            {
                return direction == YouPinInventoryStorageDirection.Store
                    ? "悠悠正在准备可存入饰品，完成后会自动刷新。"
                    : "悠悠正在准备已存入饰品，完成后会自动刷新。";
            }
            if (hasRefreshError)
                return "读取悠悠库存失败，请点击“刷新库存”重试。";
            if (isLoading)
                return "正在读取悠悠库存…";
            if (!hasLoadedState)
            {
                return direction == YouPinInventoryStorageDirection.Store
                    ? "完成悠悠登录并成功刷新后，这里会显示可存入饰品。"
                    : "完成悠悠登录并成功刷新后，这里会显示已存入饰品。";
            }
            if (sourceItemCount <= 0)
            {
                if (direction == YouPinInventoryStorageDirection.TakeOut && !hasStorageUnit)
                    return "请选择一个存储单元以查看其中的饰品。";
                return direction == YouPinInventoryStorageDirection.Store
                    ? "当前没有符合悠悠条件的可存入饰品。"
                    : "当前存储单元暂无可取出饰品。";
            }
            if (filteredItemCount <= 0 && !string.IsNullOrWhiteSpace(keyword))
                return "没有符合当前搜索条件的饰品。";

            return "暂无可显示的饰品。";
        }

        public static YouPinInventoryStorageViewState? ResolveStateAfterRefreshFailure(
            YouPinInventoryStorageViewState? previousState,
            YouPinInventoryStorageViewState? partialState)
        {
            if (previousState == null)
                return partialState;
            if (partialState == null)
                return previousState;

            return new YouPinInventoryStorageViewState(
                previousState.Query,
                partialState.Access,
                previousState.Items,
                partialState.Units.Count > 0 ? partialState.Units : previousState.Units,
                partialState.Message,
                partialState.RefreshedAt);
        }

        public static bool CanPreserveStateForRequest(
            YouPinInventoryStorageViewState? previousState,
            YouPinInventoryStorageDirection direction,
            string? storageAssetId)
        {
            if (previousState == null)
                return false;

            if (direction == YouPinInventoryStorageDirection.Store)
                return previousState.Query.View == YouPinInventoryStorageView.Storable;

            return previousState.Query.View == YouPinInventoryStorageView.StoredItems
                && string.Equals(
                    previousState.Query.StorageAssetId,
                    storageAssetId,
                    StringComparison.Ordinal);
        }

        public static YouPinInventoryStorageRetryModel BuildQueryPendingRetry(
            YouPinInventoryStorageQueryPendingException error,
            int nextAttempt,
            int maxAttempts)
        {
            ArgumentNullException.ThrowIfNull(error);
            int normalizedAttempt = Math.Max(1, nextAttempt);
            int normalizedMax = Math.Max(0, maxAttempts);
            int delayMs = Math.Clamp(
                (int)Math.Ceiling(error.RetryAfter.TotalMilliseconds),
                1000,
                30000);
            bool shouldRetry = normalizedAttempt <= normalizedMax;
            string status = shouldRetry
                ? $"悠悠正在准备库存数据，{delayMs / 1000D:0.#} 秒后自动重试（{normalizedAttempt}/{normalizedMax}）。"
                : "悠悠仍在准备库存数据，请稍后点击“刷新库存”重试。";
            return new YouPinInventoryStorageRetryModel(shouldRetry, delayMs, status);
        }

        public static YouPinInventoryStoragePageLayout BuildPageLayout(
            int viewportHeight,
            float scaleFactor)
        {
            int height = Math.Max(1, viewportHeight);
            float scale = scaleFactor > 0 ? scaleFactor : 1F;
            int S(int value) => Math.Max(1, (int)(value * scale));

            int topPadding = S(18);
            int bottomPadding = S(18);
            int summaryHeight = S(150);
            int cardGap = S(14);
            int minimumInventoryHeight = S(320);
            int fixedHeight = topPadding + bottomPadding + summaryHeight + cardGap * 2;
            int inventoryHeight = Math.Max(minimumInventoryHeight, height - fixedHeight);
            int totalHeight = fixedHeight + inventoryHeight;
            return new YouPinInventoryStoragePageLayout(
                bottomPadding,
                inventoryHeight,
                totalHeight,
                totalHeight > height);
        }

        public static YouPinInventoryStorageActionModel BuildAction(
            YouPinInventoryStorageDirection direction,
            YouPinInventoryStorageAccess access,
            int selectedCount,
            string storageUnitName,
            bool isLoading)
        {
            bool hasUnit = !string.IsNullOrWhiteSpace(storageUnitName);
            bool allowed = direction == YouPinInventoryStorageDirection.Store
                ? access.CanStore
                : access.CanTakeOut;
            string button = direction == YouPinInventoryStorageDirection.Store
                ? selectedCount > 0 ? $"存入 {selectedCount} 件" : "存入所选"
                : selectedCount > 0 ? $"取出 {selectedCount} 件" : "取出所选";

            string hint;
            if (isLoading)
                hint = "正在读取悠悠库存，请稍候…";
            else if (access.IsBusy)
                hint = "悠悠库存正在同步，完成前不能继续操作。";
            else if (!hasUnit)
                hint = direction == YouPinInventoryStorageDirection.Store
                    ? "请选择目标存储单元。"
                    : "请选择要查看的存储单元。";
            else if (!allowed)
                hint = direction == YouPinInventoryStorageDirection.Store
                    ? FirstText(access.StoreMessage, "当前没有可存入的饰品。")
                    : FirstText(access.TakeOutMessage, "当前没有可取出的饰品。");
            else if (selectedCount <= 0)
                hint = "勾选饰品后即可执行。";
            else
                hint = $"已选择 {selectedCount} 件，操作前会再次核对悠悠状态。";

            return new YouPinInventoryStorageActionModel(
                button,
                hint,
                !isLoading && !access.IsBusy && allowed && hasUnit && selectedCount > 0);
        }

        public static IReadOnlyList<YouPinInventoryStorageItem> FilterItems(
            IEnumerable<YouPinInventoryStorageItem> items,
            string? keyword)
        {
            string value = (keyword ?? string.Empty).Trim();
            if (value.Length == 0)
                return items.ToArray();

            return items
                .Where(item => Contains(item.Name, value)
                    || Contains(item.MarketHashName, value)
                    || Contains(item.ExteriorName, value))
                .ToArray();
        }

        public static string BuildConfirmation(
            YouPinInventoryStorageDirection direction,
            int selectedCount,
            string storageUnitName)
        {
            string action = direction == YouPinInventoryStorageDirection.Store ? "存入" : "取出";
            return $"确定将所选 {selectedCount} 件饰品{action}“{storageUnitName}”吗？\r\n\r\n提交前会重新核对资产状态；提交后请等待悠悠同步，不要重复操作。";
        }

        private static bool Contains(string? source, string value)
        {
            return (source ?? string.Empty).Contains(value, StringComparison.OrdinalIgnoreCase);
        }

        private static string FirstText(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return string.Empty;
        }
    }
}
