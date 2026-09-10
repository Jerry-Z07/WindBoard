# P3 FlaUI UI 自动化测试 — 执行计划

## 执行清单（按序）

### 第 1 步：工程骨架
- [ ] 新建 `WindBoard.UITests`（xUnit，net10.0-windows10.0.26100.0），加 `FlaUI.UIA3`（确认最新稳定版），加入 `WindBoard.slnx`。
- [ ] 应用启动/等待/退出的 fixture 封装（`WindBoardAppFixture`：Launch → 等主窗口 → 提供 AutomationBase）。
- [ ] 冒烟用例：启动 → 主窗口元素存在 → 退出，跑通全链路。
- 验证：`dotnet test WindBoard.slnx --filter "Category=E2E"`
- 回滚点：独立工程，删除即回滚。

### 第 2 步：AutomationId 标注（主工程，最小化）
- [ ] 按 design.md 命名规范，为首批 6 条场景触达的控件补 `AutomationProperties.AutomationId`（设置页、Dock、伪装入口、导入导出、页切换）。
- [ ] 构建 + 全量单测确认无回归；XAML 改动不改变任何绑定/事件行为。

### 第 3 步：环境隔离工具
- [ ] `SettingsBackup`（settings.json 备份/恢复）与临时目录管理工具（GUID 子目录 + Dispose 清理）。
- [ ] 断言基元封装（按 AutomationId 查找 + retry 等待 + 失败截图）。

### 第 4 步：场景实现（每条独立可回退）
- [ ] 设置持久化（含重启断言）→ Dock 显隐 → 伪装进出 → 导出 PNG → 导入 WBIX。
- 评审门：前 3 条完成后人工过一遍失败截图产物形态。
- [ ] 每条用例标注 `[Trait("Category", "E2E")]`。

### 第 5 步：稳定性验证与收尾
- [ ] 连续运行 3 次（本地交互桌面）确认无环境残留失败。
- [ ] 确认 `dotnet test WindBoard.slnx --filter Category!=E2E`（单测口径）时长不受影响。
- [ ] 在 UITests 工程加简短 README 片段或注释说明运行方式与产物位置。
- [ ] 全量：`dotnet build WindBoard.slnx -c Release` + `dotnet test WindBoard.slnx --filter Category!=E2E` 全绿。

## 验证命令汇总

```powershell
dotnet build WindBoard.slnx -c Release
dotnet test WindBoard.slnx --filter "Category=E2E"      # E2E 套件
dotnet test WindBoard.slnx --filter "Category!=E2E"     # 原单测口径
```

## 风险应对

- 若第 1 步 FlaUI 无法识别 WinUI 3 窗口树（预期外）：先经 Accessibility Insights 确认 UIA 树暴露情况，回报后再定方案（不擅自切换工具链）。
- 若伪装模式热键经自动化不可靠：按 design.md 第 5 节改用 UI 点击路径。
