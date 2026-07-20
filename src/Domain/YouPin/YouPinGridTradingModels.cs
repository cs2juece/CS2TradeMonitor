namespace CS2TradeMonitor.Domain.YouPin
{
    public enum YouPinGridAction
    {
        None,
        Buy,
        Sell
    }

    public enum YouPinGridDecisionCode
    {
        NoAction,
        BuyTriggered,
        SellTriggered,
        InvalidStrategy,
        MarketUnavailable,
        OutsidePriceRange,
        PendingOrder,
        HoldingsLimit,
        CapitalLimit
    }

    public enum YouPinGridExecutionStage
    {
        None,
        Prepared,
        AwaitingSettlement,
        Completed,
        Failed,
        RequiresManualReview
    }

    public sealed class YouPinGridStrategy
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string ItemName { get; set; } = "";
        public string TemplateId { get; set; } = "";
        public bool Enabled { get; set; }
        public bool ObserveOnly { get; set; } = true;
        public decimal BasePrice { get; set; }
        public decimal GridPercent { get; set; } = 5m;
        public int QuantityPerGrid { get; set; } = 1;
        public decimal MinimumPrice { get; set; }
        public decimal MaximumPrice { get; set; }
        public int MinimumHoldings { get; set; }
        public int MaxHoldings { get; set; } = 1;
        public decimal MaxCapital { get; set; }
        public bool CrossGridMultiplierEnabled { get; set; }
        public int MaxBatchQuantity { get; set; } = 3;
    }

    public sealed class YouPinGridEvaluationInput
    {
        public decimal ObservationPrice { get; set; }
        public int AvailableHoldings { get; set; }
        public decimal ReservedCapital { get; set; }
        public bool MarketFresh { get; set; }
        public bool HasPendingOrder { get; set; }
    }

    public sealed class YouPinGridPlan
    {
        public YouPinGridAction Action { get; init; }
        public YouPinGridDecisionCode DecisionCode { get; init; }
        public int Quantity { get; init; }
        public decimal ObservationPrice { get; init; }
        public decimal TriggerPrice { get; init; }
        public decimal NextBuyPrice { get; init; }
        public decimal NextSellPrice { get; init; }
        public bool ExecutionPermitted { get; init; }
        public string Message { get; init; } = "";
    }

    public sealed class YouPinGridMarketQuote
    {
        public bool Available { get; init; }
        public string TemplateId { get; init; } = "";
        public string ItemName { get; init; } = "";
        public string ListingId { get; init; } = "";
        public decimal LowestPrice { get; init; }
        public int ValidListingCount { get; init; }
        public DateTime CapturedAt { get; init; }
        public string Source { get; init; } = "悠悠同款在售";
        public string Message { get; init; } = "";
    }

    public sealed class YouPinGridExecutionRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string StrategyId { get; set; } = "";
        public string TemplateId { get; set; } = "";
        public string Fingerprint { get; set; } = "";
        public YouPinGridAction Action { get; set; }
        public YouPinGridExecutionStage Stage { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TriggerPrice { get; set; }
        public string TargetReference { get; set; } = "";
        public string RemoteReference { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class YouPinGridExecutionJournalState
    {
        public int SchemaVersion { get; set; } = 1;
        public List<YouPinGridExecutionRecord> Records { get; set; } = new();
    }

    public sealed class YouPinGridExecutionOutcome
    {
        public YouPinGridExecutionStage Stage { get; init; }
        public YouPinGridAction Action { get; init; }
        public int Quantity { get; init; }
        public decimal UnitPrice { get; init; }
        public decimal? CompletedBasePrice { get; init; }
        public string Message { get; init; } = "";
    }

    public sealed class YouPinGridState
    {
        public int SchemaVersion { get; set; } = 1;
        public List<YouPinGridStrategy> Strategies { get; set; } = new();
    }

    public sealed class YouPinGridStrategySnapshot
    {
        public YouPinGridStrategy Strategy { get; init; } = new();
        public YouPinGridMarketQuote MarketQuote { get; init; } = new();
        public YouPinGridPlan Plan { get; init; } = new();
        public YouPinGridExecutionOutcome Execution { get; init; } = new();
        public int Holdings { get; init; }
        public string Status { get; init; } = "";
    }

    public sealed class YouPinGridRuntimeSnapshot
    {
        public IReadOnlyList<YouPinGridStrategySnapshot> Strategies { get; init; } = Array.Empty<YouPinGridStrategySnapshot>();
        public DateTime LastRefreshAt { get; init; }
        public int EnabledCount { get; init; }
        public int TriggeredCount { get; init; }
        public int UnavailableCount { get; init; }
        public string Status { get; init; } = "尚未刷新";
    }

    public sealed class YouPinGridMutationResult
    {
        public bool Succeeded { get; init; }
        public string Message { get; init; } = "";

        public static YouPinGridMutationResult Success(string message) => new() { Succeeded = true, Message = message };
        public static YouPinGridMutationResult Failure(string message) => new() { Message = message };
    }
}
