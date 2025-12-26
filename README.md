# WindBoard

一款基于 WPF 和 Material Design 3 的智能白板应用程序，支持流畅的手写输入、多页面管理和实时墨迹平滑。

## 功能特性

### 核心功能
- **手写绘画**：基于 WPF InkCanvas 的高性能绘图引擎
- **橡皮擦**：支持滑动清屏手势，快速清空画布
- **选择模式**：选择和操作画布上的内容
- **撤销/恢复**：完整的操作历史记录支持
- **多页面管理**：支持创建、切换、删除多个页面，带实时预览
- **缩放和平移**：支持鼠标滚轮缩放和双指手势缩放平移
- **笔刷设置**：可调节笔刷粗细（3档）和颜色（9种预设颜色）

### 高级特性
- **实时墨迹平滑**：基于 OneEuroFilter 算法的实时墨迹平滑，提供流畅自然的书写体验
- **输入过滤器**：支持手势擦除、独占模式等输入过滤机制
- **视频展台集成**：可集成外部视频展台软件
- **设置持久化**：应用设置自动保存到本地 JSON 文件
- **响应式界面**：基于 Material Design 3 的现代化 UI 设计

## 技术栈

- **框架**：.NET 10.0 Windows
- **UI 框架**：WPF (Windows Presentation Foundation)
- **UI 库**：MaterialDesignThemes v5.3.0 (Material Design 3)
- **JSON 处理**：Newtonsoft.Json v13.0.4
- **字体**：MiSans 字体系列

## 项目结构

```
WindBoard/
├── Core/                    # 核心功能模块
│   ├── Filters/            # 输入过滤器
│   │   ├── ExclusiveModeFilter.cs
│   │   ├── GestureEraserFilter.cs
│   │   ├── IInputFilter.cs
│   │   └── InputFilterBase.cs
│   ├── Ink/                # 墨迹算法
│   │   ├── InkSmoothingDefaults.cs
│   │   ├── InkSmoothingParameters.cs
│   │   ├── OneEuroFilter2D.cs
│   │   └── RealtimeInkSmoother.cs
│   ├── Input/              # 输入管理
│   │   ├── InputDeviceType.cs
│   │   ├── InputEventArgs.cs
│   │   ├── InputManager.cs
│   │   └── InputStage.cs
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
│   ├── MainWindow.InputPipeline.cs
│   ├── MainWindow.Pages.cs
│   └── MainWindow.UI.cs
├── Models/                 # 数据模型
│   └── BoardPage.cs
├── Services/               # 业务服务
│   ├── AutoExpandService.cs
│   ├── PagePreviewRenderer.cs
│   ├── PageService.cs
│   ├── SettingsService.cs
│   ├── StrokeService.cs
│   └── ZoomPanService.cs
├── Views/                  # 视图和控件
│   ├── Controls/
│   │   ├── PageNavigatorControl.xaml
│   │   └── PageNavigatorControl.xaml.cs
│   ├── MainWindow.xaml
│   ├── MainWindow.xaml.cs
│   ├── SettingsWindow.xaml
│   └── SettingsWindow.xaml.cs
├── Styles/                 # 样式资源
│   └── BottomBarStyles.xaml
├── Resources/              # 资源文件
│   └── Fonts/              # 字体文件
└── App.xaml                # 应用程序入口
```


## 快速开始

### 环境要求
- .NET 10.0 SDK
- Windows 操作系统
- Visual Studio 2022 或更高版本（推荐）

### 构建和运行
```bash
# 克隆仓库
git clone <repository-url>
cd WindBoard

# 还原依赖
dotnet restore

# 构建项目
dotnet build

# 运行应用
dotnet run
```


## Todo
- [ ] 笔迹性能优化
- [ ] 笔锋算法
- [ ] 触摸面积识别算法（用于掌擦逻辑）
- [ ] 伪装功能
- [ ] 补全相关设置项
- [ ] 软件整体性能优化
- [ ] 软件中文名及图标设计
- [ ] 完善文档
- [ ] 完善workflow等

## 许可证

Apache License 2.0

## 贡献

欢迎提交 Issue 和 Pull Request！


