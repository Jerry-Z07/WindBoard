# WindBoard

> [!WARNING]
> 本项目完全使用AI开发，如对此感到不适请寻找其它软件进行替代。

> [!NOTE]
> 本项目处于活跃开发阶段，API 和功能可能会频繁变更。生产环境使用请谨慎。


一款基于 WPF 和 Material Design 3 的智能白板应用程序，支持流畅的手写输入、多页面管理和实时墨迹平滑。

## 功能特性

### 核心功能
- **手写绘画**：基于 WPF InkCanvas 的高性能绘图引擎，支持笔锋算法
- **橡皮擦**：支持滑动清屏手势，快速清空画布
- **选择模式**：选择和操作画布上的笔迹和附件
- **撤销/恢复**：完整的操作历史记录支持
- **多页面管理**：支持创建、切换、删除多个页面，带实时预览
- **缩放和平移**：支持鼠标滚轮缩放和双指手势缩放平移
- **笔刷设置**：可调节笔刷粗细（3档）和颜色（9种预设颜色）
- **附件管理**：支持导入图片、视频、文本、链接等多种附件，支持拖拽移动、调整大小、置顶等操作

### 高级特性
- **实时墨迹平滑**：基于 OneEuroFilter 算法的实时墨迹平滑，提供流畅自然的书写体验
- **笔锋算法**：模拟真实笔触的起笔、收笔渐变效果，根据书写速度动态调整笔迹粗细
- **输入设备适配**：支持多种输入设备，包括 RealTimeStylus 适配器
- **输入过滤系统**：灵活的输入过滤器架构，支持独占模式等高级输入处理
- **触摸手势识别**：智能识别触摸手势，支持双指缩放、平移等操作
- **伪装功能**：支持伪装模式，用于特殊场景下的演示需求
- **设置持久化**：应用设置自动保存到本地 JSON 文件
- **响应式界面**：基于 Material Design 3 的现代化 UI 设计
- **自动扩展画布**：支持画布自动扩展功能
- **附件导入**：支持批量导入图片、视频、文本文件、文本内容和链接

## 技术栈

- **框架**：.NET 10.0 Windows (Target: net10.0-windows10.0.26100.0)
- **UI 框架**：WPF (Windows Presentation Foundation)
- **UI 库**：MaterialDesignThemes v5.3.0 (Material Design 3)
- **JSON 处理**：Newtonsoft.Json v13.0.4
- **图形处理**：System.Drawing.Common v10.0.1
- **字体**：MiSans 字体系列

## 项目结构

```
WindBoard/
├── Core/                    # 核心功能模块
│   ├── Filters/            # 输入过滤器
│   │   ├── ExclusiveModeFilter.cs
│   │   ├── IInputFilter.cs
│   │   └── InputFilterBase.cs
│   ├── Ink/                # 墨迹算法
│   │   ├── InkSmoothingDefaults.cs
│   │   ├── InkSmoothingParameters.cs
│   │   ├── OneEuroFilter2D.cs
│   │   ├── RealtimeInkSmoother.cs
│   │   ├── SimulatedPressureConfig.cs
│   │   └── StrokeThicknessMetadata.cs
│   ├── Input/              # 输入管理
│   │   ├── RealTimeStylus/
│   │   │   ├── RealTimeStylusAdapter.cs
│   │   │   └── RealTimeStylusManager.cs
│   │   ├── InputDeviceType.cs
│   │   ├── InputEventArgs.cs
│   │   ├── InputManager.cs
│   │   ├── InputSourceSelector.cs
│   │   ├── InputStage.cs
│   │   └── StylusPlugInsAccessor.cs
│   └── Modes/              # 交互模式
│       ├── EraserMode.cs
│       ├── IInteractionMode.cs
│       ├── InkMode.cs
│       ├── InteractionModeBase.cs
│       ├── ModeController.cs
│       ├── NoMode.cs
│       └── SelectMode.cs
├── MainWindow/             # 主窗口逻辑
│   ├── MainWindow.Architecture.cs
│   ├── MainWindow.Attachments.cs
│   ├── MainWindow.InputPipeline.cs
│   ├── MainWindow.Pages.cs
│   └── MainWindow.UI.cs
├── Models/                 # 数据模型
│   ├── BoardAttachment.cs
│   ├── BoardAttachmentType.cs
│   ├── BoardPage.cs
│   └── ImportRequest.cs
├── Services/               # 业务服务
│   ├── AutoExpandService.cs
│   ├── CamouflageService.cs
│   ├── PagePreviewRenderer.cs
│   ├── PageService.cs
│   ├── SettingsService.cs
│   ├── StrokeService.cs
│   ├── StrokeUndoHistory.cs
│   ├── TouchGestureService.cs
│   └── ZoomPanService.cs
├── Views/                  # 视图和控件
│   ├── Controls/
│   │   ├── PageNavigatorControl.xaml
│   │   └── PageNavigatorControl.xaml.cs
│   ├── Dialogs/
│   │   ├── ImportDialog.xaml
│   │   └── ImportDialog.xaml.cs
│   ├── MainWindow.xaml
│   ├── MainWindow.xaml.cs
│   ├── SettingsWindow.xaml
│   └── SettingsWindow.xaml.cs
├── Styles/                 # 样式资源
│   └── BottomBarStyles.xaml
├── Resources/              # 资源文件
│   └── Fonts/              # 字体文件
├── App.xaml                # 应用程序入口
└── App.xaml.cs
```


## 架构设计

### 核心架构
- **输入管道**：采用分阶段的输入处理管道，支持多种输入设备和过滤器
- **模式系统**：基于策略模式的交互模式管理（InkMode、EraserMode、SelectMode 等）
- **服务层**：模块化的服务设计（PageService、StrokeService、ZoomPanService 等）
- **事件驱动**：基于 WPF 事件系统的松耦合架构

### 关键设计模式
- **策略模式**：交互模式的切换和管理
- **过滤器模式**：输入事件的过滤和处理
- **观察者模式**：服务间的状态同步
- **适配器模式**：RealTimeStylus 适配器集成

### 模块职责
- **Core**：核心算法和基础组件（墨迹平滑、输入处理、交互模式）
- **Services**：业务逻辑服务（页面管理、笔迹管理、缩放平移等）
- **MainWindow**：主窗口协调器，整合各模块
- **Views**：UI 视图和自定义控件


## 快速开始

### 环境要求
- .NET 10.0 SDK
- Windows 10/11
- Visual Studio 2022 或更高版本（推荐）,或者 Visual Studio Code 也行（本软件的一部分开发工作就是在VSCode使用AI完成的，另一部分在Codex）

### 构建和运行
```bash
# 克隆仓库
git clone https://github.com/Jerry-Z07/WindBoard.git
cd WindBoard

# 还原依赖
dotnet restore

# 构建项目
dotnet build

# 运行应用
dotnet run
```


## Todo
- [x] 笔迹性能优化
- [x] 笔锋算法
- [ ] 触摸面积识别算法（用于掌擦逻辑）
- [x] 伪装功能
- [x] 附件管理功能（图片、视频、文本、链接导入）
- [ ] 补全相关设置项
- [ ] 软件整体性能优化
- [ ] 软件中文名及图标设计
- [ ] 完善文档
- [ ] 完善workflow等

## 许可证

Apache License 2.0

## 贡献

欢迎提交 Issue 和 Pull Request！


