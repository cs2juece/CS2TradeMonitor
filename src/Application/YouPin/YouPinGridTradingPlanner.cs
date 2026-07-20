using CS2TradeMonitor.Domain.YouPin;

namespace CS2TradeMonitor.Application.YouPin
{
    public static class YouPinGridTradingPlanner
    {
        public static YouPinGridPlan Plan(
            YouPinGridStrategy strategy,
            YouPinGridEvaluationInput input)
        {
            ArgumentNullException.ThrowIfNull(strategy);
            ArgumentNullException.ThrowIfNull(input);

            decimal ratio = strategy.GridPercent / 100m;
            decimal nextBuyPrice = RoundMoney(strategy.BasePrice * (1m - ratio));
            decimal nextSellPrice = RoundMoney(strategy.BasePrice * (1m + ratio));

            if (string.IsNullOrWhiteSpace(strategy.ItemName)
                || strategy.BasePrice <= 0m
                || strategy.GridPercent <= 0m
                || strategy.GridPercent >= 100m
                || strategy.QuantityPerGrid <= 0
                || strategy.MaxHoldings < strategy.MinimumHoldings)
            {
                return NoAction(
                    input,
                    nextBuyPrice,
                    nextSellPrice,
                    YouPinGridDecisionCode.InvalidStrategy,
                    "网格参数不完整或超出允许范围");
            }

            if (!input.MarketFresh || input.ObservationPrice <= 0m)
            {
                return NoAction(
                    input,
                    nextBuyPrice,
                    nextSellPrice,
                    YouPinGridDecisionCode.MarketUnavailable,
                    "悠悠同款在售价不可用或已过期");
            }

            if (input.HasPendingOrder)
            {
                return NoAction(
                    input,
                    nextBuyPrice,
                    nextSellPrice,
                    YouPinGridDecisionCode.PendingOrder,
                    "同一标的已有未完成订单");
            }

            if ((strategy.MinimumPrice > 0m && input.ObservationPrice < strategy.MinimumPrice)
                || (strategy.MaximumPrice > 0m && input.ObservationPrice > strategy.MaximumPrice))
            {
                return NoAction(
                    input,
                    nextBuyPrice,
                    nextSellPrice,
                    YouPinGridDecisionCode.OutsidePriceRange,
                    "观察价已超出策略有效价格区间");
            }

            if (input.ObservationPrice <= nextBuyPrice)
            {
                int remainingHoldings = Math.Max(0, strategy.MaxHoldings - input.AvailableHoldings);
                if (remainingHoldings == 0)
                {
                    return NoAction(
                        input,
                        nextBuyPrice,
                        nextSellPrice,
                        YouPinGridDecisionCode.HoldingsLimit,
                        "已达到最大持有件数");
                }

                int crossed = strategy.CrossGridMultiplierEnabled
                    ? CountCrossedBuyLevels(strategy.BasePrice, ratio, input.ObservationPrice)
                    : 1;
                int requested = Math.Max(1, crossed) * strategy.QuantityPerGrid;
                int batchLimit = Math.Max(1, strategy.MaxBatchQuantity);
                int quantity = Math.Min(requested, Math.Min(batchLimit, remainingHoldings));
                if (strategy.MaxCapital > 0m)
                {
                    decimal remainingCapital = Math.Max(0m, strategy.MaxCapital - input.ReservedCapital);
                    int affordable = input.ObservationPrice > 0m
                        ? decimal.ToInt32(decimal.Floor(remainingCapital / input.ObservationPrice))
                        : 0;
                    quantity = Math.Min(quantity, affordable);
                }

                if (!strategy.ObserveOnly && quantity > 0)
                    quantity = 1;

                if (quantity <= 0)
                {
                    return NoAction(
                        input,
                        nextBuyPrice,
                        nextSellPrice,
                        YouPinGridDecisionCode.CapitalLimit,
                        "剩余资金不足以执行本档买入");
                }

                return new YouPinGridPlan
                {
                    Action = YouPinGridAction.Buy,
                    DecisionCode = YouPinGridDecisionCode.BuyTriggered,
                    Quantity = quantity,
                    ObservationPrice = input.ObservationPrice,
                    TriggerPrice = nextBuyPrice,
                    NextBuyPrice = nextBuyPrice,
                    NextSellPrice = nextSellPrice,
                    ExecutionPermitted = !strategy.ObserveOnly,
                    Message = "观察价已到达下一档买入价"
                };
            }

            if (input.ObservationPrice >= nextSellPrice)
            {
                int sellable = Math.Max(0, input.AvailableHoldings - strategy.MinimumHoldings);
                if (sellable == 0)
                {
                    return NoAction(
                        input,
                        nextBuyPrice,
                        nextSellPrice,
                        YouPinGridDecisionCode.HoldingsLimit,
                        "没有超过最低持有数量的可出售饰品");
                }

                int crossed = strategy.CrossGridMultiplierEnabled
                    ? CountCrossedSellLevels(strategy.BasePrice, ratio, input.ObservationPrice)
                    : 1;
                int requested = Math.Max(1, crossed) * strategy.QuantityPerGrid;
                int quantity = Math.Min(
                    requested,
                    Math.Min(Math.Max(1, strategy.MaxBatchQuantity), sellable));
                if (!strategy.ObserveOnly && quantity > 0)
                    quantity = 1;
                return new YouPinGridPlan
                {
                    Action = YouPinGridAction.Sell,
                    DecisionCode = YouPinGridDecisionCode.SellTriggered,
                    Quantity = quantity,
                    ObservationPrice = input.ObservationPrice,
                    TriggerPrice = nextSellPrice,
                    NextBuyPrice = nextBuyPrice,
                    NextSellPrice = nextSellPrice,
                    ExecutionPermitted = !strategy.ObserveOnly,
                    Message = "观察价已到达下一档卖出价"
                };
            }

            return NoAction(
                input,
                nextBuyPrice,
                nextSellPrice,
                YouPinGridDecisionCode.NoAction,
                "观察价尚未到达下一档网格");
        }

        private static YouPinGridPlan NoAction(
            YouPinGridEvaluationInput input,
            decimal nextBuyPrice,
            decimal nextSellPrice,
            YouPinGridDecisionCode decisionCode,
            string message)
        {
            return new YouPinGridPlan
            {
                Action = YouPinGridAction.None,
                DecisionCode = decisionCode,
                ObservationPrice = input.ObservationPrice,
                NextBuyPrice = nextBuyPrice,
                NextSellPrice = nextSellPrice,
                Message = message
            };
        }

        private static int CountCrossedBuyLevels(decimal basePrice, decimal ratio, decimal observationPrice)
        {
            decimal level = basePrice;
            int crossed = 0;
            for (int index = 0; index < 100; index++)
            {
                level *= 1m - ratio;
                if (observationPrice > level)
                    break;
                crossed++;
            }

            return crossed;
        }

        private static int CountCrossedSellLevels(decimal basePrice, decimal ratio, decimal observationPrice)
        {
            decimal level = basePrice;
            int crossed = 0;
            for (int index = 0; index < 100; index++)
            {
                level *= 1m + ratio;
                if (observationPrice < level)
                    break;
                crossed++;
            }

            return crossed;
        }

        private static decimal RoundMoney(decimal value)
        {
            return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        }
    }
}
