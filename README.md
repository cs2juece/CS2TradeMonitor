# 📈 CS2 Trade Monitor / CS2 交易监控

[![Release](https://img.shields.io/github/v/release/cs2juece/CS2TradeMonitor?style=flat-square&label=Release)](https://github.com/cs2juece/CS2TradeMonitor/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/cs2juece/CS2TradeMonitor/total?style=flat-square&label=Downloads)](https://github.com/cs2juece/CS2TradeMonitor/releases)
[![License](https://img.shields.io/github/license/cs2juece/CS2TradeMonitor?style=flat-square&label=License)](LICENSE)

> 面向中文 CS2 饰品玩家的轻量、本地优先 Windows 桌面监控工具。
>
> 聚合 Steam、悠悠有品、QAQ 与 SteamDT，让交易数据和自动报价触手可及。

CS2 Trade Monitor 基于 .NET 10 WinForms 与 AntdUI 构建，强调轻量常驻、本地优先和明确确认。它是交易辅助工具，不代替用户判断，也不承诺任何收益。

## 快速开始

1. 优先前往 [Gitee Releases](https://gitee.com/cs2juece/CS2TradeMonitor/releases) 下载 `CS2TradeMonitor_v{version}-win-x64.zip`；Gitee 不可用时使用 [GitHub Releases](https://github.com/cs2juece/CS2TradeMonitor/releases/latest)。
2. 将 ZIP **完整解压**到当前用户可写的固定目录，例如 `D:\Apps\CS2TradeMonitor`。
3. 双击解压目录最外层的 `CS2TradeMonitor.exe`。
4. 如果电脑缺少 `.NET 10 Desktop Runtime x64`，启动器会显示中文确认界面；同意后会通过微软官方 HTTPS 地址下载安装，并在成功检测到运行环境后继续启动。

正常使用不需要管理员权限；只有首次安装运行环境时，Windows 才会请求管理员权限。不要直接在 ZIP 预览窗口中运行，也不要单独启动 `app`、`resources` 或 `runtimes` 目录里的文件。

> 本项目提供便携 ZIP，不提供传统安装器或卸载器。程序需要在自身目录创建和修改 `user-data`，因此不要解压到当前用户无法写入的位置。

## 功能概览

### 🖥️ 沉浸式桌面监控

通过任务栏常驻与桌面悬浮窗持续展示 QAQ / SteamDT 指数、单品价格和库存涨跌。字体、颜色、间距、位置及显示方式均可配置，并支持搜索添加饰品与自定义监控条件。

### 🛒 全链路交易处理

**Steam**：管理令牌、登录状态和报价列表，展示报价安全分类、物品明细与风险提示；支持单条或批量处理，并可配置二次确认提醒。

**悠悠有品**：同步登录与库存状态，处理报价和包租公发货配置；提供租赁自动改价与库存自动出租，但相关自动化默认关闭。

### 💰 风控与提醒

按整库或指定饰品设置止损、止盈**提醒阈值**，并按 QAQ / SteamDT 大盘点位、单品价格或涨跌幅规则触发桌面、托盘或手机提醒。手机通知可通过用户配置的第三方推送服务发送，同时支持 CS2 更新和模块健康状态检测。

### 📊 盈亏统计

根据悠悠相关历史成交记录汇总单品盈亏（吃米 / 亏米）与交易明细，帮助用户复盘已有数据；统计结果仅供参考，不构成交易或投资建议。

### 📈 本机量化研究

“量化研究”页面可启动随安装包提供的本机只读网页服务，并查看日 K、均线、MACD、精简缠论结构、候选价格信号与 CSV 导出。服务默认只监听 `127.0.0.1:5078`，不读取交易账户，也不执行交易；若电脑缺少 Microsoft ASP.NET Core Runtime 10（x64），页面会显示中文说明并可打开微软官方下载地址。

## 🛡️ 安全与隐私承诺

### 1. 凭据与本地数据

Steam 与悠悠凭据使用 Windows DPAPI 按当前 Windows 用户加密，并保存在软件目录的 `user-data` 中，避免凭据以明文形式落盘。**软件不会自动上传凭据文件、`user-data`、运行日志或诊断包。**

所有设置、凭据、历史、日志、缓存和更新文件均保存在当前软件目录的 `user-data` 中。便携版本不会读取、迁移、合并或删除旧 `%LocalAppData%\CS2TradeMonitor` / `%LocalAppData%\CS2DesktopMonitor` 数据。复制整个软件目录也会复制加密凭据、日志和悠悠设备档案，请勿上传、公开或交给不可信的人；跨用户或跨电脑复制后可能无法解密。

### 2. 交易与自动化风险（重点）

> **如果不了解自动交易会执行哪些真实操作，或不清楚各项开关、处理范围和确认流程，请保持所有自动交易、自动改价及自动出租功能关闭。**

自动交易、悠悠自动改价和库存自动出租默认关闭。开启后即代表持续授权，软件可能在后台自动接收匹配报价、发送悠悠出售或租赁报价、完成匹配的 Steam 手机确认、批量改价或自动上架，不再逐件询问。启用前必须确认账号、处理范围、逐件或同款选择、白名单或黑名单、价格规则及冷却时段；不确定时只使用查看和扫描功能。

Steam 单条同意和一键同意默认会显示二次确认，但允许选择以后跳过。“一键同意所有报价”可能包含我方转出物品的报价，操作前必须核对“收到”和“失去”的物品。只要相关自动交易规则仍然开启，24 小时内未完成的手机确认可能在软件重启后继续恢复和重试；切换账号、准备人工处理或不再需要自动化时，应先关闭相关规则并核对待处理记录。

网络异常、第三方接口变化、账号状态、平台规则或配置错误都可能影响自动化结果。本工具不能代替最终核验，交易与虚拟资产风险由使用者自行判断和承担。

> **责任声明：本工具仅为数据聚合与自动化辅助软件。因用户个人配置错误（如一键误同意转出报价、止损阈值设置错误）造成的财产损失，本项目概不负责。**

### 3. 网络、推送与诊断

应用更新会强制校验安装包大小和 SHA256；缺少 .NET 10 Desktop Runtime 时，启动器只允许通过微软官方 HTTPS 地址下载。正式版本只应从项目公开仓库的 Gitee / GitHub Releases 获取，请勿使用来源不明的修改包或镜像版。

软件不会在后台上传本地隐私文件。使用 Steam、悠悠有品、QAQ、SteamDT 等联网功能时，会向对应平台发送完成该功能所需的请求；启用手机提醒后，通知标题和正文会发送到用户配置的第三方推送服务或自定义 Webhook。请只使用可信的账号、通道和服务器，不要发送不希望交给第三方的信息。

详细诊断默认关闭，只有用户确认后才会开启。单次记录最多持续 48 小时，每个目录实例最多占用 200 MB，结束的会话最长保留 7 天；网络正文会先脱敏，无法确认安全的内容不会保留。诊断包只能由用户手动导出，软件不会自动上传或发送；分享 ZIP 前仍应自行检查内容和接收方。

## 更新机制

正式发布由公开仓库的 Gitee 与 GitHub Releases 同批提供。应用优先读取 Gitee 国内清单和安装包，连接失败时自动尝试 GitHub，并在替换文件前校验大小和 SHA256。

两个官方来源必须提供同一版本、大小和 SHA256；请勿从其他镜像、网盘或非项目官方来源下载安装包。

同一个物理目录只运行一个实例。如需多开，可复制完整软件目录，再分别启动各目录最外层的 `CS2TradeMonitor.exe`；不同目录的配置和数据彼此隔离。

## 开发与验证

开发环境：

- Windows 10/11 x64
- .NET 10 SDK
- Visual Studio 2026，或可运行 .NET SDK 的命令行环境

恢复、格式检查、构建和测试：

```powershell
dotnet restore .\CS2TradeMonitor.sln
dotnet format .\CS2TradeMonitor.sln --verify-no-changes
dotnet build .\CS2TradeMonitor.sln -c Debug
dotnet build .\CS2TradeMonitor.sln -c Release
dotnet test .\CS2TradeMonitor.sln -c Release --no-build
.\scripts\Test-CodeCoverage.ps1
dotnet list .\CS2TradeMonitor.sln package --vulnerable --include-transitive
.\tools\Check-CodeHygiene.ps1
```

生成发布包：

```powershell
.\Publish-RunTest.ps1 -Configuration Release -Runtime win-x64
```

本机部署验证：

```powershell
.\deploy-and-run.ps1
```

## 项目结构

```text
CS2TradeMonitor.sln
CS2TradeMonitor.csproj
CS2TradeMonitor.Bootstrapper/
CS2TradeMonitor.Updater/
CS2MarketData.Core/
CS2QuantWeb.Core/
CS2QuantWeb/
CS2TradeMonitor.Tests/
src/
  Application/
  Core/
  System/
  UI/
resources/
scripts/
docs/
```

- `src/Core`：程序入口、模块、运行状态和核心调度。
- `src/Application`：Steam、悠悠等应用层服务和业务投影。
- `src/System`：历史服务、系统集成和兼容逻辑。
- `src/UI`：WinForms / AntdUI 页面、控件和设置中心。
- `CS2MarketData.Core`：桌面端与量化网页共享的饰品目录检索、SteamDT K 线契约和解析核心。
- `CS2QuantWeb.Core`：无账户状态的量化计算与结构分析核心。
- `CS2QuantWeb`：随安装包分发、默认仅监听本机回环地址的 ASP.NET Core 网页服务。
- `CS2TradeMonitor.Tests`：xUnit 测试。

更完整的分层和维护边界见 [架构文档](docs/ARCHITECTURE.md)。

## 反馈与贡献

使用咨询或问题反馈请加入 [【CS2交易监控】官方群](https://qm.qq.com/q/K2AuJdMxG)，群号 `1057043823`。代码和文档贡献可通过 Issue 或 Pull Request 提交。

## 许可证与第三方组件

本项目采用 **GNU General Public License v3.0 only**（SPDX：`GPL-3.0-only`）授权。您可以使用、研究、修改和分发本项目；分发修改版或衍生作品时，必须继续遵守 GPL-3.0-only 并提供对应源码。完整条款见 [LICENSE](LICENSE)。

主要第三方组件：

- Diorser/LiteMonitor：参考其轻量发布方式和部分实现思路，仓库与发布包不包含其源码或资源文件。
- AntdUI：Apache License 2.0。
- Microsoft.Extensions：MIT License。
- Microsoft.Web.WebView2：Microsoft WebView2 SDK 许可。
- xUnit / coverlet：测试与覆盖率工具。

第三方版权、来源和许可文本见 `THIRD_PARTY_NOTICES.txt`。

### 鸣谢

感谢 DT 站长星辰、QAQ 站长棒棒糖提供数据生态支持：[SteamDT 主页](https://www.steamdt.com) · [QAQ 主页](https://csqaq.com)。

部分接口的思路受到 [Steamauto/Steamauto](https://github.com/Steamauto/Steamauto) 启发。

同时感谢 [Diorser/LiteMonitor](https://github.com/Diorser/LiteMonitor) 为轻量桌面监控与便携发布方向带来的启发；感谢 [AntdUI](https://github.com/AntdUI/AntdUI)、Microsoft .NET、Microsoft.Extensions 与 WebView2 为界面和运行基础提供支持；感谢 xUnit 与 coverlet 为项目的测试和质量验证提供工具。

也感谢每一位参与试用、反馈问题和提出建议的用户。你们的真实使用体验帮助项目不断完善。

## 免责声明

<details>
<summary>展开阅读全文</summary>

CS2 Trade Monitor 是独立开源项目，与 Valve、Steam、Counter-Strike 2、悠悠有品、SteamDT、QAQ 等平台或服务没有官方从属关系。

本项目仅以学习、技术研究和个人自用为目的提供，不提供任何交易、投资、资产管理、数据服务或商业服务承诺。使用者不得将本工具或其衍生内容用于任何违法、欺诈、侵权、规避平台规则、破坏系统安全、侵犯隐私或其他不道德行为。商业使用、再分发和修改的权利及条件以仓库中的 `LICENSE` 和适用法律为准；本免责声明不改变 `LICENSE` 已授予的权利。

本工具按“现状”和“可用性”提供。作者、贡献者及发布者不就本工具的安全性、完整性、可靠性、可用性、连续性、及时性、准确性、有效性、正确性、适用性、兼容性、无错误、无病毒或不侵权作出任何明示、默示或法定保证，包括但不限于适销性、特定用途适用性和不侵权保证。

本工具依赖第三方平台、公开或非官方接口、网络服务及本地运行环境；它们可能随时变更接口、数据、访问规则、风控策略或服务状态，并可能限制访问、暂停账号、拒绝交易或禁止自动化行为。使用者必须自行确认并持续遵守 Steam、悠悠有品及其他相关平台和数据服务的最新条款、规则、许可和适用法律。使用者与任何第三方之间的账户、交易、数据、内容、争议或损失均由使用者自行处理，本项目与相关第三方不存在代理、合作、授权或背书关系。

本工具展示的数据可能延迟、不完整、错误或失效，且不构成投资、交易、法律、税务、财务、资产管理或其他专业建议。自动报价、自动确认、发货、库存转移、提醒、止损/止盈及其他自动化或半自动化功能，可能因配置错误、接口变化、账号限制、网络异常、软件缺陷、系统环境、第三方服务或人为操作造成误操作、交易失败、账号处罚、数据丢失、虚拟资产损失或其他损害；使用者应自行评估风险、核验结果并承担全部后果。

在适用法律允许的最大范围内，作者、贡献者、发布者及其关联方不对因下载、安装、运行、无法运行、使用、无法使用、误用或滥用本工具及其衍生内容而产生的任何直接、间接、附带、特殊、惩罚性、后果性或其他损失、责任、索赔、要求、费用或诉讼承担责任，无论该等责任基于合同、侵权、过失、严格责任或其他理论，也无论是否已被告知可能发生此类损失。

作者保留在不事先通知且不承担任何义务的情况下，随时修改、更新、暂停、删除、终止本工具、其功能、文档、发布包或支持渠道的权利。作者不保证旧版本继续可用、兼容或获得维护。

使用者下载、安装、运行、访问或使用本工具，即表示其已阅读、理解并同意本免责声明及 `LICENSE` 的全部内容；如不同意其中任何内容，应立即停止使用本工具并删除已获取的相关副本。若本免责声明的任何条款被认定为无效或不可执行，不影响其余条款的效力；本免责声明不排除或限制适用法律禁止排除或限制的责任。

</details>
