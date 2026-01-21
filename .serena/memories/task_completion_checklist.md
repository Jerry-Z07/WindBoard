# 完成任务时的检查清单

## 变更边界
- 避免无关重构：只改与需求直接相关的代码，保持补丁最小化。
- 避免在 `MainWindow.xaml.cs` 增加业务逻辑（用 `MainWindow/` partial 或下沉到 `Services/`/`Core/`）。

## 质量与回归
- 若修复关键问题或新增行为：优先补充最小可行单元测试/回归测试（只覆盖本次变更）。
- WPF 相关测试：使用 `[StaFact]`。

## 本地验证（建议）
```powershell
dotnet build WindBoard.sln
dotnet test WindBoard.sln
```

## 性能/交互自检（涉及画布/缩放/渲染时）
- 确认缩放/平移使用 `RenderTransform`。
- 确认未对超大画布宿主启用不受控 `BitmapCache`。
- 确认图像解码/加载等重任务不阻塞 UI 线程。

## 文档（需要时）
- 用户行为变化：更新 `docs/user/*`。
- 开发说明变化：更新 `docs/dev/*` 或 `README.md`。
