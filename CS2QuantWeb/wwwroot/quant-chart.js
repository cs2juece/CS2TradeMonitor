"use strict";

window.QuantChart = (() => {
  const CHART_ID = "marketChart";
  const OVERLAY_GROUP = "quant-research";
  const COLORS = {
    rise: "#dc3232",
    fall: "#00aa4b",
    neutral: "#8a96a6",
    ma5: "#3498ff",
    ma10: "#efa92f",
    ma20: "#9a7cf4",
    stroke: "#ec5d83",
    segment: "#8d62e8",
    center: "#4da3ff",
    buy: "#dc3232",
    sell: "#00aa4b",
    risk: "#d79100"
  };

  let chart = null;
  let renderGeneration = 0;

  function requireLibrary() {
    if (!window.klinecharts) {
      throw new Error("KLineChart 组件加载失败，请刷新页面后重试。");
    }
    return window.klinecharts;
  }

  function registerOverlays(library) {
    const supported = new Set(library.getSupportedOverlays());
    registerLineOverlay(library, supported, "chan_stroke", COLORS.stroke, 2.2, "solid");
    registerLineOverlay(library, supported, "chan_segment", COLORS.segment, 2.8, "dashed");

    if (!supported.has("chan_center")) {
      library.registerOverlay({
        name: "chan_center",
        totalStep: 3,
        lock: true,
        needDefaultPointFigure: false,
        needDefaultXAxisFigure: false,
        needDefaultYAxisFigure: false,
        createPointFigures: ({ coordinates }) => {
          if (coordinates.length !== 2) return [];
          const left = Math.min(coordinates[0].x, coordinates[1].x);
          const top = Math.min(coordinates[0].y, coordinates[1].y);
          return [{
            type: "rect",
            attrs: {
              x: left,
              y: top,
              width: Math.max(2, Math.abs(coordinates[1].x - coordinates[0].x)),
              height: Math.max(2, Math.abs(coordinates[1].y - coordinates[0].y))
            },
            styles: {
              style: "stroke_fill",
              color: "rgba(77, 163, 255, 0.14)",
              borderColor: COLORS.center,
              borderSize: 1.2,
              borderStyle: "solid",
              borderDashedValue: [4, 4],
              borderRadius: 0
            }
          }];
        }
      });
    }

    if (!supported.has("chan_fractal")) {
      library.registerOverlay({
        name: "chan_fractal",
        totalStep: 2,
        lock: true,
        needDefaultPointFigure: false,
        needDefaultXAxisFigure: false,
        needDefaultYAxisFigure: false,
        createPointFigures: ({ coordinates, overlay }) => {
          if (coordinates.length !== 1) return [];
          const point = coordinates[0];
          const isTop = overlay.extendData?.kind === "Top";
          const direction = isTop ? 1 : -1;
          const color = isTop ? COLORS.sell : COLORS.buy;
          const anchorY = point.y + direction * 8;
          return [
            {
              type: "polygon",
              attrs: {
                coordinates: [
                  { x: point.x, y: anchorY },
                  { x: point.x - 5, y: anchorY + direction * 7 },
                  { x: point.x + 5, y: anchorY + direction * 7 }
                ]
              },
              styles: {
                style: "fill",
                color,
                borderColor: color,
                borderSize: 1,
                borderStyle: "solid",
                borderDashedValue: []
              }
            },
            {
              type: "text",
              attrs: {
                x: point.x,
                y: anchorY + direction * 16,
                text: isTop ? "顶" : "底",
                align: "center",
                baseline: "middle"
              },
              styles: textStyle(color, 11)
            }
          ];
        }
      });
    }

    if (!supported.has("chan_signal")) {
      library.registerOverlay({
        name: "chan_signal",
        totalStep: 2,
        lock: true,
        needDefaultPointFigure: false,
        needDefaultXAxisFigure: false,
        needDefaultYAxisFigure: false,
        createPointFigures: ({ coordinates, overlay }) => {
          if (coordinates.length !== 1) return [];
          const point = coordinates[0];
          const side = overlay.extendData?.side || "Risk";
          const color = side === "Buy" ? COLORS.buy : side === "Sell" ? COLORS.sell : COLORS.risk;
          const label = overlay.extendData?.label || (side === "Buy" ? "买" : side === "Sell" ? "卖" : "风险");
          const y = point.y + (side === "Buy" ? 24 : -24);
          return [{
            type: "text",
            attrs: { x: point.x, y, text: label, align: "center", baseline: "middle" },
            styles: {
              ...textStyle("#ffffff", 11),
              style: "fill",
              backgroundColor: color,
              paddingLeft: 6,
              paddingRight: 6,
              paddingTop: 3,
              paddingBottom: 3,
              borderRadius: 5
            }
          }];
        }
      });
    }
  }

  function registerLineOverlay(library, supported, name, color, size, style) {
    if (supported.has(name)) return;
    library.registerOverlay({
      name,
      totalStep: 3,
      lock: true,
      needDefaultPointFigure: false,
      needDefaultXAxisFigure: false,
      needDefaultYAxisFigure: false,
      createPointFigures: ({ coordinates }) => coordinates.length === 2 ? [{
        type: "line",
        attrs: { coordinates },
        styles: { color, size, style, dashedValue: style === "dashed" ? [6, 4] : [] }
      }] : []
    });
  }

  function registerServerIndicators(library, indicatorSeries) {
    const supported = new Set(library.getSupportedIndicators());
    for (const series of indicatorSeries || []) {
      const name = indicatorName(series.id);
      if (supported.has(name)) continue;
      library.registerIndicator({
        name,
        shortName: `${series.code}(${series.parameters.join(",")})`,
        series: series.placement === "Main" ? "price" : "normal",
        precision: 4,
        shouldOhlc: series.placement === "Main",
        figures: (series.outputs || []).map(output => ({
          key: output.key,
          title: `${output.label}: `,
          type: output.figure === "bar" ? "bar" : "line",
          ...(output.figure === "bar" ? { baseValue: 0 } : {})
        })),
        calc: dataList => dataList.map(item => item.quantIndicators?.[series.id] || {})
      });
      supported.add(name);
    }
  }

  function indicatorName(id) {
    return `Q_${String(id).replace(/[^a-z0-9_]/gi, "_")}`;
  }

  function textStyle(color, size) {
    return {
      style: "fill",
      color,
      size,
      family: "Microsoft YaHei UI",
      weight: 700,
      borderStyle: "solid",
      borderDashedValue: [],
      borderSize: 0,
      borderColor: "transparent",
      borderRadius: 0,
      backgroundColor: "transparent",
      paddingLeft: 0,
      paddingTop: 0,
      paddingRight: 0,
      paddingBottom: 0
    };
  }

  function render(options) {
    const library = requireLibrary();
    registerOverlays(library);
    registerServerIndicators(library, options.indicatorSeries);
    dispose();

    const container = options.container;
    const colors = readThemeColors();
    const generation = ++renderGeneration;
    const valuesByDate = new Map();
    for (const series of options.indicatorSeries || []) {
      (series.points || []).forEach(point => {
        const values = valuesByDate.get(point.date) || {};
        values[series.id] = point.values;
        valuesByDate.set(point.date, values);
      });
    }
    const chartData = options.candles.map(candle => ({
      timestamp: dateToTimestamp(candle.date),
      open: Number(candle.open),
      high: Number(candle.high),
      low: Number(candle.low),
      close: Number(candle.close),
      volume: Number(candle.volume || 0),
      turnover: candle.turnover == null ? null : Number(candle.turnover),
      quantIndicators: valuesByDate.get(candle.date) || {}
    }));

    chart = library.init(container, {
      locale: "zh-CN",
      timezone: "Asia/Shanghai",
      styles: createStyles(colors)
    });
    if (!chart) throw new Error("KLineChart 初始化失败。");

    chart.setSymbol({
      ticker: options.symbol || "CS2",
      pricePrecision: inferPrecision(chartData),
      volumePrecision: 0
    });
    chart.setPeriod({ span: 1, type: options.interval === "Week" ? "week" : "day" });
    for (const series of options.indicatorSeries || []) {
      if (!series.isAvailable) continue;
      const config = { name: indicatorName(series.id) };
      if (series.placement === "Main") {
        chart.createIndicator({ ...config, paneId: "candle_pane" }, true);
      } else {
        const paneId = chart.createIndicator(config, false);
        if (paneId) chart.setPaneOptions({ id: paneId, height: 120, minHeight: 84 });
      }
    }
    chart.setOffsetRightDistance(36);

    chart.setDataLoader({
      getBars: ({ type, callback }) => {
        callback(type === "init" ? chartData : [], false);
        if (type === "init") {
          requestAnimationFrame(() => {
            if (!chart || generation !== renderGeneration) return;
            fitVisibleBars(container, options.visibleCount || chartData.length);
            createAnalysisOverlays(options);
          });
        }
      }
    });
  }

  function createStyles(colors) {
    const font = "Microsoft YaHei UI";
    return {
      grid: {
        horizontal: { color: colors.border, size: 1, style: "dashed", dashedValue: [3, 3] },
        vertical: { color: colors.border, size: 1, style: "dashed", dashedValue: [3, 3] }
      },
      candle: {
        type: "candle_solid",
        bar: {
          compareRule: "current_open",
          upColor: COLORS.rise,
          downColor: COLORS.fall,
          noChangeColor: COLORS.neutral,
          upBorderColor: COLORS.rise,
          downBorderColor: COLORS.fall,
          noChangeBorderColor: COLORS.neutral,
          upWickColor: COLORS.rise,
          downWickColor: COLORS.fall,
          noChangeWickColor: COLORS.neutral
        },
        priceMark: {
          high: { color: colors.subtle, textSize: 12, textFamily: font },
          low: { color: colors.subtle, textSize: 12, textFamily: font },
          last: {
            upColor: COLORS.rise,
            downColor: COLORS.fall,
            noChangeColor: COLORS.neutral,
            text: { size: 12, family: font, weight: 600 }
          }
        },
        tooltip: {
          title: { color: colors.text, size: 13, family: font, weight: 600 },
          legend: { color: colors.subtle, size: 12, family: font, weight: 500 }
        }
      },
      indicator: {
        lines: [
          { color: COLORS.ma5, size: 1.6 },
          { color: COLORS.ma10, size: 1.6 },
          { color: COLORS.ma20, size: 1.6 },
          { color: "#ff6d75", size: 1.5 },
          { color: "#5f8dff", size: 1.5 }
        ],
        bars: [{
          upColor: COLORS.rise,
          downColor: COLORS.fall,
          noChangeColor: COLORS.neutral,
          style: "fill",
          borderColor: "transparent",
          borderSize: 0,
          borderStyle: "solid",
          borderDashedValue: []
        }],
        tooltip: {
          title: { color: colors.text, size: 12, family: font, weight: 600 },
          legend: { color: colors.subtle, size: 12, family: font, weight: 500 }
        }
      },
      xAxis: { tickText: { color: colors.subtle, size: 12, family: font, weight: 500 } },
      yAxis: { tickText: { color: colors.subtle, size: 12, family: font, weight: 500 } },
      separator: { color: colors.border, size: 1, fill: true },
      crosshair: {
        horizontal: {
          line: { color: colors.primary, size: 1, style: "dashed", dashedValue: [4, 4] },
          text: { color: "#ffffff", size: 12, family: font, weight: 600, backgroundColor: colors.primary }
        },
        vertical: {
          line: { color: colors.primary, size: 1, style: "dashed", dashedValue: [4, 4] },
          text: { color: "#ffffff", size: 12, family: font, weight: 600, backgroundColor: colors.primary }
        }
      }
    };
  }

  function createAnalysisOverlays(options) {
    if (!chart) return;
    chart.removeOverlay({ groupId: OVERLAY_GROUP });
    const chan = options.chan || {};
    const overlays = [];

    for (const center of chan.centers || []) {
      overlays.push(overlay("chan_center", [
        point(center.startDate, center.upper),
        point(center.endDate, center.lower)
      ]));
    }
    for (const stroke of chan.strokes || []) {
      overlays.push(overlay("chan_stroke", [
        point(stroke.startDate, stroke.startPrice),
        point(stroke.endDate, stroke.endPrice)
      ]));
    }
    for (const segment of chan.segments || []) {
      overlays.push(overlay("chan_segment", [
        point(segment.startDate, segment.startPrice),
        point(segment.endDate, segment.endPrice)
      ]));
    }
    for (const fractal of chan.fractals || []) {
      overlays.push(overlay("chan_fractal", [point(fractal.date, fractal.price)], { kind: fractal.kind }));
    }
    for (const signal of options.signals || []) {
      overlays.push(overlay("chan_signal", [point(signal.date, signal.price)], {
        side: signal.side,
        label: signal.chartLabel || (signal.chanType ? signal.level : null),
        reason: signal.reason
      }));
    }

    if (overlays.length > 0) chart.createOverlay(overlays);
  }

  function overlay(name, points, extendData) {
    return {
      name,
      groupId: OVERLAY_GROUP,
      paneId: "candle_pane",
      lock: true,
      visible: true,
      zLevel: name === "chan_signal" ? 30 : name === "chan_fractal" ? 20 : 10,
      points,
      extendData
    };
  }

  function point(date, value) {
    return { timestamp: dateToTimestamp(date), value: Number(value) };
  }

  function fitVisibleBars(container, visibleCount) {
    if (!chart) return;
    const count = Math.max(5, Number(visibleCount) || 30);
    const available = Math.max(260, container.clientWidth - 92);
    chart.setBarSpace(Math.max(3, Math.min(50, available / count)));
    chart.scrollToRealTime();
  }

  function focusDate(date) {
    if (!chart) return;
    const timestamp = dateToTimestamp(date);
    const index = chart.getDataList().findIndex(item => item.timestamp === timestamp);
    if (index < 0) return;
    chart.scrollToDataIndex(index, 260);
    chart.executeAction("onCrosshairChange", { dataIndex: index, timestamp });
  }

  function resetView() {
    if (!chart) return;
    chart.scrollToRealTime(220);
  }

  function resize() {
    chart?.resize();
  }

  function dispose() {
    if (!chart) return;
    requireLibrary().dispose(CHART_ID);
    chart = null;
  }

  function dateToTimestamp(date) {
    const parts = String(date).split("-").map(Number);
    if (parts.length !== 3 || parts.some(value => !Number.isInteger(value))) return Date.parse(date);
    return Date.UTC(parts[0], parts[1] - 1, parts[2]);
  }

  function inferPrecision(data) {
    let precision = 0;
    for (const item of data.slice(-80)) {
      for (const value of [item.open, item.high, item.low, item.close]) {
        const decimals = String(value).split(".")[1]?.length || 0;
        precision = Math.max(precision, Math.min(decimals, 4));
      }
    }
    return Math.max(2, precision);
  }

  function readThemeColors() {
    const style = getComputedStyle(document.documentElement);
    return {
      text: style.getPropertyValue("--text").trim(),
      subtle: style.getPropertyValue("--subtle").trim(),
      border: style.getPropertyValue("--border").trim(),
      card: style.getPropertyValue("--card-muted").trim(),
      primary: style.getPropertyValue("--primary").trim()
    };
  }

  return { render, focusDate, resetView, resize, dispose };
})();
