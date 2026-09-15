namespace CS2QuantWeb.Core;

/// <summary>
/// Single source of truth for indicator metadata shared by the API, chart and strategy editor.
/// Formula defaults follow KLineChart 10.0.0 where that library provides the indicator.
/// </summary>
public static class IndicatorCatalog
{
    private static readonly IndicatorPlacement[] MainAndSub =
        [IndicatorPlacement.Main, IndicatorPlacement.Sub];

    private static readonly IndicatorPlacement[] SubOnly = [IndicatorPlacement.Sub];

    private static readonly IReadOnlyList<IndicatorDefinition> Definitions =
    [
        Lines("MA", "MA（移动平均线）", MainAndSub, Periods(5, 10, 20, 30, 60)),
        Lines("EMA", "EMA（指数平滑移动平均线）", MainAndSub, Periods(6, 12, 20)),
        One("SMA", "SMA（简单移动平均线）", MainAndSub, [P("period", "周期", 12, 1, 500), P("weight", "权重", 2, 1, 500)], "sma", "SMA"),
        One("BOLL", "BOLL（布林线）", MainAndSub, [P("period", "周期", 20, 2, 500), P("deviation", "标准差", 2, 0.1, 20, 2)], [("upper", "UP", "line"), ("mid", "MID", "line"), ("lower", "DN", "line"), ("bandwidth", "带宽（%）", "none"), ("bandwidthChange", "带宽变化", "none")]),
        One("SAR", "SAR（停损点转向指标）", MainAndSub, [P("start", "起始步长", 2, 0.1, 20, 1), P("step", "递增步长", 2, 0.1, 20, 1), P("maximum", "最大步长", 20, 1, 100, 1)], "sar", "SAR"),
        One("BBI", "BBI（多空指数）", MainAndSub, Periods(3, 6, 12, 24), "bbi", "BBI"),
        One("ENE", "ENE（轨道线）", MainAndSub, [P("period", "周期", 10, 2, 500), P("upper", "上轨幅度", 11, 0.1, 100, 2), P("lower", "下轨幅度", 9, 0.1, 100, 2)], [("upper", "UPPER"), ("mid", "ENE"), ("lower", "LOWER")]),
        Lines("EXPMA", "EXPMA（指数平均线）", MainAndSub, Periods(12, 50)),

        VolumeLines("VOL", "VOL（成交量）", Periods(5, 10, 20)),
        new IndicatorDefinition("TUR", "TUR（成交额）", "真实成交额及其均线；数据源未提供成交额时禁用。", SubOnly, Periods(5, 10), [O("turnover", "TUR", "bar"), O("tur5", "MATUR5"), O("tur10", "MATUR10")], RequiresTurnover: true),
        One("MACD", "MACD（指数平滑异同移动平均线）", SubOnly, [P("fast", "快线", 12, 1, 500), P("slow", "慢线", 26, 2, 500), P("signal", "信号线", 9, 1, 500)], [("dif", "DIF", "line"), ("dea", "DEA", "line"), ("macd", "MACD", "bar")]),
        Lines("MA", "MA（移动平均线）", SubOnly, Periods(5, 10, 20, 30, 60), duplicateCode: true),
        Lines("EMA", "EMA（指数平滑移动平均线）", SubOnly, Periods(6, 12, 20), duplicateCode: true),
        One("BOLL", "BOLL（布林线）", SubOnly, [P("period", "周期", 20, 2, 500), P("deviation", "标准差", 2, 0.1, 20, 2)], [("upper", "UP", "line"), ("mid", "MID", "line"), ("lower", "DN", "line"), ("bandwidth", "带宽（%）", "none"), ("bandwidthChange", "带宽变化", "none")], duplicateCode: true),
        One("KDJ", "KDJ（随机指标）", SubOnly, [P("period", "周期", 9, 1, 500), P("k", "K 平滑", 3, 1, 100), P("d", "D 平滑", 3, 1, 100)], [("k", "K"), ("d", "D"), ("j", "J")]),
        Lines("RSI", "RSI（相对强弱指标）", SubOnly, Periods(6, 12, 24)),
        Lines("BIAS", "BIAS（乖离率）", SubOnly, Periods(6, 12, 24)),
        One("BRAR", "BRAR（情绪指标）", SubOnly, Periods(26), [("br", "BR"), ("ar", "AR")]),
        One("CCI", "CCI（顺势指标）", SubOnly, Periods(20), "cci", "CCI"),
        One("DMI", "DMI（动向指标）", SubOnly, [P("period", "周期", 14, 1, 500), P("adx", "ADX 周期", 6, 1, 500)], [("pdi", "PDI"), ("mdi", "MDI"), ("adx", "ADX"), ("adxr", "ADXR")]),
        One("CR", "CR（能量指标）", SubOnly, Periods(26, 10, 20, 40, 60), [("cr", "CR"), ("ma1", "MA1"), ("ma2", "MA2"), ("ma3", "MA3"), ("ma4", "MA4")]),
        One("PSY", "PSY（心理线）", SubOnly, Periods(12, 6), [("psy", "PSY"), ("psyma", "PSYMA")]),
        One("DMA", "DMA（平行线差指标）", SubOnly, Periods(10, 50, 10), [("dif", "DIF"), ("difma", "DIFMA")]),
        One("TRIX", "TRIX（三重指数平滑平均线）", SubOnly, Periods(12, 9), [("trix", "TRIX"), ("matrix", "MATRIX")]),
        One("OBV", "OBV（能量潮指标）", SubOnly, Periods(30), [("obv", "OBV"), ("maobv", "MAOBV")], requiresVolume: true),
        One("VR", "VR（成交量变异率）", SubOnly, Periods(26, 6), [("vr", "VR"), ("mavr", "MAVR")], requiresVolume: true),
        Lines("WR", "WR（威廉指标）", SubOnly, Periods(6, 10, 14)),
        One("MTM", "MTM（动量指标）", SubOnly, Periods(12, 6), [("mtm", "MTM"), ("mtmma", "MTMMA")]),
        One("EMV", "EMV（简易波动指标）", SubOnly, Periods(14, 9), [("emv", "EMV"), ("maemv", "MAEMV")], requiresVolume: true),
        One("SAR", "SAR（停损点转向指标）", SubOnly, [P("start", "起始步长", 2, 0.1, 20, 1), P("step", "递增步长", 2, 0.1, 20, 1), P("maximum", "最大步长", 20, 1, 100, 1)], "sar", "SAR", duplicateCode: true),
        One("SMA", "SMA（简单移动平均线）", SubOnly, [P("period", "周期", 12, 1, 500), P("weight", "权重", 2, 1, 500)], "sma", "SMA", duplicateCode: true),
        One("ROC", "ROC（变动率指标）", SubOnly, Periods(12, 6), [("roc", "ROC"), ("maroc", "MAROC")]),
        One("PVT", "PVT（价量趋势指标）", SubOnly, [], "pvt", "PVT", requiresVolume: true),
        One("BBI", "BBI（多空指数）", SubOnly, Periods(3, 6, 12, 24), "bbi", "BBI", duplicateCode: true),
        One("AO", "AO（动量震荡指标）", SubOnly, Periods(5, 34), "ao", "AO")
    ];

    public static IReadOnlyList<IndicatorDefinition> All { get; } = Definitions
        .GroupBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
        .Select(group => Merge(group))
        .ToArray();

    public static IReadOnlyList<IndicatorSelection> DefaultSelections { get; } =
    [
        new("ma-main", "MA", IndicatorPlacement.Main, [5, 10, 20]),
        new("macd-sub", "MACD", IndicatorPlacement.Sub, [12, 26, 9])
    ];

    public static IndicatorDefinition Get(string code) => All.FirstOrDefault(
        item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"不支持指标 {code}。", nameof(code));

    private static IndicatorDefinition Merge(IGrouping<string, IndicatorDefinition> group)
    {
        IndicatorDefinition first = group.First();
        return first with
        {
            Placements = group.SelectMany(item => item.Placements).Distinct().ToArray()
        };
    }

    private static IndicatorDefinition Lines(
        string code,
        string name,
        IReadOnlyList<IndicatorPlacement> placements,
        IReadOnlyList<IndicatorParameterDefinition> parameters,
        bool duplicateCode = false)
    {
        _ = duplicateCode;
        return new IndicatorDefinition(code, name, "支持自定义周期。", placements, parameters, parameters.Select((parameter, index) => O($"value{index + 1}", parameter.Label)).ToArray());
    }

    private static IndicatorDefinition VolumeLines(string code, string name, IReadOnlyList<IndicatorParameterDefinition> parameters)
        => new(code, name, "成交量及自定义成交量均线。", SubOnly, parameters, [O("volume", "VOL", "bar"), .. parameters.Select((parameter, index) => O($"value{index + 1}", $"MAVOL{parameter.DefaultValue:0}"))], RequiresVolume: true);

    private static IndicatorDefinition One(
        string code,
        string name,
        IReadOnlyList<IndicatorPlacement> placements,
        IReadOnlyList<IndicatorParameterDefinition> parameters,
        string output,
        string label,
        bool requiresVolume = false,
        bool duplicateCode = false)
    {
        _ = duplicateCode;
        return new IndicatorDefinition(code, name, "支持自定义参数。", placements, parameters, [O(output, label)], RequiresVolume: requiresVolume);
    }

    private static IndicatorDefinition One(
        string code,
        string name,
        IReadOnlyList<IndicatorPlacement> placements,
        IReadOnlyList<IndicatorParameterDefinition> parameters,
        IReadOnlyList<(string Key, string Label)> outputs,
        bool requiresVolume = false,
        bool duplicateCode = false)
        => One(code, name, placements, parameters, outputs.Select(output => (output.Key, output.Label, "line")).ToArray(), requiresVolume, duplicateCode);

    private static IndicatorDefinition One(
        string code,
        string name,
        IReadOnlyList<IndicatorPlacement> placements,
        IReadOnlyList<IndicatorParameterDefinition> parameters,
        IReadOnlyList<(string Key, string Label, string Figure)> outputs,
        bool requiresVolume = false,
        bool duplicateCode = false)
    {
        _ = duplicateCode;
        return new IndicatorDefinition(code, name, "支持自定义参数。", placements, parameters, outputs.Select(output => O(output.Key, output.Label, output.Figure)).ToArray(), RequiresVolume: requiresVolume);
    }

    private static IndicatorParameterDefinition[] Periods(params double[] defaults)
        => defaults.Select((value, index) => P($"period{index + 1}", $"参数{index + 1}", value, 1, 500)).ToArray();

    private static IndicatorParameterDefinition P(string key, string label, double value, double min, double max, int decimals = 0)
        => new(key, label, value, min, max, decimals);

    private static IndicatorOutputDefinition O(string key, string label, string figure = "line")
        => new(key, label, figure);
}
