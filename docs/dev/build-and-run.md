# 构建与运行

## 环境要求

- Windows 10/11
- .NET 10.0 SDK
- 项目目标框架：`net10.0-windows10.0.26100.0`（WPF）

## 开发工具

- **Visual Studio 2022** 或更高版本
- **JetBrains Rider 2025.3.1** 或更高版本
- **Visual Studio Code**

## 常用命令

在仓库根目录执行：

### 还原依赖

```bash
dotnet restore
```

### 构建项目

```bash
# 构建整个解决方案
dotnet build WindBoard.sln

# 仅构建主项目
dotnet build WindBoard.csproj

# 以 Release 模式构建
dotnet build WindBoard.sln -c Release
```

### 运行项目

```bash
# 运行主项目
dotnet run --project WindBoard.csproj

# 以 Release 模式运行
dotnet run --project WindBoard.csproj -c Release
```

### 运行测试

```bash
# 运行所有测试
dotnet test WindBoard.sln

# 运行特定项目的测试
dotnet test WindBoard.Tests/WindBoard.Tests.csproj

# 显示详细输出
dotnet test WindBoard.sln --verbosity normal
```

### 测试覆盖率（可选）

```bash
# 收集测试覆盖率
dotnet test WindBoard.sln -p:CollectCoverage=true

# 生成覆盖率报告（需要安装 ReportGenerator）
dotnet test WindBoard.sln -p:CollectCoverage=true -p:CoverletOutputFormat=opencover
```

## 项目结构

项目包含以下主要部分：

- **WindBoard.csproj**：主应用程序项目
- **WindBoard.Tests/**：单元测试项目
- **Core/**：核心功能模块（输入处理、墨迹算法、交互模式）
- **Services/**：业务服务（页面管理、笔迹管理、导出导入等）
- **Views/**：UI 视图和控件
- **Models/**：数据模型

## 故障排除

### 构建失败

如果遇到构建错误，请确保：

1. 已安装 .NET 10.0 SDK：`dotnet --list-sdks`
2. 已还原所有依赖：`dotnet restore`
3. 清理并重新构建：`dotnet clean && dotnet build`

### 字体加载问题

如果 MiSans 字体无法加载，请检查：

1. `Resources/Fonts/` 目录下是否存在所有 `.ttf` 文件
2. 构建输出目录是否正确复制了字体文件



