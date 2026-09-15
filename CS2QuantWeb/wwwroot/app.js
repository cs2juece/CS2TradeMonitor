"use strict";

const state = {
  result: null,
  selectedItem: null,
  itemResults: [],
  activeSuggestion: -1,
  searchRequest: null,
  visibleCandles: [],
  signals: [],
  chartSignals: [],
  renderedRange: "30",
  catalog: null,
  indicators: [],
  pendingIndicators: [],
  strategy: null,
  pendingStrategy: null,
  customStrategies: [],
  parameterTargetId: null,
  lastAnalyzeRequest: null
};

const PROFILE_STORAGE_KEY = "cs2-quant-research-profile-v1";
const INDICATOR_LINE_COLORS = ["#4da3ff", "#f0b34b", "#a98cf5", "#ff6d75", "#5f8dff"];

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
  platformFee: document.getElementById("platformFeeInput"),
  spreadBps: document.getElementById("spreadBpsInput"),
  slippageBps: document.getElementById("slippageBpsInput"),
  message: document.getElementById("message"),
  workspace: document.getElementById("workspace"),
  serviceState: document.getElementById("serviceState"),
  summaryGrid: document.getElementById("summaryGrid"),
  resultSummaryStatus: document.getElementById("resultSummaryStatus"),
  resultSummaryGrid: document.getElementById("resultSummaryGrid"),
  qualityPanel: document.getElementById("qualityPanel"),
  qualityTitle: document.getElementById("qualityTitle"),
  qualityDetails: document.getElementById("qualityDetails"),
  executionCostPanel: document.getElementById("executionCostPanel"),
  executionCostTitle: document.getElementById("executionCostTitle"),
  executionCostDetails: document.getElementById("executionCostDetails"),
  seriesTitle: document.getElementById("seriesTitle"),
  seriesMeta: document.getElementById("seriesMeta"),
  range: document.getElementById("rangeSelect"),
  chart: document.getElementById("marketChart"),
  chartWrap: document.getElementById("chartWrap"),
  chartResetButton: document.getElementById("chartResetButton"),
  chartFullscreenButton: document.getElementById("chartFullscreenButton"),
  chartLegend: document.getElementById("chartLegend"),
  structureStats: document.getElementById("structureStats"),
  conclusions: document.getElementById("chanConclusions"),
  backtestGrid: document.getElementById("backtestGrid"),
  walkForwardStatus: document.getElementById("walkForwardStatus"),
  walkForwardGrid: document.getElementById("walkForwardGrid"),
  sideFilter: document.getElementById("sideFilter"),
  signalSearch: document.getElementById("signalSearch"),
  signalRows: document.getElementById("signalRows"),
  emptySignals: document.getElementById("emptySignals"),
  methodNote: document.getElementById("methodNote"),
  profileSummary: document.getElementById("profileSummary"),
  indicatorSettingsButton: document.getElementById("indicatorSettingsButton"),
  strategySettingsButton: document.getElementById("strategySettingsButton"),
  chartIndicatorButton: document.getElementById("chartIndicatorButton"),
  chartStrategyButton: document.getElementById("chartStrategyButton"),
  tPlusSeven: document.getElementById("tPlusSevenToggle"),
  backtestTitle: document.getElementById("backtestTitle"),
  backtestNote: document.getElementById("backtestNote"),
  indicatorDialog: document.getElementById("indicatorDialog"),
  mainIndicatorList: document.getElementById("mainIndicatorList"),
  subIndicatorList: document.getElementById("subIndicatorList"),
  indicatorSelectionCount: document.getElementById("indicatorSelectionCount"),
  saveIndicatorsButton: document.getElementById("saveIndicatorsButton"),
  parameterDialog: document.getElementById("parameterDialog"),
  parameterDialogTitle: document.getElementById("parameterDialogTitle"),
  parameterFields: document.getElementById("parameterFields"),
  parameterHint: document.getElementById("parameterHint"),
  saveParametersButton: document.getElementById("saveParametersButton"),
  strategyDialog: document.getElementById("strategyDialog"),
  strategySelect: document.getElementById("strategySelect"),
  strategyName: document.getElementById("strategyNameInput"),
  strategyEditHint: document.getElementById("strategyEditHint"),
  entryMatchMode: document.getElementById("entryMatchMode"),
  exitMatchMode: document.getElementById("exitMatchMode"),
  entryConditions: document.getElementById("entryConditions"),
  exitConditions: document.getElementById("exitConditions"),
  saveStrategyButton: document.getElementById("saveStrategyButton")
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
elements.signalRows.addEventListener("click", focusSignalOnChart);
elements.signalRows.addEventListener("keydown", event => {
  if (event.key !== "Enter" && event.key !== " ") return;
  event.preventDefault();
  focusSignalOnChart(event);
});
elements.chartResetButton.addEventListener("click", () => window.QuantChart?.resetView());
elements.chartFullscreenButton.addEventListener("click", toggleChartFullscreen);
elements.exportLink.addEventListener("click", exportSignals);
elements.indicatorSettingsButton.addEventListener("click", openIndicatorDialog);
elements.chartIndicatorButton.addEventListener("click", openIndicatorDialog);
elements.strategySettingsButton.addEventListener("click", openStrategyDialog);
elements.chartStrategyButton.addEventListener("click", openStrategyDialog);
elements.tPlusSeven.addEventListener("change", async () => {
  persistProfile();
  updateProfileSummary();
  if (state.result) await loadAnalysis();
});
elements.mainIndicatorList.addEventListener("click", handleIndicatorListClick);
elements.subIndicatorList.addEventListener("click", handleIndicatorListClick);
elements.mainIndicatorList.addEventListener("change", handleIndicatorToggle);
elements.subIndicatorList.addEventListener("change", handleIndicatorToggle);
elements.saveIndicatorsButton.addEventListener("click", saveIndicators);
elements.saveParametersButton.addEventListener("click", saveIndicatorParameters);
elements.parameterDialog.addEventListener("close", () => {
  if (elements.parameterDialog.returnValue !== "cancel" || elements.indicatorDialog.open) return;
  state.parameterTargetId = null;
  renderIndicatorLists();
  elements.indicatorDialog.showModal();
});
elements.strategySelect.addEventListener("change", selectStrategyTemplate);
elements.strategyDialog.addEventListener("click", handleStrategyClick);
elements.strategyDialog.addEventListener("change", handleStrategyFieldChange);
elements.strategyDialog.addEventListener("input", handleStrategyFieldChange);
elements.saveStrategyButton.addEventListener("click", saveStrategy);
window.addEventListener("resize", debounce(() => window.QuantChart?.resize(), 100));
window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", drawChart);
document.addEventListener("fullscreenchange", updateFullscreenButton);

updateSourceUi();
checkHealth();
initializeResearchSettings();

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

async function initializeResearchSettings() {
  try {
    const response = await fetch("/api/research/catalog", { cache: "no-store" });
    if (!response.ok) throw new Error(`配置目录读取失败（HTTP ${response.status}）`);
    state.catalog = await response.json();
    const saved = readStoredProfile();
    state.indicators = clone(saved?.indicators?.length ? saved.indicators : state.catalog.defaultIndicators);
    state.customStrategies = Array.isArray(saved?.customStrategies) ? saved.customStrategies : [];
    elements.tPlusSeven.checked = saved?.lockMode === "TPlusSeven";
    const strategies = availableStrategies();
    state.strategy = clone(strategies.find(item => item.id === saved?.strategyId) || strategies[0] || null);
    updateProfileSummary();
  } catch (error) {
    showMessage(error instanceof Error ? error.message : "研究配置读取失败。");
  }
}

function readStoredProfile() {
  try {
    const value = localStorage.getItem(PROFILE_STORAGE_KEY);
    return value ? JSON.parse(value) : null;
  } catch {
    return null;
  }
}

function persistProfile() {
  try {
    localStorage.setItem(PROFILE_STORAGE_KEY, JSON.stringify({
      version: 1,
      indicators: state.indicators,
      customStrategies: state.customStrategies,
      strategyId: state.strategy?.id || null,
      lockMode: elements.tPlusSeven.checked ? "TPlusSeven" : "None"
    }));
  } catch {
    showMessage("浏览器未允许保存研究配置，本次修改只在当前页面有效。");
  }
}

function availableStrategies() {
  return [...(state.catalog?.strategies || []), ...state.customStrategies];
}

function updateProfileSummary() {
  if (!state.catalog) {
    elements.profileSummary.textContent = "正在读取指标与策略目录…";
    return;
  }
  const indicators = state.indicators.map(item => `${item.code}(${item.parameters.join(",")})`).join(" · ");
  const lock = elements.tPlusSeven.checked ? "T+7 开启" : "T+7 关闭";
  elements.profileSummary.textContent = `${indicators || "未选择指标"} · ${state.strategy?.name || "未选择策略"} · ${lock}`;
}

function clone(value) {
  return value == null ? value : JSON.parse(JSON.stringify(value));
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
    const response = await fetch(`/api/items/search?q=${encodeURIComponent(query)}&limit=100`, {
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

    const payload = await response.json();
    if (controller.signal.aborted || elements.itemSearch.value.trim() !== query) return;
    state.itemResults = Array.isArray(payload?.items) ? payload.items : [];
    state.activeSuggestion = state.itemResults.length > 0 ? 0 : -1;
    renderItemSuggestions();
    if (state.itemResults.length === 0) {
      setItemSearchStatus("未找到匹配单品，请尝试中文名或英文名。", "error");
      return;
    }
    const totalCount = Number.isInteger(payload.totalCount)
      ? payload.totalCount
      : state.itemResults.length;
    if (payload.hasMore) {
      setItemSearchStatus(
        `显示前 ${state.itemResults.length} 个结果（共 ${totalCount} 个），请继续输入缩小范围。`,
        "success"
      );
    } else {
      setItemSearchStatus(`找到 ${totalCount} 个结果，请选择单品。`, "success");
    }
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
  if (!state.catalog) {
    showMessage("指标与策略目录尚未加载，请稍后重试。");
    return false;
  }
  const request = {
    source: elements.source.value,
    range: elements.range.value,
    indicators: state.indicators,
    strategy: state.strategy,
    lockMode: elements.tPlusSeven.checked ? "TPlusSeven" : "None"
  };
  if (elements.source.value === "item") {
    if (!state.selectedItem) {
      showMessage("请先搜索并从候选列表中选择一个单品。");
      elements.itemSearch.focus();
      return;
    }
    request.symbol = state.selectedItem.marketHashName;
  }
  if (elements.source.value === "csv") request.symbol = elements.symbol.value.trim();
  appendCostInputs(request);
  setLoading(true);

  try {
    const response = await fetch("/api/analyze", {
      method: "POST",
      cache: "no-store",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request)
    });
    if (!response.ok) {
      let detail = `请求失败（HTTP ${response.status}）`;
      try {
        const problem = await response.json();
        detail = problem.detail || problem.title || detail;
      } catch { }
      throw new Error(detail);
    }

    state.result = await response.json();
    state.lastAnalyzeRequest = request;
    state.renderedRange = elements.range.value;
    elements.workspace.hidden = false;
    renderWorkspace();
    enableExport();
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
  elements.platformFee.disabled = loading;
  elements.spreadBps.disabled = loading;
  elements.slippageBps.disabled = loading;
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

function enableExport() {
  elements.exportLink.href = "#export";
  elements.exportLink.classList.remove("disabled");
  elements.exportLink.setAttribute("aria-disabled", "false");
}

async function exportSignals(event) {
  event.preventDefault();
  if (!state.lastAnalyzeRequest || elements.exportLink.classList.contains("disabled")) return;
  try {
    const response = await fetch("/api/export/signals.csv", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(state.lastAnalyzeRequest)
    });
    if (!response.ok) {
      const problem = await response.json().catch(() => null);
      throw new Error(problem?.detail || `导出失败（HTTP ${response.status}）`);
    }
    const blob = await response.blob();
    const disposition = response.headers.get("content-disposition") || "";
    const encodedName = disposition.match(/filename\*=UTF-8''([^;]+)/i)?.[1];
    const fileName = encodedName ? decodeURIComponent(encodedName) : `${state.result?.symbol || "research"}-signals.csv`;
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.click();
    URL.revokeObjectURL(url);
  } catch (error) {
    showMessage(error instanceof Error ? error.message : "导出失败。");
  }
}

function renderWorkspace() {
  const result = state.result;
  const interval = intervalName(result.interval);
  elements.seriesTitle.textContent = result.symbol;
  elements.seriesMeta.textContent = `${sourceName(result.source)} · ${result.summary.startDate} 至 ${result.summary.endDate} · ${result.summary.candleCount} 根${interval}`;
  elements.methodNote.textContent = result.methodNote;
  renderSummary(result.summary, interval);
  renderResearchResultSummary(result.resultSummary);
  renderQuality(result.quality);
  renderExecutionCosts(result.executionCosts);
  renderChartLegend(result.indicatorSeries);
  const locked = result.lockMode === "TPlusSeven";
  elements.backtestTitle.textContent = locked ? "T+7 执行回测" : "普通执行回测";
  elements.backtestNote.textContent = locked
    ? "信号在下一根 K 线开盘执行；买入后锁定 7 天，锁定期卖出只记录不成交。已计点差、滑点和卖出方平台手续费，样本结束不强制平仓。"
    : "信号在下一根 K 线开盘执行；T+7 当前关闭，卖出不受持有天数限制。已计点差、滑点和卖出方平台手续费，样本结束不强制平仓。";
  renderStructure(result.chan);
  renderBacktests(result.backtests);
  renderWalkForward(result.walkForward);
  state.signals = [
    ...result.strategySignals.map(signal => ({ ...signal, category: "策略" })),
    ...result.chan.signals.map(signal => ({ ...signal, category: "缠论" }))
  ].sort((a, b) => b.date.localeCompare(a.date));
  state.chartSignals = [
    ...state.signals,
    ...buildExecutionSignals(result.backtests)
  ];
  renderSignalRows();
  requestAnimationFrame(drawChart);
}

function renderChartLegend(indicatorSeries) {
  const indicatorItems = (indicatorSeries || [])
    .filter(series => series.available && series.placement === "Main")
    .flatMap(series => (series.outputs || []).map((output, index) =>
      `<span><i class="legend-line" style="background:${INDICATOR_LINE_COLORS[index % INDICATOR_LINE_COLORS.length]}"></i>${escapeHtml(output.label)}</span>`));
  elements.chartLegend.innerHTML = `${indicatorItems.join("")}
    <span><i class="legend-line stroke"></i>缠论笔</span>
    <span><i class="legend-line segment"></i>线段</span>
    <span><i class="legend-box center"></i>中枢</span>
    <span><i class="legend-dot fractal"></i>分型</span>
    <span><i class="legend-badge signal">买</i>研究信号</span>`;
}

function appendCostInputs(request) {
  const costFields = [
    ["platformFeePercent", elements.platformFee],
    ["spreadBps", elements.spreadBps],
    ["slippageBps", elements.slippageBps]
  ];
  for (const [name, input] of costFields) {
    const value = input.value.trim();
    if (value !== "") request[name] = Number(value);
  }
}

function buildExecutionSignals(backtests) {
  const activeName = state.result?.activeStrategy?.name;
  const active = (backtests || []).find(item => item.strategy === activeName);
  if (!active) return [];
  return (active.executions || []).flatMap((execution, index) => {
    const points = [{
      date: execution.buyDate,
      price: execution.buyPrice,
      side: "Buy",
      chartLabel: "买入成交",
      reason: `第 ${index + 1} 笔策略执行`
    }];
    if (execution.sellDate && execution.sellPrice != null) {
      points.push({
        date: execution.sellDate,
        price: execution.sellPrice,
        side: "Sell",
        chartLabel: "卖出成交",
        reason: execution.exitReason
      });
    }
    return points;
  });
}

function openIndicatorDialog() {
  if (!state.catalog) {
    showMessage("指标目录尚未加载，请稍后重试。");
    return;
  }
  state.pendingIndicators = clone(state.indicators);
  renderIndicatorLists();
  elements.indicatorDialog.showModal();
}

function renderIndicatorLists() {
  renderIndicatorList(elements.mainIndicatorList, "Main");
  renderIndicatorList(elements.subIndicatorList, "Sub");
  elements.indicatorSelectionCount.textContent = `已选择 ${state.pendingIndicators.length} / 12`;
}

function renderIndicatorList(container, placement) {
  const definitions = (state.catalog?.indicators || []).filter(item => item.placements.includes(placement));
  container.innerHTML = definitions.map(definition => {
    const selection = state.pendingIndicators.find(item => item.code === definition.code && item.placement === placement);
    const status = selection ? selection.parameters.join(", ") : "未启用";
    return `<div class="indicator-row">
      <label>
        <input class="indicator-toggle" type="checkbox" data-code="${escapeHtml(definition.code)}" data-placement="${placement}" ${selection ? "checked" : ""}>
        <span><strong>${escapeHtml(definition.name)}</strong><small>${escapeHtml(status)}</small></span>
      </label>
      <button class="parameter-button" type="button" data-indicator-id="${escapeHtml(selection?.id || "")}" ${selection ? "" : "disabled"}>参数</button>
    </div>`;
  }).join("");
}

function handleIndicatorToggle(event) {
  const input = event.target.closest(".indicator-toggle");
  if (!input) return;
  const code = input.dataset.code;
  const placement = input.dataset.placement;
  const existing = state.pendingIndicators.findIndex(item => item.code === code && item.placement === placement);
  if (input.checked && existing < 0) {
    if (state.pendingIndicators.length >= 12) {
      input.checked = false;
      elements.indicatorSelectionCount.textContent = "最多同时启用 12 个指标。";
      return;
    }
    const definition = indicatorDefinition(code);
    state.pendingIndicators.push({
      id: indicatorInstanceId(code, placement),
      code,
      placement,
      parameters: definition.parameters.map(item => item.defaultValue)
    });
  } else if (!input.checked && existing >= 0) {
    state.pendingIndicators.splice(existing, 1);
  }
  renderIndicatorLists();
}

function handleIndicatorListClick(event) {
  const button = event.target.closest("[data-indicator-id]");
  if (!button || !button.dataset.indicatorId) return;
  state.parameterTargetId = button.dataset.indicatorId;
  const selection = state.pendingIndicators.find(item => item.id === state.parameterTargetId);
  const definition = selection && indicatorDefinition(selection.code);
  if (!selection || !definition) return;
  elements.parameterDialogTitle.textContent = `${definition.code} 参数`;
  elements.parameterHint.textContent = definition.description;
  elements.parameterFields.innerHTML = definition.parameters.map((parameter, index) => `
    <label>
      <span>${escapeHtml(parameter.label)}</span>
      <input type="number" data-parameter-index="${index}" min="${parameter.minimum}" max="${parameter.maximum}" step="${parameter.decimalPlaces > 0 ? Math.pow(10, -parameter.decimalPlaces) : 1}"
             value="${selection.parameters[index] ?? ""}" placeholder="可留空">
    </label>`).join("");
  elements.indicatorDialog.close();
  elements.parameterDialog.returnValue = "";
  elements.parameterDialog.showModal();
}

function saveIndicatorParameters(event) {
  event.preventDefault();
  const selection = state.pendingIndicators.find(item => item.id === state.parameterTargetId);
  if (!selection) return;
  const values = [...elements.parameterFields.querySelectorAll("input")]
    .map(input => input.value.trim() === "" ? null : Number(input.value))
    .filter(value => value != null);
  if (values.length === 0 || values.some(value => !Number.isFinite(value))) {
    elements.parameterHint.textContent = "至少保留一个有效参数。";
    return;
  }
  selection.parameters = values;
  elements.parameterDialog.close();
  renderIndicatorLists();
  elements.indicatorDialog.showModal();
}

async function saveIndicators(event) {
  event.preventDefault();
  if (state.pendingIndicators.length === 0) {
    elements.indicatorSelectionCount.textContent = "至少保留一个指标。";
    return;
  }
  state.indicators = clone(state.pendingIndicators);
  persistProfile();
  updateProfileSummary();
  elements.indicatorDialog.close();
  if (state.result) await loadAnalysis();
}

function indicatorDefinition(code) {
  return state.catalog?.indicators.find(item => item.code === code);
}

function indicatorInstanceId(code, placement) {
  return `${String(code).toLowerCase()}-${String(placement).toLowerCase()}`;
}

function openStrategyDialog() {
  if (!state.catalog) {
    showMessage("策略目录尚未加载，请稍后重试。");
    return;
  }
  const strategies = availableStrategies();
  state.pendingStrategy = clone(state.strategy || strategies[0]);
  elements.strategySelect.innerHTML = strategies.map(item => `<option value="${escapeHtml(item.id)}" ${item.id === state.pendingStrategy?.id ? "selected" : ""}>${escapeHtml(item.name)}${item.isBuiltIn ? "（内置）" : "（我的）"}</option>`).join("");
  renderStrategyEditor();
  elements.strategyDialog.showModal();
}

function selectStrategyTemplate() {
  state.pendingStrategy = clone(availableStrategies().find(item => item.id === elements.strategySelect.value));
  renderStrategyEditor();
}

function renderStrategyEditor() {
  const strategy = state.pendingStrategy;
  if (!strategy) return;
  elements.strategyName.value = strategy.name;
  elements.entryMatchMode.value = strategy.entry.matchMode;
  elements.exitMatchMode.value = strategy.exit.matchMode;
  elements.strategyEditHint.textContent = strategy.isBuiltIn
    ? "内置策略保持只读；保存时会创建一个可继续修改的个人副本。"
    : "当前是个人策略，保存会更新本地副本。";
  elements.saveStrategyButton.textContent = strategy.isBuiltIn ? "保存为我的策略" : "保存修改";
  renderConditions(elements.entryConditions, strategy.entry.conditions, "entry");
  renderConditions(elements.exitConditions, strategy.exit.conditions, "exit");
  renderConditionPresetOptions();
}

function renderConditionPresetOptions() {
  const options = ["<option value=\"\">快捷添加：KDJ / OBV / BOLL…</option>"];
  for (const preset of state.catalog?.conditionPresets || [])
    options.push(`<option value="${escapeHtml(preset.id)}">${escapeHtml(preset.name)}</option>`);
  elements.strategyDialog.querySelectorAll("[data-condition-preset]").forEach(select => {
    select.innerHTML = options.join("");
  });
}

function renderConditions(container, conditions, side) {
  const references = strategyReferences();
  container.innerHTML = conditions.map((condition, index) => {
    const rightIsConstant = condition.constant != null || !condition.right;
    return `<div class="condition-row" data-side="${side}" data-index="${index}">
      <select data-role="left" aria-label="左侧指标">${referenceOptions(references, condition.left)}</select>
      <select data-role="comparison" aria-label="比较方式">${comparisonOptions(condition.comparison)}</select>
      <div class="condition-right">
        <select data-role="right" aria-label="右侧指标或常数">
          <option value="__constant__" ${rightIsConstant ? "selected" : ""}>常数</option>
          ${referenceOptions(references, condition.right, !rightIsConstant)}
        </select>
        <input data-role="constant" type="number" step="0.0001" value="${condition.constant ?? ""}" placeholder="数值" ${rightIsConstant ? "" : "hidden"}>
      </div>
      <button class="condition-remove" type="button" data-remove-condition aria-label="删除条件">删除</button>
    </div>`;
  }).join("");
}

function strategyReferences() {
  const references = [
    ["candle.open", "开盘价"], ["candle.high", "最高价"], ["candle.low", "最低价"],
    ["candle.close", "收盘价"], ["candle.volume", "成交量"], ["candle.turnover", "成交额"]
  ];
  for (const selection of strategyIndicatorSelections()) {
    const definition = indicatorDefinition(selection.code);
    if (!definition) continue;
    const outputs = selection.code === "VOL" || selection.code === "TUR"
      ? definition.outputs.slice(0, selection.parameters.length + 1)
      : ["MA", "EMA", "EXPMA", "RSI", "BIAS", "WR"].includes(selection.code)
        ? definition.outputs.slice(0, selection.parameters.length)
        : definition.outputs;
    outputs.forEach((output, index) => references.push([
      `${selection.id}.${output.key}`,
      `${selection.code} · ${indicatorOutputLabel(selection, output, index)}`
    ]));
  }
  return references;
}

function strategyIndicatorSelections() {
  const selections = [...state.indicators, ...(state.pendingStrategy?.indicators || [])];
  return selections.filter((selection, index) =>
    selections.findIndex(candidate => candidate.id === selection.id) === index);
}

function referencedIndicatorIds(strategy) {
  const ids = new Set();
  const conditions = [...strategy.entry.conditions, ...strategy.exit.conditions];
  for (const condition of conditions) {
    for (const reference of [condition.left, condition.right]) {
      if (!reference || reference.startsWith("candle.")) continue;
      const separator = reference.lastIndexOf(".");
      if (separator > 0) ids.add(reference.slice(0, separator));
    }
  }
  return ids;
}

function indicatorOutputLabel(selection, output, index) {
  if (["MA", "EMA", "EXPMA", "RSI", "BIAS", "WR"].includes(selection.code))
    return `${selection.code}${formatIndicatorParameter(selection.parameters[index])}`;
  if (selection.code === "VOL" || selection.code === "TUR") {
    if (index === 0) return selection.code;
    return `${selection.code === "VOL" ? "MAVOL" : "MATUR"}${formatIndicatorParameter(selection.parameters[index - 1])}`;
  }
  return output.label;
}

function formatIndicatorParameter(value) {
  return Number(value).toLocaleString("en-US", { maximumFractionDigits: 2, useGrouping: false });
}

function referenceOptions(references, selected, forceSelected = true) {
  return references.map(([value, label]) => `<option value="${escapeHtml(value)}" ${forceSelected && value === selected ? "selected" : ""}>${escapeHtml(label)}</option>`).join("");
}

function comparisonOptions(selected) {
  const options = [
    ["GreaterThan", ">"], ["LessThan", "<"], ["GreaterThanOrEqual", ">="],
    ["LessThanOrEqual", "<="], ["CrossAbove", "上穿"], ["CrossBelow", "下穿"]
  ];
  return options.map(([value, label]) => `<option value="${value}" ${value === selected ? "selected" : ""}>${label}</option>`).join("");
}

function handleStrategyClick(event) {
  const add = event.target.closest("[data-side]");
  if (add?.classList.contains("condition-add")) {
    const rules = add.dataset.side === "entry" ? state.pendingStrategy.entry : state.pendingStrategy.exit;
    if (rules.conditions.length >= 12) {
      elements.strategyEditHint.textContent = "每组最多 12 个条件。";
      return;
    }
    rules.conditions.push({ left: "candle.close", comparison: "GreaterThan", constant: 0 });
    renderStrategyEditor();
    return;
  }
  const remove = event.target.closest("[data-remove-condition]");
  if (!remove) return;
  const row = remove.closest(".condition-row");
  const rules = row.dataset.side === "entry" ? state.pendingStrategy.entry : state.pendingStrategy.exit;
  if (rules.conditions.length <= 1) {
    elements.strategyEditHint.textContent = "买入和卖出规则至少各保留一个条件。";
    return;
  }
  rules.conditions.splice(Number(row.dataset.index), 1);
  renderStrategyEditor();
}

function handleStrategyFieldChange(event) {
  if (!state.pendingStrategy) return;
  if (event.target.matches("[data-condition-preset]")) {
    addConditionPreset(event.target);
    return;
  }
  if (event.target === elements.strategyName) state.pendingStrategy.name = elements.strategyName.value;
  if (event.target === elements.entryMatchMode) state.pendingStrategy.entry.matchMode = elements.entryMatchMode.value;
  if (event.target === elements.exitMatchMode) state.pendingStrategy.exit.matchMode = elements.exitMatchMode.value;
  const row = event.target.closest(".condition-row");
  if (!row) return;
  const rules = row.dataset.side === "entry" ? state.pendingStrategy.entry : state.pendingStrategy.exit;
  const condition = rules.conditions[Number(row.dataset.index)];
  const role = event.target.dataset.role;
  if (role === "left") condition.left = event.target.value;
  if (role === "comparison") condition.comparison = event.target.value;
  if (role === "right") {
    const input = row.querySelector("[data-role=constant]");
    const constant = event.target.value === "__constant__";
    input.hidden = !constant;
    condition.right = constant ? null : event.target.value;
    condition.constant = constant ? Number(input.value || 0) : null;
  }
  if (role === "constant") condition.constant = event.target.value === "" ? null : Number(event.target.value);
}

function addConditionPreset(select) {
  const preset = state.catalog?.conditionPresets?.find(item => item.id === select.value);
  if (!preset) return;
  const rules = select.dataset.side === "entry" ? state.pendingStrategy.entry : state.pendingStrategy.exit;
  if (rules.conditions.length >= 12) {
    elements.strategyEditHint.textContent = "每组最多 12 个条件。";
    select.value = "";
    return;
  }

  const selections = strategyIndicatorSelections();
  const selectedIndicator = selections.find(item => item.code === preset.indicator.code) || clone(preset.indicator);
  const condition = clone(preset.condition);
  condition.left = remapIndicatorReference(condition.left, preset.indicator.id, selectedIndicator.id);
  condition.right = remapIndicatorReference(condition.right, preset.indicator.id, selectedIndicator.id);
  rules.conditions.push(condition);
  state.pendingStrategy.indicators = [...(state.pendingStrategy.indicators || []), selectedIndicator]
    .filter((selection, index, all) => all.findIndex(candidate => candidate.id === selection.id) === index);
  elements.strategyEditHint.textContent = `${preset.name}：${preset.description}`;
  renderConditions(elements.entryConditions, state.pendingStrategy.entry.conditions, "entry");
  renderConditions(elements.exitConditions, state.pendingStrategy.exit.conditions, "exit");
  select.value = "";
}

function remapIndicatorReference(reference, sourceId, targetId) {
  if (!reference || sourceId === targetId || !reference.startsWith(`${sourceId}.`)) return reference;
  return `${targetId}${reference.slice(sourceId.length)}`;
}

async function saveStrategy(event) {
  event.preventDefault();
  const strategy = clone(state.pendingStrategy);
  strategy.name = elements.strategyName.value.trim();
  if (!strategy.name) {
    elements.strategyEditHint.textContent = "策略名称不能为空。";
    return;
  }
  const invalidConstant = [...elements.strategyDialog.querySelectorAll("[data-role=constant]:not([hidden])")]
    .some(input => input.value.trim() === "" || !Number.isFinite(Number(input.value)));
  if (invalidConstant) {
    elements.strategyEditHint.textContent = "常数条件必须填写有效数值。";
    return;
  }
  strategy.entry.matchMode = elements.entryMatchMode.value;
  strategy.exit.matchMode = elements.exitMatchMode.value;
  const requiredIds = referencedIndicatorIds(strategy);
  const availableIndicators = strategyIndicatorSelections();
  const requiredIndicators = availableIndicators.filter(selection => requiredIds.has(selection.id));
  if (requiredIndicators.length !== requiredIds.size) {
    elements.strategyEditHint.textContent = "策略引用了尚未启用的指标，请先在指标设置中启用。";
    return;
  }
  const mergedIndicators = [...state.indicators, ...requiredIndicators].filter((selection, index, all) =>
    all.findIndex(candidate => candidate.id === selection.id) === index);
  if (mergedIndicators.length > 12) {
    elements.strategyEditHint.textContent = "策略所需指标会超过 12 个上限，请先减少图表指标。";
    return;
  }
  state.indicators = clone(mergedIndicators);
  strategy.indicators = clone(requiredIndicators);
  if (strategy.isBuiltIn) {
    strategy.sourceStrategyId = strategy.id;
    strategy.id = `user-${Date.now()}`;
    strategy.isBuiltIn = false;
  }
  const index = state.customStrategies.findIndex(item => item.id === strategy.id);
  if (index >= 0) state.customStrategies[index] = strategy;
  else state.customStrategies.push(strategy);
  state.strategy = strategy;
  persistProfile();
  updateProfileSummary();
  elements.strategyDialog.close();
  if (state.result) await loadAnalysis();
}

function renderQuality(quality) {
  const warnings = Array.isArray(quality?.warnings) ? quality.warnings : [];
  const isHealthy = quality?.isUsable && warnings.length === 0;
  elements.qualityPanel.className = `quality-panel ${isHealthy ? "healthy" : "warning"}`;
  elements.qualityTitle.textContent = isHealthy ? "数据质量检查通过" : "数据质量需要注意";
  if (isHealthy) {
    elements.qualityDetails.textContent = `${quality.validCandleCount} 根 K 线通过日期、OHLC、重复值和连续性检查。`;
    return;
  }

  const messages = warnings.map(warning => `${warning.message}${warning.count > 1 ? `（${warning.count} 处）` : ""}`);
  if (!quality?.isUsable) messages.unshift("有效 K 线数量不足，研究结果不可用。");
  elements.qualityDetails.replaceChildren(...messages.map(message => {
    const item = document.createElement("span");
    item.textContent = message;
    return item;
  }));
}

function renderExecutionCosts(profile) {
  const isComplete = Boolean(profile?.isComplete);
  elements.executionCostPanel.className = `cost-profile-panel ${isComplete ? "complete" : "incomplete"}`;
  elements.executionCostTitle.textContent = isComplete ? "执行成本已完整配置" : "执行成本存在缺失项";
  const policy = profile?.baselinePolicy || {};
  const missing = Array.isArray(profile?.missingInputs) && profile.missingInputs.length > 0
    ? `缺失：${profile.missingInputs.join("、")}`
    : "缺失：无";
  const details = [
    profile?.status || "成本状态不可用。",
    missing,
    `基准：卖出平台费 ${percent(Number(policy.platformFeeRate || 0) * 100)} · 价差 ${number(policy.spreadBps || 0)} bps · 滑点 ${number(policy.slippageBps || 0)} bps`,
    profile?.source || ""
  ].filter(Boolean);
  elements.executionCostDetails.replaceChildren(...details.map(message => {
    const item = document.createElement("span");
    item.textContent = message;
    return item;
  }));
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
  const pointCounts = Object.fromEntries([
    ["FirstBuy", "一买"], ["FirstSell", "一卖"],
    ["SecondBuy", "二买"], ["SecondSell", "二卖"],
    ["ThirdBuy", "三买"], ["ThirdSell", "三卖"]
  ].map(([type, label]) => [label, chan.signals.filter(signal => signal.chanType === type).length]));
  const stats = [
    ["分型", chan.fractals.length],
    ["笔", chan.strokes.length],
    ["线段", chan.segments.length],
    ["中枢", chan.centers.length],
    ...Object.entries(pointCounts)
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
  elements.backtestGrid.innerHTML = backtests.map(item => {
    const missingCosts = state.result?.executionCosts?.missingInputs || [];
    const feeConfigured = !missingCosts.some(item => item.includes("平台手续费"));
    const feeText = feeConfigured
      ? percent(item.policy.platformFeeRate * 100)
      : "未配置（按 0）";
    if (!item.isAvailable) {
      return `<section class="backtest-item unavailable">
        <h3>${escapeHtml(item.strategy)}</h3>
        <p class="backtest-status">${escapeHtml(item.status)}</p>
      </section>`;
    }

    const sensitivity = Array.isArray(item.costSensitivity)
      ? item.costSensitivity.map(scenario => `
        <div class="cost-scenario ${scenario.code === "baseline" ? "baseline" : ""}">
          <span>${escapeHtml(scenario.label)}</span>
          <strong>${percent(combinedReturnPercent(scenario.totalReturnPercent, scenario.unrealizedReturnPercent))}</strong>
          <small>成本影响 ${percent(-Number(scenario.costPercent))}</small>
        </div>`).join("")
      : "";
    return `<section class="backtest-item">
      <h3>${escapeHtml(item.strategy)}</h3>
      <p class="backtest-status">${escapeHtml(item.status)}</p>
      <div class="backtest-metrics">
        <div><span>已平仓</span><strong>${item.tradeCount}</strong></div>
        <div><span>已实现净收益</span><strong>${percent(item.totalReturnPercent)}</strong></div>
        <div><span>未实现净收益</span><strong>${percent(item.unrealizedReturnPercent)}</strong></div>
        <div><span>交易成本影响</span><strong>${percent(-item.costPercent)}</strong></div>
        <div><span>胜率</span><strong>${percent(item.winRatePercent)}</strong></div>
        <div><span>最大回撤</span><strong>${percent(item.maxDrawdownPercent)}</strong></div>
        <div><span>平均持有</span><strong>${item.averageHoldingDays.toFixed(1)} 天</strong></div>
        <div><span>T+${item.policy.tradeLockDays} 受阻信号</span><strong>${item.blockedExitCount}</strong></div>
      </div>
      <p class="backtest-policy">下一根开盘 · 点差 ${item.policy.spreadBps} bps · 滑点 ${item.policy.slippageBps} bps · 卖出平台费 ${feeText}</p>
      <div class="cost-sensitivity" aria-label="成本敏感性">${sensitivity}</div>
    </section>`;
  }).join("");
}

function renderResearchResultSummary(summary) {
  const value = summary || {};
  elements.resultSummaryStatus.textContent = value.status || "尚未生成结果摘要。";
  const cards = [
    ["可用策略", `${Number(value.availableStrategyCount || 0)} / ${Number(value.strategyCount || 0)}`, "完成有效回测"],
    ["已平仓交易", Number(value.tradeCount || 0), "全部可用策略合计"],
    ["最佳净收益", percent(value.bestNetReturnPercent || 0), escapeHtml(value.bestStrategy || "暂无")],
    ["加权胜率", percent(value.weightedWinRatePercent || 0), "按已平仓交易数加权"],
    ["最深回撤", percent(value.worstMaxDrawdownPercent || 0), "可用策略中的最差值"],
    ["平均持有", `${Number(value.averageHoldingDays || 0).toFixed(1)} 天`, "按交易及未平仓仓位加权"],
    ["成本压力", percent(value.worstCostScenarioReturnPercent || 0), "全部成本情景最低净收益"],
    ["样本外稳定", Number(value.stableStrategyCount || 0), "稳定候选策略数"]
  ];
  elements.resultSummaryGrid.innerHTML = cards.map(([label, metric, note]) => `
    <div class="result-summary-metric">
      <span>${label}</span>
      <strong>${metric}</strong>
      <small>${note}</small>
    </div>`).join("");
}

function renderWalkForward(report) {
  elements.walkForwardStatus.textContent = report?.status || "样本外验证不可用。";
  const strategies = Array.isArray(report?.strategies) ? report.strategies : [];
  elements.walkForwardGrid.innerHTML = strategies.map(strategy => {
    const windows = Array.isArray(strategy.windows) ? strategy.windows : [];
    const rows = windows.map(window => `
      <tr>
        <td>窗口 ${window.sequence}</td>
        <td>${escapeHtml(window.validationStartDate)} 至 ${escapeHtml(window.validationEndDate)}</td>
        <td>${percent(window.validation.netReturnPercent)}</td>
        <td>${escapeHtml(window.testStartDate)} 至 ${escapeHtml(window.testEndDate)}</td>
        <td>${percent(window.test.netReturnPercent)}</td>
        <td><span class="window-result ${window.passed ? "pass" : window.isEvaluated ? "fail" : "unknown"}">${window.passed ? "通过" : window.isEvaluated ? "未通过" : "不可判定"}</span></td>
      </tr>`).join("");
    return `<section class="walk-forward-item ${strategy.isStable ? "stable" : ""}">
      <header>
        <div><h3>${escapeHtml(strategy.strategy)}</h3><p>${escapeHtml(strategy.status)}</p></div>
        <strong>${strategy.isStable ? "稳定候选" : strategy.isAvailable ? "研究观察" : "不可判定"}</strong>
      </header>
      <div class="walk-forward-counts">已评估 ${strategy.evaluatedWindowCount} 个窗口 · 通过 ${strategy.passedWindowCount} 个</div>
      ${rows ? `<div class="walk-forward-table-wrap"><table><thead><tr><th>窗口</th><th>验证区间</th><th>验证净值</th><th>测试区间</th><th>测试净值</th><th>结果</th></tr></thead><tbody>${rows}</tbody></table></div>` : ""}
    </section>`;
  }).join("");
}

function renderSignalRows() {
  const side = elements.sideFilter.value;
  const query = elements.signalSearch.value.trim().toLocaleLowerCase("zh-CN");
  const rows = state.signals.filter(signal => {
    const sideMatches = side === "all" || signal.side === side;
    const textMatches = !query || `${signal.strategy} ${signal.reason} ${signal.category} ${signal.level}`.toLocaleLowerCase("zh-CN").includes(query);
    return sideMatches && textMatches;
  }).slice(0, 80);

  elements.signalRows.innerHTML = rows.map(signal => `
    <tr data-date="${escapeHtml(signal.date)}" tabindex="0" title="点击定位到图表日期">
      <td data-label="日期">${escapeHtml(signal.date)}${signal.availableDate !== signal.date ? `<br><small>确认 ${escapeHtml(signal.availableDate)}</small>` : ""}</td>
      <td data-label="策略"><strong>${escapeHtml(signal.chanType ? signal.level : signal.strategy)}</strong><br><small>${escapeHtml(signal.category)} · ${escapeHtml(signal.strategy)}</small></td>
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
  try {
    window.QuantChart.render({
      container: elements.chart,
      symbol: state.result.symbol,
      interval: state.result.interval,
      candles: state.result.candles,
      visibleCount: state.visibleCandles.length,
      chan: state.result.chan,
      signals: state.chartSignals,
      indicatorSeries: state.result.indicatorSeries || []
    });
    elements.chart.classList.remove("chart-error");
    elements.chart.removeAttribute("data-error");
  } catch (error) {
    elements.chart.classList.add("chart-error");
    elements.chart.dataset.error = error instanceof Error ? error.message : "图表加载失败。";
  }
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

function focusSignalOnChart(event) {
  const row = event.target.closest("tr[data-date]");
  if (!row) return;
  window.QuantChart?.focusDate(row.dataset.date);
  elements.chartWrap.scrollIntoView({ behavior: "smooth", block: "center" });
}

async function toggleChartFullscreen() {
  try {
    if (document.fullscreenElement === elements.chartWrap) await document.exitFullscreen();
    else await elements.chartWrap.requestFullscreen();
  } catch {
    showMessage("浏览器未允许图表进入全屏模式。");
  }
}

function updateFullscreenButton() {
  elements.chartFullscreenButton.textContent = document.fullscreenElement === elements.chartWrap
    ? "退出全屏"
    : "全屏图表";
  requestAnimationFrame(() => window.QuantChart?.resize());
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

function combinedReturnPercent(realizedPercent, unrealizedPercent) {
  const realizedMultiplier = 1 + Number(realizedPercent) / 100;
  const unrealizedMultiplier = 1 + Number(unrealizedPercent) / 100;
  return (realizedMultiplier * unrealizedMultiplier - 1) * 100;
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
