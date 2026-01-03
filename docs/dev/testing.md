# 测试指南

WindBoard.Tests 使用 xUnit，并通过 `Xunit.StaFact` 支持 WPF/STA 线程相关测试。

## 测试框架版本

- xUnit v2.9.3
- Xunit.StaFact v1.2.69
- Microsoft.NET.Test.Sdk v18.0.1
- coverlet.collector v6.0.4（测试覆盖率收集）
- xunit.runner.visualstudio v2.5.7

## 运行测试

### 基本命令

```bash
# 运行所有测试
dotnet test WindBoard.sln

# 运行特定项目的测试
dotnet test WindBoard.Tests/WindBoard.Tests.csproj

# 显示详细输出
dotnet test WindBoard.sln --verbosity normal

# 运行特定测试（使用过滤器）
dotnet test WindBoard.sln --filter "FullyQualifiedName~DetailPreservingSmoother"
```

### 测试覆盖率

```bash
# 收集测试覆盖率
dotnet test WindBoard.sln -p:CollectCoverage=true

# 生成覆盖率报告（需要安装 ReportGenerator）
dotnet test WindBoard.sln -p:CollectCoverage=true -p:CoverletOutputFormat=opencover

# 设置覆盖率阈值
dotnet test WindBoard.sln -p:CollectCoverage=true -p:Threshold=80
```

## 测试项目结构

```
WindBoard.Tests/
├── Ink/                        # 墨迹算法测试
│   ├── DetailPreservingSmootherTests.cs
│   ├── InkModeTests.cs
│   ├── SimulatedPressureDefaultsTests.cs
│   ├── SimulatedPressureTests.cs
│   └── StrokeThicknessMetadataTests.cs
├── Services/                   # 服务测试
│   ├── Export/
│   │   ├── ExportRendererTests.cs
│   │   ├── WbiExporterTests.cs
│   │   └── WbiImporterTests.cs
│   ├── PageServiceTests.cs
│   ├── StrokeUndoHistoryTests.cs
│   └── ZoomPanServiceTests.cs
├── Resources/                  # 资源测试
│   ├── FontDeploymentTests.cs
│   └── LocalizationResourcesTests.cs
└── TestHelpers/                # 测试辅助工具
    └── InkTestHelpers.cs
```

## 放置位置与命名

- 测试项目：`WindBoard.Tests/`
- 按领域分文件夹（示例）：
  - `WindBoard.Tests/Ink/`：墨迹相关逻辑（书写模式、模拟压感等）
  - `WindBoard.Tests/Services/`：页面/导出导入等服务
  - `WindBoard.Tests/Resources/`：资源相关测试（字体、本地化等）
- 命名建议：`ClassName_MethodUnderTest_ExpectedOutcome`

## 编写建议

### 测试特性

- **普通测试**：使用 `[Fact]` 特性
- **WPF STA 测试**：使用 `[StaFact]` 特性（涉及 WPF 类型如 `InkCanvas`、`StrokeCollection`）
- **参数化测试**：使用 `[Theory]` 和 `[InlineData]`、`[MemberData]` 等数据源特性
- **跳过测试**：使用 `[Skip("原因")]` 特性

### 测试编写原则

- 尽量使用确定性数据（不要依赖随机数）
- 同时断言状态与副作用（例如：导出 ZIP 条目、导入后的页面属性、笔迹数量等）
- 每个测试应该独立，不依赖其他测试的执行顺序
- 测试名称应该清晰描述测试的目的

### 测试辅助工具

项目提供了一些测试辅助工具，位于 `WindBoard.Tests/TestHelpers/`：

- `InkTestHelpers.cs`：墨迹测试辅助工具，提供创建测试笔迹、测试点等便捷方法

### 示例测试

```csharp
public class SimulatedPressureTests
{
    [StaFact]
    public void CalculatePressure_WithValidPoints_ReturnsExpectedPressure()
    {
        // Arrange
        var parameters = SimulatedPressureDefaults.Default;
        var points = new List<StylusPoint>
        {
            new StylusPoint(0, 0, 0.5f),
            new StylusPoint(10, 10, 0.5f)
        };

        // Act
        var result = SimulatedPressure.CalculatePressure(points, parameters);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.All(p => p >= 0 && p <= 1));
    }
}
```

## 测试覆盖率目标

- **核心算法**（墨迹平滑、模拟压感等）：目标覆盖率 90%+
- **服务层**（页面管理、导出导入等）：目标覆盖率 80%+
- **整体项目**：目标覆盖率 70%+

## 常见问题

### STA 线程测试失败

如果遇到 STA 线程测试失败，确保：

1. 使用 `[StaFact]` 而不是 `[Fact]`
2. 测试方法不依赖特定的 UI 线程状态
3. 避免在测试中创建实际的 WPF 窗口

### 字体加载测试失败

如果字体加载测试失败，检查：

1. 测试输出目录是否包含字体文件
2. 字体文件路径是否正确
3. 字体文件是否损坏

### 导出/导入测试失败

如果导出/导入测试失败，检查：

1. 临时目录是否有写入权限
2. 测试数据是否完整
3. 文件路径是否正确

## 参考资源

- xUnit 文档：https://xunit.net/
- Xunit.StaFact 文档：https://github.com/xunit/xunit.stafact
- 项目现有测试用例（例如 `WindBoard.Tests/Services/Export/WbiExporterTests.cs`、`WindBoard.Tests/Services/Export/WbiImporterTests.cs`）
