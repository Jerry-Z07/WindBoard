# WindBoard 建议常用命令（Windows / 仓库根目录）

## 依赖与构建
```powershell
# 还原依赖
dotnet restore

# 构建解决方案
dotnet build WindBoard.sln
```

## 运行
```powershell
# 运行主程序
dotnet run --project WindBoard.csproj
```

## 测试
```powershell
# 运行全部测试
dotnet test WindBoard.sln

# 可选：带覆盖率（依赖 coverlet.collector）
dotnet test WindBoard.sln -p:CollectCoverage=true
```

## 发布/打包（参考 CI）
- GitHub Release 流程见：`.github/workflows/release.yml`
- 主要动作：`dotnet publish WindBoard.csproj -c Release -r <RID> --self-contained <true|false>`
- 典型 RID：`win-x86` / `win-x64` / `win-arm64`

## Git 常用
```powershell
# 查看最近提交
git log -n 20 --oneline

# 查找标签
git tag --list "v*"
```
