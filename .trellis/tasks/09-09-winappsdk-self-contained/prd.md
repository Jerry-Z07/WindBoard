# 为 self-contained 变体启用 WindowsAppSDKSelfContained 运行时自包含

## Goal

在 CI 发布管线中为 sc 变体（便携版 zip 与推荐安装版）启用 `WindowsAppSDKSelfContained=true`，使 WinAppSDK 运行时随应用分发；fd 变体显式保持框架依赖。解除 WinAppSDK NuGet 包升级后对用户机器已安装 runtime 版本的依赖。

## Background

- 本项目为 unpackaged 应用（`WindowsPackageType=None`），CI（`.github/workflows/release.yml`）未设置 `WindowsAppSDKSelfContained`。
- 现状：sc 变体仅 .NET 运行时自包含，WinAppSDK 运行时仍依赖机器上已安装的 Windows App Runtime（Bootstrap 按主版本匹配查找）；Inno 安装器不安装该 runtime。
- 风险：升级 `Microsoft.WindowsAppSDK` NuGet 包后，用户机器上已装的旧版 runtime 不匹配，应用启动即失败。
- 官方依据：<https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps>（unpackaged + `WindowsAppSDKSelfContained=true` 支持随应用分发；`WindowsPackageType=None` + `WinExe` 下 UndockedRegFreeWinRT 自动初始化，无需 Bootstrap）。
- 功能限制核查：本项目未使用依赖 Singleton 包的 API（`AppNotificationManager`/`PushNotificationManager`），提醒通道使用系统原生 `ToastNotificationManager`，不受 self-contained 限制影响。

## Requirements

- 仅修改 `.github/workflows/release.yml`，每架构循环内两处 publish：
  - sc 变体主程序 `dotnet publish` 增加 `-p:WindowsAppSDKSelfContained=true`；
  - fd 变体主程序 `dotnet publish` 增加 `-p:WindowsAppSDKSelfContained=false`（显式声明，语义清晰）。
- 不改动 `WindBoard.csproj`、发布配置文件（pubxml）、安装器脚本（`.iss`）：开发期构建保持框架依赖默认，发布形态由管线显式控制（命令行全局属性优先于 pubxml）。
- Launcher（Native AOT）、CrashReporter（WinForms，不依赖 WinAppSDK）的 publish 命令不变。
- 发布资产命名与 latest.json 资产清单结构不变。

## Acceptance Criteria

- [ ] sc publish 命令包含 `-p:WindowsAppSDKSelfContained=true`；fd publish 命令包含 `-p:WindowsAppSDKSelfContained=false`。
- [ ] 本地抽查验证：以 sc 参数执行一次 `dotnet publish`（如 win-x64），输出目录包含 WinAppSDK 运行时文件（如 `Microsoft.WindowsAppRuntime.Bootstrap.dll` 等）；fd 参数 publish 的输出目录不包含。
- [ ] `release.yml` YAML 语法有效。
- [ ] 未改动 csproj、pubxml、安装器脚本。

## Notes

- `WindowsAppSDKSelfContained` 会增大 sc 变体体积约几十 MB，符合"带运行库"变体的用户预期，可接受。
- fd 变体升级 WinAppSDK 包后仍需用户机器装有匹配主版本 runtime，属该变体既有定义；必要时在发布说明中提示。
