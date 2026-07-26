namespace CS2QuantWeb.Core;

/// <summary>
/// Central indicator engine. The chart and strategy evaluator consume these values instead of
/// maintaining separate formula implementations. KLineChart 10.0.0 is the compatibility target.
/// </summary>
internal static class TechnicalIndicators
{
    public static IReadOnlyList<IndicatorPoint> Calculate(IReadOnlyList<QuantCandle> candles)
    {
        IReadOnlyList<IndicatorSeries> series = CalculateSeries(candles, IndicatorCatalog.DefaultSelections);
        IndicatorSeries ma = series.Single(item => item.Id == "ma-main");
        IndicatorSeries macd = series.Single(item => item.Id == "macd-sub");
        return candles.Select((candle, index) => new IndicatorPoint(
            candle.Date,
            candle.Close,
            Value(ma, index, "value1"),
            Value(ma, index, "value2"),
            Value(ma, index, "value3"),
            Average(candles.Select(item => item.Volume).ToArray(), index, 20),
            Value(macd, index, "dif") ?? 0,
            Value(macd, index, "dea") ?? 0,
            Value(macd, index, "macd") ?? 0)).ToArray();
    }

    public static IReadOnlyList<IndicatorSeries> CalculateSeries(
        IReadOnlyList<QuantCandle> candles,
        IReadOnlyList<IndicatorSelection>? selections)
    {
        ArgumentNullException.ThrowIfNull(candles);
        IReadOnlyList<IndicatorSelection> active = selections is { Count: > 0 }
            ? selections
            : IndicatorCatalog.DefaultSelections;
        if (active.Count > 12)
            throw new ArgumentException("图表最多同时启用 12 个指标。", nameof(selections));

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return active.Select(selection =>
        {
            if (string.IsNullOrWhiteSpace(selection.Id) || !ids.Add(selection.Id))
                throw new ArgumentException("指标实例 ID 不能为空或重复。", nameof(selections));

            IndicatorDefinition definition = IndicatorCatalog.Get(selection.Code);
            if (!definition.Placements.Contains(selection.Placement))
                throw new ArgumentException($"{definition.Code} 不支持 {selection.Placement} 位置。", nameof(selections));

            double[] parameters = NormalizeParameters(definition, selection.Parameters);
            bool available = (!definition.RequiresVolume || candles.Any(item => item.Volume > 0))
                && (!definition.RequiresTurnover || candles.Any(item => item.Turnover is > 0));
            string status = available
                ? "已计算"
                : definition.RequiresTurnover
                    ? "当前数据源未提供真实成交额，未使用价格×成交量伪造。"
                    : "当前数据源未提供有效成交量。";
            IReadOnlyList<Dictionary<string, double?>> values = available
                ? CalculateValues(definition.Code, candles, parameters)
                : candles.Select(_ => definition.Outputs.ToDictionary(output => output.Key, _ => (double?)null, StringComparer.OrdinalIgnoreCase)).ToArray();
            IReadOnlyList<IndicatorOutputDefinition> outputs = ResolveOutputs(definition, parameters.Length, parameters);
            return new IndicatorSeries(
                selection.Id,
                definition.Code,
                definition.Name,
                selection.Placement,
                parameters,
                outputs,
                candles.Select((candle, index) => new IndicatorValuePoint(candle.Date, values[index])).ToArray(),
                available,
                status);
        }).ToArray();
    }

    private static IReadOnlyList<Dictionary<string, double?>> CalculateValues(
        string code,
        IReadOnlyList<QuantCandle> candles,
        double[] parameters)
    {
        double[] close = candles.Select(item => item.Close).ToArray();
        double[] high = candles.Select(item => item.High).ToArray();
        double[] low = candles.Select(item => item.Low).ToArray();
        double[] open = candles.Select(item => item.Open).ToArray();
        double[] volume = candles.Select(item => item.Volume).ToArray();
        return code.ToUpperInvariant() switch
        {
            "MA" => MovingAverageLines(candles, close, parameters),
            "EMA" or "EXPMA" => ExponentialAverageLines(candles, close, parameters),
            "SMA" => SimpleSmoothedAverage(candles, close, parameters),
            "BOLL" => Boll(candles, close, parameters),
            "SAR" => Sar(candles, parameters),
            "BBI" => Bbi(candles, close, parameters),
            "ENE" => Ene(candles, close, parameters),
            "VOL" => VolumeLines(candles, volume, parameters, "volume"),
            "TUR" => VolumeLines(candles, candles.Select(item => item.Turnover ?? 0).ToArray(), parameters, "turnover"),
            "MACD" => Macd(candles, close, parameters),
            "KDJ" => Kdj(candles, high, low, close, parameters),
            "RSI" => Rsi(candles, close, parameters),
            "BIAS" => Bias(candles, close, parameters),
            "BRAR" => Brar(candles, open, high, low, close, parameters),
            "CCI" => Cci(candles, high, low, close, parameters),
            "DMI" => Dmi(candles, high, low, close, parameters),
            "CR" => Cr(candles, high, low, close, parameters),
            "PSY" => Psy(candles, close, parameters),
            "DMA" => Dma(candles, close, parameters),
            "TRIX" => Trix(candles, close, parameters),
            "OBV" => Obv(candles, close, volume, parameters),
            "VR" => Vr(candles, close, volume, parameters),
            "WR" => Wr(candles, high, low, close, parameters),
            "MTM" => Mtm(candles, close, parameters),
            "EMV" => Emv(candles, high, low, volume, parameters),
            "ROC" => Roc(candles, close, parameters),
            "PVT" => Pvt(candles, close, volume),
            "AO" => Ao(candles, high, low, parameters),
            _ => throw new ArgumentException($"尚未实现指标 {code}。", nameof(code))
        };
    }

    private static IReadOnlyList<Dictionary<string, double?>> MovingAverageLines(
        IReadOnlyList<QuantCandle> candles,
        double[] values,
        double[] parameters)
        => Rows(candles.Count, parameters.Select((period, index) => ($"value{index + 1}", MovingAverage(values, Period(period)))).ToArray());

    private static IReadOnlyList<Dictionary<string, double?>> ExponentialAverageLines(
        IReadOnlyList<QuantCandle> candles,
        double[] values,
        double[] parameters)
        => Rows(candles.Count, parameters.Select((period, index) => ($"value{index + 1}", Ema(values, Period(period)))).ToArray());

    private static IReadOnlyList<Dictionary<string, double?>> SimpleSmoothedAverage(
        IReadOnlyList<QuantCandle> candles,
        double[] values,
        double[] parameters)
    {
        int period = Period(parameters[0]);
        double weight = parameters[1];
        var result = new double?[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            if (i + 1 < period)
                continue;
            result[i] = i + 1 == period
                ? values.Take(period).Average()
                : (values[i] * weight + result[i - 1]!.Value * (period - weight + 1)) / (period + 1);
        }
        return Rows(candles.Count, [("sma", result)]);
    }

    private static IReadOnlyList<Dictionary<string, double?>> Boll(
        IReadOnlyList<QuantCandle> candles,
        double[] close,
        double[] parameters)
    {
        int period = Period(parameters[0]);
        double multiplier = parameters[1];
        double?[] mid = MovingAverage(close, period);
        var upper = new double?[close.Length];
        var lower = new double?[close.Length];
        for (int i = period - 1; i < close.Length; i++)
        {
            double average = mid[i]!.Value;
            double deviation = Math.Sqrt(close.Skip(i - period + 1).Take(period).Sum(value => Math.Pow(value - average, 2)) / period);
            upper[i] = average + multiplier * deviation;
            lower[i] = average - multiplier * deviation;
        }
        return Rows(candles.Count, [("upper", upper), ("mid", mid), ("lower", lower)]);
    }

    private static IReadOnlyList<Dictionary<string, double?>> Sar(IReadOnlyList<QuantCandle> candles, double[] parameters)
    {
        var values = new double?[candles.Count];
        if (candles.Count == 0)
            return Rows(0, [("sar", values)]);
        double acceleration = parameters[0] / 100;
        double step = parameters[1] / 100;
        double maximum = parameters[2] / 100;
        bool rising = true;
        double extreme = candles[0].High;
        double sar = candles[0].Low;
        values[0] = sar;
        for (int i = 1; i < candles.Count; i++)
        {
            sar += acceleration * (extreme - sar);
            if (rising)
            {
                sar = Math.Min(sar, candles[i - 1].Low);
                if (i > 1) sar = Math.Min(sar, candles[i - 2].Low);
                if (candles[i].Low < sar)
                {
                    rising = false;
                    sar = extreme;
                    extreme = candles[i].Low;
                    acceleration = parameters[0] / 100;
                }
                else if (candles[i].High > extreme)
                {
                    extreme = candles[i].High;
                    acceleration = Math.Min(maximum, acceleration + step);
                }
            }
            else
            {
                sar = Math.Max(sar, candles[i - 1].High);
                if (i > 1) sar = Math.Max(sar, candles[i - 2].High);
                if (candles[i].High > sar)
                {
                    rising = true;
                    sar = extreme;
                    extreme = candles[i].High;
                    acceleration = parameters[0] / 100;
                }
                else if (candles[i].Low < extreme)
                {
                    extreme = candles[i].Low;
                    acceleration = Math.Min(maximum, acceleration + step);
                }
            }
            values[i] = sar;
        }
        return Rows(candles.Count, [("sar", values)]);
    }

    private static IReadOnlyList<Dictionary<string, double?>> Bbi(IReadOnlyList<QuantCandle> candles, double[] close, double[] parameters)
    {
        double?[][] lines = parameters.Select(period => MovingAverage(close, Period(period))).ToArray();
        var bbi = new double?[close.Length];
        for (int i = 0; i < close.Length; i++)
            if (lines.All(line => line[i].HasValue)) bbi[i] = lines.Average(line => line[i]!.Value);
        return Rows(candles.Count, [("bbi", bbi)]);
    }

    private static IReadOnlyList<Dictionary<string, double?>> Ene(IReadOnlyList<QuantCandle> candles, double[] close, double[] parameters)
    {
        double?[] midBase = MovingAverage(close, Period(parameters[0]));
        var upper = new double?[close.Length];
        var lower = new double?[close.Length];
        var mid = new double?[close.Length];
        for (int i = 0; i < close.Length; i++)
        {
            if (!midBase[i].HasValue) continue;
            upper[i] = midBase[i] * (1 + parameters[1] / 100);
            lower[i] = midBase[i] * (1 - parameters[2] / 100);
            mid[i] = (upper[i] + lower[i]) / 2;
        }
        return Rows(candles.Count, [("upper", upper), ("mid", mid), ("lower", lower)]);
    }

    private static IReadOnlyList<Dictionary<string, double?>> VolumeLines(IReadOnlyList<QuantCandle> candles, double[] source, double[] parameters, string rawKey)
    {
        var series = new List<(string, double?[])> { (rawKey, source.Select(value => (double?)value).ToArray()) };
        series.AddRange(parameters.Select((period, index) => ($"value{index + 1}", MovingAverage(source, Period(period)))));
        if (rawKey == "turnover")
            series = [(rawKey, series[0].Item2), .. series.Skip(1).Select((item, index) => ($"tur{Period(parameters[index])}", item.Item2))];
        return Rows(candles.Count, series.ToArray());
    }

    private static IReadOnlyList<Dictionary<string, double?>> Macd(IReadOnlyList<QuantCandle> candles, double[] close, double[] parameters)
    {
        double?[] fast = Ema(close, Period(parameters[0]));
        double?[] slow = Ema(close, Period(parameters[1]));
        var dif = new double?[close.Length];
        for (int i = 0; i < close.Length; i++) if (fast[i].HasValue && slow[i].HasValue) dif[i] = fast[i] - slow[i];
        double?[] dea = EmaNullable(dif, Period(parameters[2]));
        var histogram = new double?[close.Length];
        for (int i = 0; i < close.Length; i++) if (dif[i].HasValue && dea[i].HasValue) histogram[i] = (dif[i] - dea[i]) * 2;
        return Rows(candles.Count, [("dif", dif), ("dea", dea), ("macd", histogram)]);
    }

    private static IReadOnlyList<Dictionary<string, double?>> Kdj(IReadOnlyList<QuantCandle> candles, double[] high, double[] low, double[] close, double[] parameters)
    {
        int period = Period(parameters[0]);
        double kSmooth = parameters[1];
        double dSmooth = parameters[2];
        var k = new double?[close.Length];
        var d = new double?[close.Length];
        var j = new double?[close.Length];
        double previousK = 50;
        double previousD = 50;
        for (int i = 0; i < close.Length; i++)
        {
            int start = Math.Max(0, i - period + 1);
            double highest = high.Skip(start).Take(i - start + 1).Max();
            double lowest = low.Skip(start).Take(i - start + 1).Min();
            double rsv = highest == lowest ? 50 : (close[i] - lowest) / (highest - lowest) * 100;
            previousK = ((kSmooth - 1) * previousK + rsv) / kSmooth;
            previousD = ((dSmooth - 1) * previousD + previousK) / dSmooth;
            k[i] = previousK;
            d[i] = previousD;
            j[i] = 3 * previousK - 2 * previousD;
        }
        return Rows(candles.Count, [("k", k), ("d", d), ("j", j)]);
    }

    private static IReadOnlyList<Dictionary<string, double?>> Rsi(IReadOnlyList<QuantCandle> candles, double[] close, double[] parameters)
    {
        var changes = new double[close.Length];
        for (int i = 1; i < close.Length; i++) changes[i] = close[i] - close[i - 1];
        return Rows(candles.Count, parameters.Select((value, line) =>
        {
            int period = Period(value);
            var result = new double?[close.Length];
            for (int i = period; i < close.Length; i++)
            {
                double up = changes.Skip(i - period + 1).Take(period).Sum(item => Math.Max(item, 0));
                double total = changes.Skip(i - period + 1).Take(period).Sum(Math.Abs);
                result[i] = total == 0 ? 0 : up / total * 100;
            }
            return ($"value{line + 1}", result);
        }).ToArray());
    }

    private static IReadOnlyList<Dictionary<string, double?>> Bias(IReadOnlyList<QuantCandle> candles, double[] close, double[] parameters)
        => Rows(candles.Count, parameters.Select((value, line) =>
        {
            double?[] ma = MovingAverage(close, Period(value));
            return ($"value{line + 1}", ma.Select((average, index) => average is null or 0 ? null : (double?)((close[index] - average.Value) / average.Value * 100)).ToArray());
        }).ToArray());

    private static IReadOnlyList<Dictionary<string, double?>> Brar(IReadOnlyList<QuantCandle> candles, double[] open, double[] high, double[] low, double[] close, double[] parameters)
    {
        int period = Period(parameters[0]);
        var ar = new double?[close.Length];
        var br = new double?[close.Length];
        for (int i = period - 1; i < close.Length; i++)
        {
            int start = i - period + 1;
            double arUp = 0, arDown = 0, brUp = 0, brDown = 0;
            for (int n = start; n <= i; n++)
            {
                arUp += high[n] - open[n];
                arDown += open[n] - low[n];
                double previous = n == 0 ? close[n] : close[n - 1];
                brUp += Math.Max(0, high[n] - previous);
                brDown += Math.Max(0, previous - low[n]);
            }
            ar[i] = arDown == 0 ? null : arUp / arDown * 100;
            br[i] = brDown == 0 ? null : brUp / brDown * 100;
        }
        return Rows(candles.Count, [("br", br), ("ar", ar)]);
    }

    private static IReadOnlyList<Dictionary<string, double?>> Cci(IReadOnlyList<QuantCandle> candles, double[] high, double[] low, double[] close, double[] parameters)
    {
        int period = Period(parameters[0]);
        double[] typical = close.Select((value, i) => (high[i] + low[i] + value) / 3).ToArray();
        var cci = new double?[close.Length];
        for (int i = period - 1; i < close.Length; i++)
        {
            double average = typical.Skip(i - period + 1).Take(period).Average();
            double deviation = typical.Skip(i - period + 1).Take(period).Average(value => Math.Abs(value - average));
            cci[i] = deviation == 0 ? 0 : (typical[i] - average) / (0.015 * deviation);
        }
        return Rows(candles.Count, [("cci", cci)]);
    }

    private static IReadOnlyList<Dictionary<string, double?>> Dmi(IReadOnlyList<QuantCandle> candles, double[] high, double[] low, double[] close, double[] parameters)
    {
        int period = Period(parameters[0]);
        int adxPeriod = Period(parameters[1]);
        var tr = new double[close.Length];
        var plus = new double[close.Length];
        var minus = new double[close.Length];
        for (int i = 1; i < close.Length; i++)
        {
            tr[i] = Math.Max(high[i] - low[i], Math.Max(Math.Abs(high[i] - close[i - 1]), Math.Abs(low[i] - close[i - 1])));
            double up = high[i] - high[i - 1];
            double down = low[i - 1] - low[i];
            plus[i] = up > down && up > 0 ? up : 0;
            minus[i] = down > up && down > 0 ? down : 0;
        }
        var pdi = new double?[close.Length];
        var mdi = new double?[close.Length];
        var dx = new double?[close.Length];
        for (int i = period; i < close.Length; i++)
        {
            double trSum = tr.Skip(i - period + 1).Take(period).Sum();
            if (trSum == 0) continue;
            pdi[i] = plus.Skip(i - period + 1).Take(period).Sum() / trSum * 100;
            mdi[i] = minus.Skip(i - period + 1).Take(period).Sum() / trSum * 100;
            double sum = pdi[i]!.Value + mdi[i]!.Value;
            dx[i] = sum == 0 ? 0 : Math.Abs(pdi[i]!.Value - mdi[i]!.Value) / sum * 100;
        }
        double?[] adx = MovingAverageNullable(dx, adxPeriod);
        var adxr = new double?[close.Length];
        for (int i = adxPeriod; i < close.Length; i++) if (adx[i].HasValue && adx[i - adxPeriod].HasValue) adxr[i] = (adx[i] + adx[i - adxPeriod]) / 2;
        return Rows(candles.Count, [("pdi", pdi), ("mdi", mdi), ("adx", adx), ("adxr", adxr)]);
    }

    private static IReadOnlyList<Dictionary<string, double?>> Cr(IReadOnlyList<QuantCandle> candles, double[] high, double[] low, double[] close, double[] parameters)
    {
        int period = Period(parameters[0]);
        var up = new double[close.Length];
        var down = new double[close.Length];
        for (int i = 1; i < close.Length; i++)
        {
            double previousMid = (high[i - 1] + low[i - 1] + close[i - 1]) / 3;
            up[i] = Math.Max(0, high[i] - previousMid);
            down[i] = Math.Max(0, previousMid - low[i]);
        }
        var cr = new double?[close.Length];
        for (int i = period; i < close.Length; i++)
        {
            double denominator = down.Skip(i - period + 1).Take(period).Sum();
            cr[i] = denominator == 0 ? 0 : up.Skip(i - period + 1).Take(period).Sum() / denominator * 100;
        }
        var lines = new List<(string, double?[])> { ("cr", cr) };
        for (int n = 1; n < parameters.Length; n++) lines.Add(($"ma{n}", MovingAverageNullable(cr, Period(parameters[n]))));
        return Rows(candles.Count, lines.ToArray());
    }

    private static IReadOnlyList<Dictionary<string, double?>> Psy(IReadOnlyList<QuantCandle> candles, double[] close, double[] parameters)
    {
        int period = Period(parameters[0]);
        var psy = new double?[close.Length];
        for (int i = period; i < close.Length; i++)
        {
            int up = 0;
            for (int n = i - period + 1; n <= i; n++) if (close[n] > close[n - 1]) up++;
            psy[i] = up * 100d / period;
        }
        return Rows(candles.Count, [("psy", psy), ("psyma", MovingAverageNullable(psy, Period(parameters[1])))]);
    }

    private static IReadOnlyList<Dictionary<string, double?>> Dma(IReadOnlyList<QuantCandle> candles, double[] close, double[] parameters)
    {
        double?[] shortMa = MovingAverage(close, Period(parameters[0]));
        double?[] longMa = MovingAverage(close, Period(parameters[1]));
        var dif = new double?[close.Length];
        for (int i = 0; i < close.Length; i++) if (shortMa[i].HasValue && longMa[i].HasValue) dif[i] = shortMa[i] - longMa[i];
        return Rows(candles.Count, [("dif", dif), ("difma", MovingAverageNullable(dif, Period(parameters[2])))]);
    }

    private static IReadOnlyList<Dictionary<string, double?>> Trix(IReadOnlyList<QuantCandle> candles, double[] close, double[] parameters)
    {
        int period = Period(parameters[0]);
        double?[] first = Ema(close, period);
        double?[] second = EmaNullable(first, period);
        double?[] third = EmaNullable(second, period);
        var trix = new double?[close.Length];
        for (int i = 1; i < close.Length; i++) if (third[i].HasValue && third[i - 1] is not null and not 0) trix[i] = (third[i] / third[i - 1] - 1) * 100;
        return Rows(candles.Count, [("trix", trix), ("matrix", MovingAverageNullable(trix, Period(parameters[1])))]);
    }

    private static IReadOnlyList<Dictionary<string, double?>> Obv(IReadOnlyList<QuantCandle> candles, double[] close, double[] volume, double[] parameters)
    {
        var obv = new double?[close.Length];
        double current = 0;
        for (int i = 0; i < close.Length; i++)
        {
            if (i > 0) current += close[i] > close[i - 1] ? volume[i] : close[i] < close[i - 1] ? -volume[i] : 0;
            obv[i] = current;
        }
        return Rows(candles.Count, [("obv", obv), ("maobv", MovingAverageNullable(obv, Period(parameters[0])))]);
    }

    private static IReadOnlyList<Dictionary<string, double?>> Vr(IReadOnlyList<QuantCandle> candles, double[] close, double[] volume, double[] parameters)
    {
        int period = Period(parameters[0]);
        var vr = new double?[close.Length];
        for (int i = period; i < close.Length; i++)
        {
            double up = 0, down = 0, flat = 0;
            for (int n = i - period + 1; n <= i; n++)
            {
                if (close[n] > close[n - 1]) up += volume[n];
                else if (close[n] < close[n - 1]) down += volume[n];
                else flat += volume[n];
            }
            double denominator = down + flat / 2;
            vr[i] = denominator == 0 ? 0 : (up + flat / 2) / denominator * 100;
        }
        return Rows(candles.Count, [("vr", vr), ("mavr", MovingAverageNullable(vr, Period(parameters[1])))]);
    }

    private static IReadOnlyList<Dictionary<string, double?>> Wr(IReadOnlyList<QuantCandle> candles, double[] high, double[] low, double[] close, double[] parameters)
        => Rows(candles.Count, parameters.Select((value, line) =>
        {
            int period = Period(value);
            var wr = new double?[close.Length];
            for (int i = period - 1; i < close.Length; i++)
            {
                double highest = high.Skip(i - period + 1).Take(period).Max();
                double lowest = low.Skip(i - period + 1).Take(period).Min();
                wr[i] = highest == lowest ? 0 : (close[i] - highest) / (highest - lowest) * 100;
            }
            return ($"value{line + 1}", wr);
        }).ToArray());

    private static IReadOnlyList<Dictionary<string, double?>> Mtm(IReadOnlyList<QuantCandle> candles, double[] close, double[] parameters)
    {
        int period = Period(parameters[0]);
        var mtm = new double?[close.Length];
        for (int i = period; i < close.Length; i++) mtm[i] = close[i] - close[i - period];
        return Rows(candles.Count, [("mtm", mtm), ("mtmma", MovingAverageNullable(mtm, Period(parameters[1])))]);
    }

    private static IReadOnlyList<Dictionary<string, double?>> Emv(IReadOnlyList<QuantCandle> candles, double[] high, double[] low, double[] volume, double[] parameters)
    {
        int period = Period(parameters[0]);
        var raw = new double?[high.Length];
        for (int i = 1; i < high.Length; i++)
        {
            double midpointMove = (high[i] + low[i] - high[i - 1] - low[i - 1]) / 2;
            double range = high[i] - low[i];
            raw[i] = volume[i] == 0 ? 0 : midpointMove * range / (volume[i] / 100_000_000d);
        }
        double?[] emv = MovingAverageNullable(raw, period);
        return Rows(candles.Count, [("emv", emv), ("maemv", MovingAverageNullable(emv, Period(parameters[1])))]);
    }

    private static IReadOnlyList<Dictionary<string, double?>> Roc(IReadOnlyList<QuantCandle> candles, double[] close, double[] parameters)
    {
        int period = Period(parameters[0]);
        var roc = new double?[close.Length];
        for (int i = period; i < close.Length; i++) roc[i] = close[i - period] == 0 ? null : (close[i] / close[i - period] - 1) * 100;
        return Rows(candles.Count, [("roc", roc), ("maroc", MovingAverageNullable(roc, Period(parameters[1])))]);
    }

    private static IReadOnlyList<Dictionary<string, double?>> Pvt(IReadOnlyList<QuantCandle> candles, double[] close, double[] volume)
    {
        var pvt = new double?[close.Length];
        double current = 0;
        for (int i = 0; i < close.Length; i++)
        {
            if (i > 0 && close[i - 1] != 0) current += volume[i] * (close[i] - close[i - 1]) / close[i - 1];
            pvt[i] = current;
        }
        return Rows(candles.Count, [("pvt", pvt)]);
    }

    private static IReadOnlyList<Dictionary<string, double?>> Ao(IReadOnlyList<QuantCandle> candles, double[] high, double[] low, double[] parameters)
    {
        double[] midpoint = high.Select((value, index) => (value + low[index]) / 2).ToArray();
        double?[] shortMa = MovingAverage(midpoint, Period(parameters[0]));
        double?[] longMa = MovingAverage(midpoint, Period(parameters[1]));
        var ao = new double?[high.Length];
        for (int i = 0; i < high.Length; i++) if (shortMa[i].HasValue && longMa[i].HasValue) ao[i] = shortMa[i] - longMa[i];
        return Rows(candles.Count, [("ao", ao)]);
    }

    private static double[] NormalizeParameters(IndicatorDefinition definition, IReadOnlyList<double>? requested)
    {
        double[] values = requested is { Count: > 0 }
            ? requested.ToArray()
            : definition.Parameters.Select(parameter => parameter.DefaultValue).ToArray();
        bool variableLength = definition.Code is "MA" or "EMA" or "EXPMA" or "RSI" or "BIAS" or "WR" or "VOL" or "TUR";
        if ((!variableLength && values.Length != definition.Parameters.Count)
            || (variableLength && (values.Length < 1 || values.Length > definition.Parameters.Count)))
            throw new ArgumentException($"{definition.Code} 需要 {definition.Parameters.Count} 个参数。", nameof(requested));
        for (int i = 0; i < values.Length; i++)
        {
            IndicatorParameterDefinition spec = definition.Parameters[i];
            if (!double.IsFinite(values[i]) || values[i] < spec.Minimum || values[i] > spec.Maximum)
                throw new ArgumentOutOfRangeException(nameof(requested), $"{definition.Code} 的{spec.Label}必须在 {spec.Minimum} 至 {spec.Maximum} 之间。");
        }
        if (definition.Code.Equals("MACD", StringComparison.OrdinalIgnoreCase) && values[0] >= values[1])
            throw new ArgumentException("MACD 快线周期必须小于慢线周期。", nameof(requested));
        return values;
    }

    private static IReadOnlyList<IndicatorOutputDefinition> ResolveOutputs(
        IndicatorDefinition definition,
        int parameterCount,
        IReadOnlyList<double>? parameters = null)
    {
        if (parameters is not null && definition.Code is "VOL" or "TUR")
        {
            string valueLabel = definition.Code == "VOL" ? "VOL" : "TUR";
            string averagePrefix = definition.Code == "VOL" ? "MAVOL" : "MATUR";
            return
            [
                definition.Outputs[0] with { Label = valueLabel },
                .. definition.Outputs.Skip(1).Take(parameterCount).Select((output, index) =>
                    output with { Label = $"{averagePrefix}{FormatParameter(parameters[index])}" })
            ];
        }
        if (parameters is not null && definition.Code is "MA" or "EMA" or "EXPMA" or "RSI" or "BIAS" or "WR")
        {
            return definition.Outputs.Take(parameterCount).Select((output, index) =>
                output with { Label = $"{definition.Code}{FormatParameter(parameters[index])}" }).ToArray();
        }
        return definition.Outputs;
    }

    private static string FormatParameter(double value)
        => value.ToString(value % 1 == 0 ? "0" : "0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static IReadOnlyList<Dictionary<string, double?>> Rows(int count, params (string Key, double?[] Values)[] series)
    {
        var rows = new Dictionary<string, double?>[count];
        for (int i = 0; i < count; i++)
        {
            rows[i] = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
            foreach ((string key, double?[] values) in series) rows[i][key] = values[i];
        }
        return rows;
    }

    private static double?[] MovingAverage(double[] values, int period)
    {
        var result = new double?[values.Length];
        double sum = 0;
        for (int i = 0; i < values.Length; i++)
        {
            sum += values[i];
            if (i >= period) sum -= values[i - period];
            if (i + 1 >= period) result[i] = sum / period;
        }
        return result;
    }

    private static double?[] MovingAverageNullable(double?[] values, int period)
    {
        var result = new double?[values.Length];
        var window = new Queue<double>();
        double sum = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (!values[i].HasValue)
            {
                window.Clear();
                sum = 0;
                continue;
            }
            window.Enqueue(values[i]!.Value);
            sum += values[i]!.Value;
            if (window.Count > period) sum -= window.Dequeue();
            if (window.Count == period) result[i] = sum / period;
        }
        return result;
    }

    private static double?[] Ema(double[] values, int period)
        => EmaNullable(values.Select(value => (double?)value).ToArray(), period);

    private static double?[] EmaNullable(double?[] values, int period)
    {
        var result = new double?[values.Length];
        double? previous = null;
        double alpha = 2d / (period + 1d);
        for (int i = 0; i < values.Length; i++)
        {
            if (!values[i].HasValue)
                continue;
            if (!previous.HasValue)
            {
                previous = values[i]!.Value;
            }
            else
            {
                previous = alpha * values[i]!.Value + (1 - alpha) * previous.Value;
            }
            result[i] = previous;
        }
        return result;
    }

    private static double? Average(double[] values, int index, int period)
        => index + 1 < period ? null : values.Skip(index - period + 1).Take(period).Average();

    private static double? Value(IndicatorSeries series, int index, string key)
        => series.Points[index].Values.TryGetValue(key, out double? value) ? value : null;

    private static int Period(double value) => Math.Max(1, (int)Math.Round(value, MidpointRounding.AwayFromZero));
}
