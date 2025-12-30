# 设置持久化

WindBoard 使用 JSON 将设置持久化到用户目录，并通过事件将变更同步到 UI。

实现位置：

- `Services/Settings/SettingsService.cs`
- `Models/AppSettings.cs`

## 配置文件路径

- Windows：`%APPDATA%\\WindBoard\\settings.json`
- 首次启动：若文件不存在，会创建目录并写入默认配置。

## 当前设置项（AppSettings）

> 以 `Models/AppSettings.cs` 为准。

- 外观：
  - `BackgroundColorHex`
- 视频展台：
  - `VideoPresenterEnabled`
  - `VideoPresenterPath`
  - `VideoPresenterArgs`
- 伪装：
  - `CamouflageEnabled`
  - `CamouflageTitle`
  - `CamouflageSourcePath`
  - `CamouflageIconCachePath`
- 书写相关：
  - `StrokeThicknessConsistencyEnabled`
  - `SimulatedPressureEnabled`
- 触摸手势：
  - `ZoomPanTwoFingerOnly`
- 高级平滑参数：
  - `CustomSmoothingEnabled`
  - `SmoothingWarningDismissed`
  - `SmoothingPenStepMm` / `SmoothingPenEpsilonMm` / `SmoothingPenFcMin` / `SmoothingPenBeta` / `SmoothingPenDCutoff`
  - `SmoothingFingerStepMm` / `SmoothingFingerEpsilonMm` / `SmoothingFingerFcMin` / `SmoothingFingerBeta` / `SmoothingFingerDCutoff`

## 新增设置项的建议流程

1. 在 `Models/AppSettings.cs` 添加属性并给出合理默认值。
2. 在 `Services/Settings/SettingsService.cs` 增加 `GetXxx/SetXxx`（或复用现有读取方式），并在需要时触发 `SettingsChanged`。
3. 在 UI（通常是 `Views/SettingsWindow.*`）中增加控件/绑定，必要时在 `MainWindow` 初始化阶段读取设置快照并应用到对应服务/模式。
4. 为关键行为补充单元测试（如果该设置影响 Core/Services 中的可测逻辑）。

