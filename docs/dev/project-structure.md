# 项目结构

本页由根目录 `README.md` 的“项目结构”拆分而来，面向开发者用于快速定位代码。

## 目录树

```
WindBoard/
├── Core/                    # 核心功能模块
│   ├── Filters/            # 输入过滤器
│   │   ├── ExclusiveModeFilter.cs
│   │   ├── IInputFilter.cs
│   │   └── InputFilterBase.cs
│   ├── Ink/                # 墨迹算法
│   │   ├── DetailPreservingSmoother.cs
│   │   ├── SegmentSpatialIndex.cs
│   │   ├── SimulatedPressure.cs
│   │   ├── SimulatedPressureDefaults.cs
│   │   ├── SimulatedPressureParameters.cs
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
│   │   ├── StylusPlugInsAccessor.cs
│   │   └── StylusPressureHardware.cs
│   └── Modes/              # 交互模式
│       ├── EraserMode.cs
│       ├── IInteractionMode.cs
│       ├── InkMode.ActiveStroke.cs
│       ├── InkMode.Flush.cs
│       ├── InkMode.cs
│       ├── InteractionModeBase.cs
│       ├── ModeController.cs
│       ├── NoMode.cs
│       └── SelectMode.cs
├── MainWindow/             # 主窗口逻辑（分部类拆分）
│   ├── MainWindow.Architecture.cs
│   ├── MainWindow.Attachments.cs
│   ├── MainWindow.Attachments.BitmapLoader.cs
│   ├── MainWindow.Attachments.ExternalOpen.cs
│   ├── MainWindow.Attachments.Import.cs
│   ├── MainWindow.Attachments.Selection.cs
│   ├── MainWindow.Export.cs
│   ├── MainWindow.InputPipeline.cs
│   ├── MainWindow.Pages.cs
│   ├── MainWindow.Popups.cs
│   ├── MainWindow.SettingsSync.cs
│   ├── MainWindow.SystemDock.cs
│   ├── MainWindow.ToolUi.cs
│   ├── MainWindow.UI.cs
│   └── MainWindow.VideoPresenter.cs
├── Models/                 # 数据模型
│   ├── Export/             # 导出相关模型
│   │   ├── ExportOptions.cs
│   │   ├── ImageExportOptions.cs
│   │   ├── PdfExportOptions.cs
│   │   └── WbiExportOptions.cs
│   ├── Wbi/                # WBI 格式模型
│   │   ├── WbiManifest.cs
│   │   └── WbiPageData.cs
│   ├── AppLanguage.cs
│   ├── AppSettings.cs
│   ├── BoardAttachment.cs
│   ├── BoardAttachmentType.cs
│   ├── BoardPage.cs
│   ├── ImportRequest.cs
│   └── StrokeSmoothingMode.cs
├── Resources/              # 资源文件
│   ├── Fonts/              # MiSans 字体文件
│   ├── icons/              # 图标文件
│   │   ├── icon.ico
│   │   └── icon.png
│   ├── Strings.en-US.xaml  # 英文本地化资源
│   └── Strings.zh-CN.xaml  # 中文本地化资源
├── Services/               # 业务服务
│   ├── Export/            # 导出服务
│   │   ├── ExportRenderer.cs
│   │   ├── ExportService.cs
│   │   ├── PdfExporter.cs
│   │   ├── WbiExporter.cs
│   │   └── WbiImporter.cs
│   ├── Localization/       # 本地化服务
│   │   └── LocalizationService.cs
│   ├── Settings/
│   │   └── SettingsService.cs
│   ├── AppDisplayNames.cs
│   ├── AppFonts.cs
│   ├── AppVersionInfo.cs
│   ├── AutoExpandService.cs
│   ├── CamouflageService.cs
│   ├── PagePreviewRenderer.cs
│   ├── PageService.cs
│   ├── StrokeService.cs
│   ├── StrokeUndoHistory.cs
│   ├── TouchGestureService.cs
│   └── ZoomPanService.cs
├── Styles/                 # 样式资源
│   └── BottomBarStyles.xaml
├── Views/                  # 视图和控件（XAML + code-behind）
│   ├── Controls/
│   │   ├── PageNavigatorControl.xaml
│   │   └── PageNavigatorControl.xaml.cs
│   ├── Dialogs/
│   │   ├── ExportDialog.xaml
│   │   ├── ExportDialog.xaml.cs
│   │   ├── ImportDialog.xaml
│   │   └── ImportDialog.xaml.cs
│   ├── MainWindow.xaml
│   ├── MainWindow.xaml.cs
│   ├── SettingsWindow.About.cs
│   ├── SettingsWindow.Appearance.cs
│   ├── SettingsWindow.Camouflage.cs
│   ├── SettingsWindow.Fields.cs
│   ├── SettingsWindow.Language.cs
│   ├── SettingsWindow.TouchGestures.cs
│   ├── SettingsWindow.VideoPresenter.cs
│   ├── SettingsWindow.Writing.cs
│   ├── SettingsWindow.xaml
│   └── SettingsWindow.xaml.cs
├── WindBoard.Tests/         # 单元测试项目
│   ├── Ink/                # 墨迹算法测试
│   │   ├── DetailPreservingSmootherTests.cs
│   │   ├── InkModeTests.cs
│   │   ├── SimulatedPressureDefaultsTests.cs
│   │   ├── SimulatedPressureTests.cs
│   │   └── StrokeThicknessMetadataTests.cs
│   ├── Resources/          # 资源测试
│   │   ├── FontDeploymentTests.cs
│   │   └── LocalizationResourcesTests.cs
│   ├── Services/           # 服务测试
│   │   ├── Export/
│   │   │   ├── ExportRendererTests.cs
│   │   │   ├── WbiExporterTests.cs
│   │   │   └── WbiImporterTests.cs
│   │   ├── PageServiceTests.cs
│   │   ├── StrokeUndoHistoryTests.cs
│   │   └── ZoomPanServiceTests.cs
│   ├── TestHelpers/        # 测试辅助工具
│   │   └── InkTestHelpers.cs
│   ├── AssemblyInfo.cs
│   └── WindBoard.Tests.csproj
├── docs/                   # 文档目录
│   ├── dev/                # 开发者文档
│   │   ├── architecture-overview.md
│   │   ├── build-and-run.md
│   │   ├── coding-guidelines.md
│   │   ├── project-structure.md
│   │   ├── settings-persistence.md
│   │   ├── testing.md
│   │   └── wbi-format.md
│   ├── user/               # 用户文档
│   │   ├── import-export.md
│   │   └── quick-start.md
│   └── README.md
├── App.xaml                # 应用程序入口
└── App.xaml.cs
```

## 相关文档

- 更宏观的架构说明：[`docs/dev/architecture-overview.md`](architecture-overview.md)
- 编码约定与组织方式：[`docs/dev/coding-guidelines.md`](coding-guidelines.md)
