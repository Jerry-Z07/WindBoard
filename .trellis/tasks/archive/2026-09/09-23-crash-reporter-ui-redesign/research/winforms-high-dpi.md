# 研究：WinForms 高 DPI 缩放与布局分配规则

> 研究目的：确定 `WindBoard.CrashReporter`（WinForms）在 150% 缩放下布局错乱的根因，并给出官方依据正确的修复方案。
> 研究日期：2026-09-23

---

## 结论速览

1. **根因**：工程未声明 `AutoScaleMode`，而 .NET 8 起在 `PerMonitorV2`（以及本工程当前等效的 `SystemAware` 路径）下，**顶级窗口的缩放由 `AutoScaleMode` 决定**。未设置时窗口不参与自动缩放，而字体（pt 为物理单位）仍随 DPI 渲染放大 → 控件像素尺寸不变、文字变大 → 文本溢出与换行膨胀。
2. **修复一（进程级）**：在 `WindBoard.CrashReporter.csproj` 设置 `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>`。官方推荐用项目文件配置，**不要**用 `app.manifest`（会触发编译器警告 WFO0003 并可能与应用配置冲突）。
3. **修复二（窗体级）**：显式设置 `AutoScaleMode = AutoScaleMode.None`，并对所有固定像素调用 `LogicalToDeviceUnits` 自管缩放（§7 实测：`Dpi` / `Font` 模式在本场景下缩放因子恒为 1，不可用）。顶层 `Form` **不要**使用 `AutoScaleMode.Inherit`（无父容器，语义悬空）。
4. **修复三（布局级）**：`TableLayoutPanel` 的分配顺序是 `Absolute → AutoSize → Percent`，Percent 行只拿剩余空间且**空间不足时内容被剪裁**。因此可换行文本绝不能放在 `AutoSize` 行（内容膨胀会直接吃掉 Percent 行的空间）。

---

## 一、DPR 感知模式与配置方式（官方）

来源：<https://learn.microsoft.com/zh-cn/dotnet/desktop/winforms/forms/autoscale>（最后更新 2026-08-19）

- `ApplicationHighDpiMode` **默认值为 `SystemAware`**，共 5 个可选值：`SystemAware` / `PerMonitor` / `PerMonitorV2` / `DpiUnaware` / `DpiUnawareGdiScaled`。
- 官方推荐通过**项目文件**配置：

  ```xml
  <PropertyGroup>
    <ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>
  </PropertyGroup>
  ```

- 官方明确**不建议**使用 `app.manifest` 配置 DPI：它会与应用配置冲突，并触发编译器警告 **WFO0003**。
- 官方警告：**不支持任意混用 DPI 与字体缩放模式**。基窗体与派生窗体使用不同模式会导致意外结果（本工程为独立单窗体，无派生窗口，不触发该风险）。

来源：<https://learn.microsoft.com/zh-cn/dotnet/desktop/winforms/automatic-scaling-in-windows-forms>（最后更新 2026-07-27）

- `AutoScaleMode` 取值：`None`（不缩放）/ `Font`（按字体相对比例缩放）/ `Dpi`（按分辨率缩放）/ `Inherit`（从父容器继承）。
- 顶层 `Form` 应**显式**设为 `Font` 或 `Dpi`；`Inherit` 应留给子容器与 `UserControl`。
- 自动缩放运行机制：设计时记录 `AutoScaleMode` + `AutoScaleDimensions`；运行时用 `CurrentAutoScaleDimensions` 计算 `AutoScaleFactor`，不一致则调用 `PerformAutoScale`，完成后**更新 `AutoScaleDimensions` 以避免渐进式重复缩放**。
- `PerformAutoScale` 的其他触发时机：`Font` 模式下响应 `OnFontChanged`；父 `ContainerControl` 缩放时（**每个容器用自身比例因子缩放其子控件**，而非沿用父容器因子）。

## 二、与本任务直接相关的官方破坏性变更（.NET 8）

来源：<https://learn.microsoft.com/zh-cn/dotnet/core/compatibility/windows-forms/8.0/top-level-window-scaling>（最后更新 2025-06-19，引入版本 .NET 8 预览版 1）

| 对比项 | 变更前 | 变更后（.NET 8+） |
|---|---|---|
| 缩放依据 | Windows 按**线性** DPI 比例缩放顶级窗口，**忽略** `AutoScaleMode` | 顶级窗口**根据 `AutoScaleMode`** 缩放 |
| 一致性 | 顶级窗体与其子控件缩放**不一致** | 顶级窗口与子控件**保持一致** |
| 机制 | 不处理 `WM_GETDPISCALEDSIZE` | 顶级对象处理 `WM_GETDPISCALEDSIZE` |

- 变更动机：`AutoScaleMode.Font` 要求窗体的缩放是**非线性**的（取决于分配给窗体/子控件的字体），旧行为导致顶层窗体与子控件比例不一致。
- 官方建议措施：**无需执行任何操作**（属于修正一致性的行为变更）。

**对本工程的直接推论**：本工程当前未设置 `AutoScaleMode`（即等效 `None`）。在 .NET 8+ 的新行为下，顶级窗口不再被强制线性缩放，而子控件本身也未配置自动缩放 → 窗口与内容停留在 96 DPI 逻辑尺寸，而 pt 字号随 DPI 变大 → 必然错位。这是本次显示问题的根因。

## 三、TableLayoutPanel 的空间分配规则（官方）

来源：<https://learn.microsoft.com/zh-cn/dotnet/desktop/winforms/controls/autosize-behavior-in-the-tablelayoutpanel-control>（最后更新 2025-05-07）

`AutoSize = false` 时的分配顺序：

1. `SizeType = Absolute` → 分配 `RowStyle.Height` / `ColumnStyle.Width` 指定的**像素值**；
2. `SizeType = AutoSize` → 分配子控件 `GetPreferredSize` 返回的**像素值**（由内容决定）；
3. 上述分配完成后，**剩余空间**才由 `SizeType = Percent` 按比例分配。

关键推论：

- Percent 行/列拿到的是"剩余空间"，**空间不足时其内容会被剪裁**，不会反过来挤占 Absolute / AutoSize 行。
- `AutoSize = true` 时，Percent 列/行会获得"内容不被裁剪"的扩展能力——代价是控件整体尺寸由内容驱动扩张。

> 官方页面**未涉及** `Visible = false` 的控件对行高分配的影响；该情形属于通用布局行为，本设计通过"不依赖隐藏控件所在行的可见性切换"来规避（详见 `design.md`）。

**对本工程的直接推论**：原实现把三行手工换行的「建议操作」`Label`（`AutoSize = true`）放在 `AutoSize` 行里。150% 缩放下文字变大 → 该 `Label` 首选尺寸膨胀 → `AutoSize` 行先拿走大量高度 → `Percent` 行（摘要 35% / 详情 65%）只能分到残余的几十像素。这精确对应截图 1 中"摘要框只有两行高"的现象。

## 四、.NET 8 其他相关改进

来源：<https://learn.microsoft.com/zh-cn/dotnet/desktop/winforms/whats-new/net80>（最后更新 2026-02-10）

- `PerMonitorV2` 下**嵌套控件**按正确比例缩放（例如 TabPage → Panel → Button）。
- `Form.MaximumSize` / `Form.MinimumSize` 按**当前监视器 DPI** 缩放，**从 .NET 8 起默认启用**；如需还原旧行为，需在 `runtimeOptions.configProperties` 中设置 `System.Windows.Forms.ScaleTopLevelFormMinMaxSizeForDpi = false`（本工程**不**需要，保留默认即可）。实测（§7 第 6 条）：按 `LogicalToDeviceUnits(780x540)` 赋值的 `MinimumSize` 运行时仍为 1170x810，**没有**二次放大，故不需要为规避双重缩放而回退换算。
- 设计器相关：`ForceDesignerDPIUnaware` 仅影响 VS 设计器，不改变应用运行行为（本工程为纯代码布局，不使用设计器，无需设置）。

## 五、本工程采用的配置（决策）

> 本节表格已按 §7 的实测结论修订（初稿的「`AutoScaleMode.Dpi` + `AutoScaleDimensions` 96 DPI 基线」方案已被实测证伪）。

| 层级 | 配置 | 依据 |
|---|---|---|
| 进程级 | `ApplicationHighDpiMode = PerMonitorV2` | 官方推荐的现代配置；跨不同 DPI 显示器移动时清晰 |
| 窗体级 | `AutoScaleMode = AutoScaleMode.None`，由本类对固定像素调用 `LogicalToDeviceUnits` 自管缩放 | `Dpi` / `Font` 模式下 `AutoScaleDimensions` 会被框架归一化到当前 DPI，缩放因子恒为 1（§7），自动缩放不可用 |
| 布局级 | 恒定尺寸用 `Absolute` 逻辑值；唯一自适应区用 `Percent`；**可换行文本一律不放在 `AutoSize` 行** | TableLayoutPanel 分配规则（见上） |
| 尺寸级 | `MinimumSize` / `ClientSize` / `Padding` / `Margin` / `Absolute` 行高按 96 DPI 逻辑值书写，赋值前经 `LogicalToDeviceUnits` 换算；由 `Dock` / `AutoSize` 决定的尺寸不换算 | 硬编码像素不会自动放大（§7 第 2 条）；字号用 pt，由 GDI+ 按设备 DPI 解析 |

## 六、待实现阶段实测确认的点

以下无法仅凭文档确证，已在实现阶段通过实测或代码探查核实（结论见 §7）：

1. `AutoScaleMode.Dpi` + 显式 `AutoScaleDimensions = new SizeF(96F, 96F)` 在 150% 下的实际 `AutoScaleFactor` 是否为 1.5 —— **否**，恒为 1（§7 第 1 条），该方案已废弃。
2. `ApplicationHighDpiMode = PerMonitorV2` 是否被 `ApplicationConfiguration.Initialize()` 的源生成器正确写入 `Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)` —— **是**（§7 第 5 条）。
3. `GroupBox` 在高 DPI 下标题与内边距的表现 —— 摘要区 4 行完整可见（§7）。

---

## 七、实测结论（2026-09-23，本机 2K 屏 / 系统 150%）

用进程内探针（`ApplicationHighDpiMode = PerMonitorV2`，`GetDpiForSystem() = 144`）创建 `Form`，对比不同配置下「设定 900x640 后的实际客户区尺寸」：

| 配置 | `AutoScaleDimensions` | `CurrentAutoScaleDimensions` | `ClientSize` |
|---|---|---|---|
| `Dpi` + `(96,96)` + 直接赋 `new Size(900,640)` | 144x144 | 144x144 | 900x640（**未放大**） |
| `Dpi` + 不设基线 + 直接赋 `new Size(900,640)` | 144x144 | 144x144 | 900x640（**未放大**） |
| `Font` + `(6,13)` + 直接赋 `new Size(900,640)` | 11x24 | 11x24 | 900x640（**未放大**） |
| `Font` + 不设基线 + 直接赋 `new Size(900,640)` | 11x24 | 11x24 | 900x640（**未放大**） |
| `Dpi` + `(96,96)` + `LogicalToDeviceUnits(new Size(900,640))` | 144x144 | 144x144 | **1350x960** |
| `None` + `LogicalToDeviceUnits(new Size(900,640))` | 0x0 | 0x0 | **1350x960** |

**结论**：

1. WinForms 会把 `AutoScaleDimensions` **归一化到当前 DPI**（`Dpi` 模式 → 当前 DPI 值；`Font` 模式 → 当前字体度量），使 `AutoScaleDimensions == CurrentAutoScaleDimensions` 恒成立，`PerformAutoScale` 的缩放因子恒为 **1**。**纯代码 Form 无法通过设置 `AutoScaleDimensions` 触发初始 DPI 缩放**——这推翻了「显式写出 96 DPI 基线即可放大」的初始假设。
2. 硬编码像素值（`ClientSize` / `MinimumSize` / `Padding` / 固定行高）**不会自动放大**。要让窗口在高 DPI 下按逻辑尺寸呈现，必须显式调用 `LogicalToDeviceUnits`。
3. 由 `AutoSize` + pt 字体决定的控件尺寸**本来就 DPI 正确**（9pt 字体在 144 DPI 下高 23px；两种配置下按钮均为 128x34）。因此只有「固定像素」需要换算。
4. 正确策略：**自管缩放** —— `AutoScaleMode.None` + 所有固定像素经 `LogicalToDeviceUnits` 换算，其余交给 `Dock` / `AutoSize` 自适应布局。
5. `ApplicationConfiguration.Initialize()` 的源生成器**确实**写入了 `Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)`（用 `-p:EmitCompilerGeneratedFiles=true` 探查确认），DPI 感知本身没有问题。
6. **最终形态实测**（进程内探针 + exe 级 `GetClientRect` 双口径一致）：`AutoScaleMode.None` + `ClientSize = LogicalToDeviceUnits(900x640)` 下客户区为 **1350x960**；`MinimumSize = LogicalToDeviceUnits(780x540)` 为 **1170x810**（**未**被框架二次放大）；根 `Padding` 为 18px，摘要 `GroupBox` 高 186px、报告 `GroupBox` 高 575px，四个按钮单行完整显示。
