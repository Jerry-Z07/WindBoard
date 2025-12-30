# 构建与运行

## 环境要求

- Windows 10/11
- .NET 10.0 SDK
- 项目目标框架：`net10.0-windows10.0.26100.0`（WPF）

## 常用命令

在仓库根目录执行：

```bash
dotnet restore
dotnet build WindBoard.sln
dotnet run --project WindBoard.csproj
dotnet test WindBoard.sln
```

覆盖率（可选）：

```bash
dotnet test WindBoard.sln -p:CollectCoverage=true
```

