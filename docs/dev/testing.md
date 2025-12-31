# 测试指南

WindBoard.Tests 使用 xUnit，并通过 `Xunit.StaFact` 支持 WPF/STA 线程相关测试。

## 测试框架版本

- xUnit v2.9.3
- Xunit.StaFact v1.2.69
- Microsoft.NET.Test.Sdk v18.0.1
- coverlet.collector v6.0.4（测试覆盖率收集）

## 运行测试

```bash
dotnet test WindBoard.sln
```

覆盖率（可选）：

```bash
dotnet test WindBoard.sln -p:CollectCoverage=true
```

## 放置位置与命名

- 测试项目：`WindBoard.Tests/`
- 按领域分文件夹（示例）：
  - `WindBoard.Tests/Ink/`：墨迹相关逻辑（书写模式、模拟压感等）
  - `WindBoard.Tests/Services/`：页面/导出导入等服务
- 命名建议：`ClassName_MethodUnderTest_ExpectedOutcome`

## 编写建议

- 涉及 WPF 类型（如 `InkCanvas`、`StrokeCollection`）的测试优先使用 `[StaFact]`。
- 尽量使用确定性数据（不要依赖随机数）；同时断言状态与副作用（例如：导出 ZIP 条目、导入后的页面属性、笔迹数量等）。
- 参考现有用例（例如 `WindBoard.Tests/Services/Export/WbiExporterTests.cs`、`WindBoard.Tests/Services/Export/WbiImporterTests.cs`）。
