# WindBoard.UITests（E2E 冒烟套件）

基于 FlaUI（UIA3）驱动真实应用的桌面 UI 自动化测试，覆盖：启动冒烟、设置持久化、Dock 显隐、伪装模式、导出 PNG、导入 WBIX。

## 运行

```powershell
dotnet build WindBoard.slnx -c Release
dotnet test WindBoard.slnx -c Release -p:RunUITests=true --filter "Category=E2E"
```

默认口径 `dotnet test WindBoard.slnx` 不含 E2E（本工程 `IsTestProject` 由 `RunUITests` 开关控制）。

## 约束

- 需要交互式桌面会话（真实鼠标/键盘/UIA），CI 无桌面环境不适用。
- 每条用例独立启动应用，自动备份/恢复 settings.json 与临时目录，幂等可重复。
- 失败时在 `TestResults/uitests-artifacts/<用例>/` 留存截图与步骤日志（steps.log）。
- `WINDBOARD_EXE` 可显式指定被测 exe（默认自动定位仓库内构建输出）。

