# Research：WindowsAppSDKSelfContained 自包含部署

## 来源

- Windows App SDK deployment guide for self-contained apps（Microsoft Learn，2026-05 更新）
  <https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps>

## 关键结论

1. **设置方式**：应用项目主 `PropertyGroup` 设置 `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>`；库项目不得设置。设置后 WinAppSDK Framework 包内容提取到生成输出目录，随应用分发。
2. **unpackaged 支持明确**：非打包（含外部位置打包）应用，运行时依赖复制到 `.exe` 旁，可 xcopy 部署或嵌入自定义安装器。
3. **.NET 前提**：需同时 .NET self-contained 发布（`SelfContained=true` + `RuntimeIdentifier`）才是完全自包含。
4. **Bootstrap 无需关心**：`WindowsAppSDKSelfContained=true` + `WindowsPackageType=None` + `OutputType` 为 Exe/WinExe 时，`WindowsAppSdkUndockedRegFreeWinRTInitialize` 默认为 true，自动初始化，无需 Bootstrap。
5. **已知限制**：依赖 Singleton 包的 API（`PushNotificationManager`、`AppNotificationManager`）在 self-contained 下需 `IsSupported()` 检测或额外部署 MSIX。本项目提醒使用系统原生 `ToastNotificationManager`，不在此列。
6. **命令行传参可行**：`dotnet publish -p:WindowsAppSDKSelfContained=true/false` 以全局属性传入，优先级高于 pubxml 内设置，可按发布变体分别控制，无需改 csproj。

## 项目现状核对（改动前）

- `WindBoard.csproj`：`WindowsPackageType=None`，未设 `WindowsAppSDKSelfContained`（默认框架依赖）。
- `.github/workflows/release.yml`：sc 变体 `--self-contained true`，fd 变体 `--self-contained false`，均未传 `WindowsAppSDKSelfContained`。
- Inno 安装器（`installer/WindBoard.iss`）：不含 Windows App Runtime 安装引导。
- 结论：sc 变体（便携版 zip + 推荐安装版）当前仅 .NET 自包含；升级 WinAppSDK NuGet 包后需用户机器装有匹配主版本 runtime，否则启动失败。
