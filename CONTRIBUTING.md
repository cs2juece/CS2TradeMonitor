# 贡献与反馈

公开仓库是从私有权威仓库单向生成的可构建源码快照，不是日常开发源。

- Bug 和功能建议请使用仓库的 Issue 模板。
- 请勿在 Issue、PR、日志或截图中提交 token、Cookie、登录状态、设备标识、订单号、HAR、数据库或真实用户数据。
- 公开 PR 不会直接合并回私有仓库；维护者可在明确选择、来源与许可证审查完成后，在私有仓库重新实现建议。
- 公开源码可使用 Windows x64、Visual Studio C++ 工具链和 .NET 10 SDK 构建。

```powershell
dotnet restore .\CS2TradeMonitor.sln
dotnet build .\CS2TradeMonitor.sln -c Debug
dotnet build .\CS2TradeMonitor.sln -c Release
.\Build-PublicRelease.ps1 -Configuration Release -Runtime win-x64
```

安全漏洞请按 `SECURITY.md` 私下报告，不要提交公开 Issue。
