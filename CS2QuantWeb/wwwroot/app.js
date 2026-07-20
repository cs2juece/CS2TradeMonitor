"use strict";

const state = {
  result: null,
  selectedItem: null,
  itemResults: [],
  activeSuggestion: -1,
  searchRequest: null,
  visibleCandles: [],
  visibleIndicators: [],
  chartMetrics: null,
  signals: [],
  renderedRange: "30"
};

const elements = {
  form: document.getElementById("sourceForm"),
  source: document.getElementById("sourceSelect"),
  itemField: document.getElementById("itemField"),
  itemSearch: document.getElementById("itemSearchInput"),
  itemSuggestions: document.getElementById("itemSuggestions"),
  itemSearchStatus: document.getElementById("itemSearchStatus"),
  symbolField: document.getElementById("symbolField"),
  symbol: document.getElementById("symbolInput"),
  sourceHint: document.getElementById("sourceHint"),
  analyzeButton: document.getElementById("analyzeButton"),
  exportLink: document.getElementById("exportLink"),
  message: document.getElementById("message"),
  workspace: document.getElementById("workspace"),
  serviceState: document.getElementById("serviceState"),
  summaryGrid: document.getElementById("summaryGrid"),
  seriesTitle: document.getElementById("seriesTitle"),
  seriesMeta: document.getElementById("seriesMeta"),
  range: document.getElementById("rangeSelect"),
  canvas: document.getElementById("marketChart"),
  tooltip: document.getElementById("chartTooltip"),
  structureStats: document.getElementById("structureStats"),
  conclusions: document.getElementById("chanConclusions"),
  backtestGrid: document.getElementById("backtestGrid"),
  sideFilter: document.getElementById("sideFilter"),
  signalSearch: document.getElementById("signalSearch"),
  signalRows: document.getElementById("signalRows"),
  emptySignals: document.getElementById("emptySignals"),
  methodNote: document.getElementById("methodNote")
};

const sourceDescriptions = {
  item: "搜索本地饰品库；一年以内读取日线，两年和全部读取周线。",
  csqaq: "调用 CSQAQ 大盘日线；密钥仅从服务进程环境变量读取。",
  csv: "仅读取网页服务 data 目录中的 CSV 文件名，禁止任意路径。"
};

elements.source.addEventListener("change", updateSourceUi);
const queueItemSearch = debounce(searchItems, 250);
elements.itemSearch.addEventListener("input", () => {
  state.selectedItem = null;
  disableExport();
  queueItemSearch();
});
elements.itemSearch.addEventListener("keydown", handleItemSearchKeydown);
elements.itemSearch.addEventListener("focus", () => {
  if (state.itemResults.length > 0 && !state.selectedItem) openItemSuggestions();
});
elements.itemSuggestions.addEventListener("mousedown", event => {
  const option = event.target.closest("[data-index]");
  if (option) selectItem(Number(option.dataset.index));
});
document.addEventListener("mousedown", event => {
  if (!elements.itemField.contains(event.target)) closeItemSuggestions();
});
elements.form.addEventListener("submit", event => {
  event.preventDefault();
  loadAnalysis();
});
elements.range.addEventListener("change", handleRangeChange);
elements.sideFilter.addEventListener("change", renderSignalRows);
elements.signalSearch.addEventListener("input", renderSignalRows);
elements.canvas.addEventListener("mousemove", showChartTooltip);
elements.canvas.addEventListener("mouseleave", () => { elements.tooltip.hidden = true; });
window.addEventListener("resize", debounce(drawChart, 100));
window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", drawChart);

updateSourceUi();
checkHealth();

async function checkHealth() {
  try {
    const response = await fetch("/health", { cache: "no-store" });
    if (!response.ok) throw new Error("health check failed");
    elements.serviceState.className = "service-state online";
    elements.serviceState.lastElementChild.textContent = "本地服务已连接";
  } catch {
    elements.serviceState.className = "service-state error";
    elements.serviceState.lastElementChild.textContent = "本地服务不可用";
  }
}

function updateSourceUi() {
  const source = elements.source.value;
  hideMessage();
  disableExport();
  const isItem = source === "item";
  elements.form.classList.toggle("csv-mode", source === "csv");
  elements.form.classList.toggle("item-mode", isItem);
  elements.itemField.hidden = !isItem;
  elements.symbolField.hidden = source !== "csv";
  elements.sourceHint.textContent = sourceDescriptions[source];
  closeItemSuggestions();
  if (source === "csv") elements.symbol.focus();
  if (isItem) elements.itemSearch.focus();
}

async function searchItems() {
  const query = elements.itemSearch.value.trim();
  state.searchRequest?.abort();
  state.searchRequest = null;
  state.itemResults = [];
  state.activeSuggestion = -1;
  closeItemSuggestions();
  if (query.length < 2) {
    setItemSearchStatus(query.length === 0 ? "输入至少 2 个字符开始搜索" : "请再输入 1 个字符", "");
    return;
  }

  const controller = new AbortController();
  state.searchRequest = controller;
  setItemSearchStatus("正在搜索本地饰品库…", "loading");
  try {
    const response = await fetch(`/api/items/search?q=${encodeURIComponent(query)}`, {
      cache: "no-store",
      signal: controller.signal
    });
    if (!response.ok) {
      let detail = `搜索失败（HTTP ${response.status}）`;
      try {
        const problem = await response.json();
        detail = problem.detail || problem.title || detail;
      } catch { }
      throw new Error(detail);
    }

    const results = await response.json();
    if (controller.signal.aborted || elements.itemSearch.value.trim() !== query) return;
    state.itemResults = Array.isArray(results) ? results : [];
    state.activeSuggestion = state.itemResults.length > 0 ? 0 : -1;
    renderItemSuggestions();
    if (state.itemResults.length === 0) {
      setItemSearchStatus("未找到匹配单品，请尝试中文名或英文名。", "error");
      return;
    }
    setItemSearchStatus(`找到 ${state.itemResults.length} 个结果，请选择单品。`, "success");
    openItemSuggestions();
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") return;
    setItemSearchStatus(error instanceof Error ? error.message : "单品搜索失败，请稍后重试。", "error");
  } finally {
    if (state.searchRequest === controller) state.searchRequest = null;
  }
}

function renderItemSuggestions() {
  elements.itemSuggestions.replaceChildren(...state.itemResults.map((item, index) => {
    const option = document.createElement("li");
    option.id = `item-option-${index}`;
    option.dataset.index = String(index);
    option.className = index === state.activeSuggestion ? "active" : "";
    option.setAttribute("role", "option");
    option.setAttribute("aria-selected", String(index === state.activeSuggestion));
    const name = document.createElement("strong");
    name.textContent = item.name;
    const marketName = document.createElement("small");
    marketName.textContent = item.marketHashName;
    option.append(name, marketName);
    return option;
  }));
  updateActiveSuggestion();
}

function handleItemSearchKeydown(event) {
  if (event.key === "Escape") {
    closeItemSuggestions();
    return;
  }
  if (state.itemResults.length === 0 || elements.itemSuggestions.hidden) return;
  if (event.key === "ArrowDown") {
    event.preventDefault();
    state.activeSuggestion = (state.activeSuggestion + 1) % state.itemResults.length;
    updateActiveSuggestion();
  } else if (event.key === "ArrowUp") {
    event.preventDefault();
    state.activeSuggestion = (state.activeSuggestion - 1 + state.itemResults.length) % state.itemResults.length;
    updateActiveSuggestion();
  } else if (event.key === "Enter") {
    event.preventDefault();
    selectItem(state.activeSuggestion >= 0 ? state.activeSuggestion : 0);
  }
}

function updateActiveSuggestion() {
  const options = elements.itemSuggestions.querySelectorAll("[role=option]");
  options.forEach((option, index) => {
    const active = index === state.activeSuggestion;
    option.classList.toggle("active", active);
    option.setAttribute("aria-selected", String(active));
    if (active) option.scrollIntoView({ block: "nearest" });
  });
  if (state.activeSuggestion >= 0) {
    elements.itemSearch.setAttribute("aria-activedescendant", `item-option-${state.activeSuggestion}`);
  } else {
    elements.itemSearch.removeAttribute("aria-activedescendant");
  }
}

function selectItem(index) {
  const item = state.itemResults[index];
  if (!item) return;
  state.selectedItem = item;
  elements.itemSearch.value = item.name;
  setItemSearchStatus(`已选择：${item.marketHashName}`, "success");
  closeItemSuggestions();
  elements.analyzeButton.focus();
}

function openItemSuggestions() {
  if (state.itemResults.length === 0) return;
  elements.itemSuggestions.hidden = false;
  elements.itemSearch.setAttribute("aria-expanded", "true");
  updateActiveSuggestion();
}

function closeItemSuggestions() {
  elements.itemSuggestions.hidden = true;
  elements.itemSearch.setAttribute("aria-expanded", "false");
  elements.itemSearch.removeAttribute("aria-activedescendant");
}

function setItemSearchStatus(message, tone) {
  elements.itemSearchStatus.textContent = message;
  elements.itemSearchStatus.className = `item-search-status${tone ? ` ${tone}` : ""}`;
}

async function loadAnalysis() {
  hideMessage();
  disableExport();
  const params = new URLSearchParams({ source: elements.source.value });
  if (elements.source.value === "item") {
    if (!state.selectedItem) {
      showMessage("请先搜索并从候选列表中选择一个单品。");
      elements.itemSearch.focus();
      return;
    }
    params.set("symbol", state.selectedItem.marketHashName);
    params.set("range", elements.range.value);
  }
  if (elements.source.value === "csv") params.set("symbol", elements.symbol.value.trim());
  setLoading(true);

  try {
    const response = await fetch(`/api/analyze?${params}`, { cache: "no-store" });
    if (!response.ok) {
      let detail = `请求失败（HTTP ${response.status}）`;
      try {
        const problem = await response.json();
        detail = problem.detail || problem.title || detail;
      } catch { }
      throw new Error(detail);
    }

    state.result = await response.json();
    state.renderedRange = elements.range.value;
    elements.workspace.hidden = false;
    renderWorkspace();
    enableExport(`/api/export/signals.csv?${params}`);
    return true;
  } catch (error) {
    showMessage(error instanceof Error ? error.message : "分析失败，请稍后重试。");
    return false;
  } finally {
    setLoading(false);
  }
}

function setLoading(loading) {
  elements.workspace.classList.toggle("is-loading", loading);
  elements.workspace.setAttribute("aria-busy", String(loading));
  elements.analyzeButton.disabled = loading;
  elements.range.disabled = loading;
  elements.analyzeButton.textContent = loading ? "分析中…" : "开始分析";
}

async function handleRangeChange() {
  if (!state.result) return;
  const requestedRange = elements.range.value;
  const previousRange = state.renderedRange;
  const requiresReload = elements.source.value === "item"
    && state.selectedItem
    && intervalForRange(requestedRange) !== state.result.interval;
  if (!requiresReload) {
    state.renderedRange = requestedRange;
    drawChart();
    return;
  }

  const loaded = await loadAnalysis();
  if (!loaded) {
    elements.range.value = previousRange;
    drawChart();
  }
}

function showMessage(message) {
  elements.message.textContent = message;
  elements.message.classList.remove("hidden");
}

function hideMessage() {
  elements.message.classList.add("hidden");
  elements.message.textContent = "";
}

function disableExport() {
  elements.exportLink.href = "#";
  elements.exportLink.classList.add("disabled");
  elements.exportLink.setAttribute("aria-disabled", "true");
}

function enableExport(href) {
  elements.exportLink.href = href;
  elements.exportLink.classList.remove("disabled");
  elements.exportLink.setAttribute("aria-disabled", "false");
}

function renderWorkspace() {
  const result = state.result;
  const interval = intervalName(result.interval);
  elements.seriesTitle.textContent = result.symbol;
  elements.seriesMeta.textContent = `${sourceName(result.source)} · ${result.summary.startDate} 至 ${result.summary.endDate} · ${result.summary.candleCount} 根${interval}`;
  elements.methodNote.textContent = result.methodNote;
  renderSummary(result.summary, interval);
  renderStructure(result.chan);
  renderBacktests(result.backtests);
  state.signals = [
    ...result.strategySignals.map(signal => ({ ...signal, category: "策略" })),
    ...result.chan.signals.map(signal => ({ ...signal, category: "缠论" }))
  ].sort((a, b) => b.date.localeCompare(a.date));
  renderSignalRows();
  requestAnimationFrame(drawChart);
}

function renderSummary(summary, interval) {
  const cards = [
    ["最新收盘", number(summary.latestClose), summary.endDate, ""],
    ["区间涨跌", percent(summary.periodReturnPercent), `${summary.candleCount} 根 K 线`, marketTone(summary.periodReturnPercent)],
    ["最大回撤", percent(summary.maxDrawdownPercent), `按每${interval === "周线" ? "周" : "日"}收盘计算`, summary.maxDrawdownPercent < 0 ? "negative" : ""],
    ["缠论笔", String(state.result.chan.strokes.length), `${state.result.chan.fractals.length} 个分型`, ""],
    ["研究信号", String(state.result.strategySignals.length + state.result.chan.signals.length), "策略与结构候选", ""]
  ];
  elements.summaryGrid.innerHTML = cards.map(([label, value, note, cardTone]) => `
    <article class="summary-card ${cardTone}">
      <span>${escapeHtml(label)}</span>
      <strong>${escapeHtml(value)}</strong>
      <small>${escapeHtml(note)}</small>
    </article>`).join("");
}

function renderStructure(chan) {
  const stats = [
    ["分型", chan.fractals.length],
    ["笔", chan.strokes.length],
    ["线段", chan.segments.length],
    ["中枢", chan.centers.length]
  ];
  elements.structureStats.innerHTML = stats.map(([label, value]) => `
    <div class="structure-stat"><strong>${value}</strong><span>${label}</span></div>`).join("");
  elements.conclusions.replaceChildren(...chan.conclusions.map(text => {
    const item = document.createElement("li");
    item.textContent = text;
    return item;
  }));
}

function renderBacktests(backtests) {
  elements.backtestGrid.innerHTML = backtests.map(item => `
    <section class="backtest-item">
      <h3>${escapeHtml(item.strategy)}</h3>
      <div class="backtest-metrics">
        <div><span>配对次数</span><strong>${item.tradeCount}</strong></div>
        <div><span>累计收益</span><strong>${percent(item.totalReturnPercent)}</strong></div>
        <div><span>胜率</span><strong>${percent(item.winRatePercent)}</strong></div>
        <div><span>平均持有</span><strong>${item.averageHoldingDays.toFixed(1)} 天</strong></div>
      </div>
    </section>`).join("");
}

function renderSignalRows() {
  const side = elements.sideFilter.value;
  const query = elements.signalSearch.value.trim().toLocaleLowerCase("zh-CN");
  const rows = state.signals.filter(signal => {
    const sideMatches = side === "all" || signal.side === side;
    const textMatches = !query || `${signal.strategy} ${signal.reason} ${signal.category}`.toLocaleLowerCase("zh-CN").includes(query);
    return sideMatches && textMatches;
  }).slice(0, 80);

  elements.signalRows.innerHTML = rows.map(signal => `
    <tr>
      <td data-label="日期">${escapeHtml(signal.date)}</td>
      <td data-label="策略"><strong>${escapeHtml(signal.strategy)}</strong><br><small>${escapeHtml(signal.category)}</small></td>
      <td data-label="方向"><span class="signal-side ${signal.side.toLowerCase()}">${sideName(signal.side)}</span></td>
      <td data-label="价格">${number(signal.price)}</td>
      <td data-label="触发原因">${escapeHtml(signal.reason)}</td>
    </tr>`).join("");
  elements.emptySignals.hidden = rows.length > 0;
}

function drawChart() {
  if (!state.result) return;
  const start = getRangeStartIndex(state.result.candles, elements.range.value);
  state.visibleCandles = state.result.candles.slice(start);
  state.visibleIndicators = state.result.indicators.slice(start);

  const canvas = elements.canvas;
  const rect = canvas.getBoundingClientRect();
  if (rect.width < 40 || rect.height < 40) return;
  const ratio = Math.max(1, window.devicePixelRatio || 1);
  canvas.width = Math.round(rect.width * ratio);
  canvas.height = Math.round(rect.height * ratio);
  const context = canvas.getContext("2d");
  context.setTransform(ratio, 0, 0, ratio, 0, 0);

  const style = getComputedStyle(document.documentElement);
  const colors = {
    text: style.getPropertyValue("--subtle").trim(),
    border: style.getPropertyValue("--border").trim(),
    card: style.getPropertyValue("--card-muted").trim(),
    positive: style.getPropertyValue("--positive").trim(),
    negative: style.getPropertyValue("--negative").trim(),
    marketRise: style.getPropertyValue("--market-rise").trim(),
    marketFall: style.getPropertyValue("--market-fall").trim(),
    primary: style.getPropertyValue("--primary").trim()
  };
  const width = rect.width;
  const height = rect.height;
  const padding = { left: 58, right: 18, top: 16, bottom: 30 };
  const macdHeight = Math.max(82, height * .2);
  const gap = 26;
  const priceBottom = height - padding.bottom - macdHeight - gap;
  const plotWidth = width - padding.left - padding.right;
  const priceHeight = priceBottom - padding.top;
  const candles = state.visibleCandles;
  const values = candles.flatMap(candle => [candle.high, candle.low]);
  const priceMin = Math.min(...values);
  const priceMax = Math.max(...values);
  const pricePad = Math.max((priceMax - priceMin) * .08, priceMax * .005);
  const lower = priceMin - pricePad;
  const upper = priceMax + pricePad;
  const step = plotWidth / Math.max(candles.length, 1);
  const xAt = index => padding.left + (index + .5) * step;
  const yAt = price => padding.top + (upper - price) / Math.max(upper - lower, .0001) * priceHeight;

  context.clearRect(0, 0, width, height);
  context.fillStyle = colors.card;
  context.fillRect(0, 0, width, height);
  drawGrid(context, padding, width, priceBottom, lower, upper, colors, yAt);
  drawCenters(context, state.result.chan.centers, start, candles.length, xAt, yAt, colors.primary, priceBottom);
  drawCandles(context, candles, step, xAt, yAt, colors);
  drawAverage(context, state.visibleIndicators, "ma5", "#4da3ff", xAt, yAt);
  drawAverage(context, state.visibleIndicators, "ma10", "#f0b34b", xAt, yAt);
  drawAverage(context, state.visibleIndicators, "ma20", "#a98cf5", xAt, yAt);
  drawStrokes(context, state.result.chan.strokes, start, candles.length, xAt, yAt);
  drawMacd(context, state.visibleIndicators, padding, width, height, macdHeight, step, xAt, colors);
  drawDates(context, candles, padding, width, height, xAt, colors.text);

  state.chartMetrics = { rect, start, step, xAt, yAt, priceBottom };
}

function getRangeStartIndex(candles, rangeValue) {
  if (!candles.length || rangeValue === "all") return 0;
  const dayCount = Number(rangeValue);
  if (!Number.isFinite(dayCount) || dayCount < 1) return 0;

  const latestParts = candles[candles.length - 1].date.split("-").map(Number);
  if (latestParts.length !== 3 || latestParts.some(value => !Number.isInteger(value))) {
    return Math.max(0, candles.length - dayCount);
  }

  const latestUtc = Date.UTC(latestParts[0], latestParts[1] - 1, latestParts[2]);
  const cutoffUtc = latestUtc - (dayCount - 1) * 86_400_000;
  const cutoffDate = new Date(cutoffUtc).toISOString().slice(0, 10);
  const start = candles.findIndex(candle => candle.date >= cutoffDate);
  return start < 0 ? 0 : start;
}

function intervalForRange(rangeValue) {
  return rangeValue === "730" || rangeValue === "all" ? "Week" : "Day";
}

function drawGrid(context, padding, width, priceBottom, lower, upper, colors, yAt) {
  context.font = '11px "Microsoft YaHei UI", sans-serif';
  context.textAlign = "right";
  context.textBaseline = "middle";
  for (let line = 0; line <= 5; line++) {
    const value = lower + (upper - lower) * line / 5;
    const y = yAt(value);
    context.strokeStyle = colors.border;
    context.globalAlpha = .55;
    context.beginPath();
    context.moveTo(padding.left, y);
    context.lineTo(width - padding.right, y);
    context.stroke();
    context.globalAlpha = 1;
    context.fillStyle = colors.text;
    context.fillText(number(value), padding.left - 8, y);
  }
  context.strokeStyle = colors.border;
  context.beginPath();
  context.moveTo(padding.left, priceBottom);
  context.lineTo(width - padding.right, priceBottom);
  context.stroke();
}

function drawCandles(context, candles, step, xAt, yAt, colors) {
  const bodyWidth = Math.max(2, Math.min(10, step * .62));
  candles.forEach((candle, index) => {
    const x = xAt(index);
    const rising = candle.close >= candle.open;
    context.strokeStyle = rising ? colors.marketRise : colors.marketFall;
    context.fillStyle = rising ? colors.marketRise : colors.marketFall;
    context.lineWidth = 1;
    context.beginPath();
    context.moveTo(x, yAt(candle.high));
    context.lineTo(x, yAt(candle.low));
    context.stroke();
    const top = yAt(Math.max(candle.open, candle.close));
    const bottom = yAt(Math.min(candle.open, candle.close));
    const bodyHeight = Math.max(1.5, bottom - top);
    if (rising) {
      context.globalAlpha = .78;
      context.fillRect(x - bodyWidth / 2, top, bodyWidth, bodyHeight);
      context.globalAlpha = 1;
    } else {
      context.fillRect(x - bodyWidth / 2, top, bodyWidth, bodyHeight);
    }
  });
}

function drawAverage(context, indicators, field, color, xAt, yAt) {
  context.strokeStyle = color;
  context.lineWidth = 1.45;
  context.beginPath();
  let drawing = false;
  indicators.forEach((point, index) => {
    const value = point[field];
    if (value == null) { drawing = false; return; }
    if (!drawing) { context.moveTo(xAt(index), yAt(value)); drawing = true; }
    else context.lineTo(xAt(index), yAt(value));
  });
  context.stroke();
}

function drawStrokes(context, strokes, start, visibleCount, xAt, yAt) {
  context.strokeStyle = "#e75d86";
  context.lineWidth = 2.1;
  context.globalAlpha = .92;
  for (const stroke of strokes) {
    if (stroke.endIndex < start || stroke.startIndex >= start + visibleCount) continue;
    const startIndex = stroke.startIndex - start;
    const endIndex = stroke.endIndex - start;
    if (startIndex < 0 || endIndex >= visibleCount) continue;
    context.beginPath();
    context.moveTo(xAt(startIndex), yAt(stroke.startPrice));
    context.lineTo(xAt(endIndex), yAt(stroke.endPrice));
    context.stroke();
  }
  context.globalAlpha = 1;
}

function drawCenters(context, centers, start, visibleCount, xAt, yAt, color, priceBottom) {
  context.fillStyle = color;
  context.strokeStyle = color;
  for (const center of centers) {
    const leftIndex = Math.max(center.startIndex - start, 0);
    const rightIndex = Math.min(center.endIndex - start, visibleCount - 1);
    if (rightIndex < 0 || leftIndex >= visibleCount) continue;
    const left = xAt(leftIndex);
    const right = xAt(rightIndex);
    const top = Math.min(yAt(center.upper), priceBottom);
    const bottom = Math.min(yAt(center.lower), priceBottom);
    context.globalAlpha = .1;
    context.fillRect(left, top, Math.max(2, right - left), Math.max(2, bottom - top));
    context.globalAlpha = .48;
    context.strokeRect(left, top, Math.max(2, right - left), Math.max(2, bottom - top));
  }
  context.globalAlpha = 1;
}

function drawMacd(context, indicators, padding, width, height, macdHeight, step, xAt, colors) {
  const top = height - padding.bottom - macdHeight;
  const bottom = height - padding.bottom;
  const max = Math.max(...indicators.map(point => Math.abs(point.histogram)), .001);
  const zero = top + macdHeight / 2;
  context.fillStyle = colors.text;
  context.font = '10px "Microsoft YaHei UI", sans-serif';
  context.textAlign = "left";
  context.fillText("MACD", padding.left, top - 7);
  context.strokeStyle = colors.border;
  context.beginPath();
  context.moveTo(padding.left, zero);
  context.lineTo(width - padding.right, zero);
  context.stroke();
  const barWidth = Math.max(1, Math.min(7, step * .55));
  indicators.forEach((point, index) => {
    const barHeight = Math.abs(point.histogram) / max * (macdHeight / 2 - 4);
    context.fillStyle = point.histogram >= 0 ? colors.marketRise : colors.marketFall;
    context.globalAlpha = .68;
    context.fillRect(xAt(index) - barWidth / 2, point.histogram >= 0 ? zero - barHeight : zero, barWidth, barHeight);
  });
  context.globalAlpha = 1;
  context.strokeStyle = colors.border;
  context.strokeRect(padding.left, top, width - padding.left - padding.right, bottom - top);
}

function drawDates(context, candles, padding, width, height, xAt, color) {
  if (!candles.length) return;
  const marks = Math.min(5, candles.length);
  context.fillStyle = color;
  context.font = '10px "Microsoft YaHei UI", sans-serif';
  context.textAlign = "center";
  context.textBaseline = "bottom";
  for (let mark = 0; mark < marks; mark++) {
    const index = Math.round(mark * (candles.length - 1) / Math.max(marks - 1, 1));
    context.fillText(candles[index].date.slice(5), xAt(index), height - 5);
  }
}

function showChartTooltip(event) {
  if (!state.chartMetrics || !state.visibleCandles.length) return;
  const bounds = elements.canvas.getBoundingClientRect();
  const mouseX = event.clientX - bounds.left;
  const index = Math.max(0, Math.min(state.visibleCandles.length - 1,
    Math.floor((mouseX - 58) / state.chartMetrics.step)));
  const candle = state.visibleCandles[index];
  const indicator = state.visibleIndicators[index];
  elements.tooltip.innerHTML = `
    <strong>${escapeHtml(candle.date)}</strong><br>
    开 ${number(candle.open)}　高 ${number(candle.high)}<br>
    低 ${number(candle.low)}　收 ${number(candle.close)}<br>
    MA5 ${indicator.ma5 == null ? "—" : number(indicator.ma5)}　MACD ${number(indicator.histogram)}`;
  elements.tooltip.hidden = false;
  const left = Math.min(Math.max(event.clientX - bounds.left + 12, 8), bounds.width - 238);
  const top = Math.min(Math.max(event.clientY - bounds.top + 12, 8), bounds.height - 112);
  elements.tooltip.style.left = `${left}px`;
  elements.tooltip.style.top = `${top}px`;
}

function sourceName(source) {
  return ({ "steamdt-item": "SteamDT 单品 K 线", csv: "本地 CSV", csqaq: "CSQAQ 大盘日线" })[source] || source;
}

function intervalName(interval) {
  return interval === "Week" ? "周线" : "日线";
}

function sideName(side) {
  return ({ Buy: "买入", Sell: "卖出", Risk: "风险" })[side] || side;
}

function number(value) {
  return Number(value).toLocaleString("zh-CN", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function percent(value) {
  const numeric = Number(value);
  return `${numeric > 0 ? "+" : ""}${numeric.toFixed(2)}%`;
}

function marketTone(value) { return Number(value) > 0 ? "market-rise" : Number(value) < 0 ? "market-fall" : ""; }

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function debounce(callback, delay) {
  let timer;
  return (...args) => {
    clearTimeout(timer);
    timer = setTimeout(() => callback(...args), delay);
  };
}
