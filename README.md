<div align="center">
  <img src="./Resources/icons/icon.png" alt="icon" width="15%" />
</div>

<div align="center">
  <h2><strong>轻风白板</strong></h2>
</div>

<div align="center">
  开源、简洁、易上手的电子白板
</div>

---

![GitHub commit activity (branch)](https://img.shields.io/github/commit-activity/m/Jerry-Z07/WindBoard/main) ![GitHub Issues or Pull Requests](https://img.shields.io/github/issues/Jerry-Z07/WindBoard) ![GitHub License](https://img.shields.io/github/license/Jerry-Z07/WindBoard) [![zread](https://img.shields.io/badge/Ask_Zread-_.svg?style=flat&color=00b0aa&labelColor=000000&logo=data%3Aimage%2Fsvg%2Bxml%3Bbase64%2CPHN2ZyB3aWR0aD0iMTYiIGhlaWdodD0iMTYiIHZpZXdCb3g9IjAgMCAxNiAxNiIgZmlsbD0ibm9uZSIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIj4KPHBhdGggZD0iTTQuOTYxNTYgMS42MDAxSDIuMjQxNTZDMS44ODgxIDEuNjAwMSAxLjYwMTU2IDEuODg2NjQgMS42MDE1NiAyLjI0MDFWNC45NjAxQzEuNjAxNTYgNS4zMTM1NiAxLjg4ODEgNS42MDAxIDIuMjQxNTYgNS42MDAxSDQuOTYxNTZDNS4zMTUwMiA1LjYwMDEgNS42MDE1NiA1LjMxMzU2IDUuNjAxNTYgNC45NjAxVjIuMjQwMUM1LjYwMTU2IDEuODg2NjQgNS4zMTUwMiAxLjYwMDEgNC45NjE1NiAxLjYwMDFaIiBmaWxsPSIjZmZmIi8%2BCjxwYXRoIGQ9Ik00Ljk2MTU2IDEwLjM5OTlIMi4yNDE1NkMxLjg4ODEgMTAuMzk5OSAxLjYwMTU2IDEwLjY4NjQgMS42MDE1NiAxMS4wMzk5VjEzLjc1OTlDMS42MDE1NiAxNC4xMTM0IDEuODg4MSAxNC4zOTk5IDIuMjQxNTYgMTQuMzk5OUg0Ljk2MTU2QzUuMzE1MDIgMTQuMzk5OSA1LjYwMTU2IDE0LjExMzQgNS42MDE1NiAxMy43NTk5VjExLjAzOTlDNS42MDE1NiAxMC42ODY0IDUuMzE1MDIgMTAuMzk5OSA0Ljk2MTU2IDEwLjM5OTlaIiBmaWxsPSIjZmZmIi8%2BCjxwYXRoIGQ9Ik0xMy43NTg0IDEuNjAwMUgxMS4wMzg0QzEwLjY4NSAxLjYwMDEgMTAuMzk4NCAxLjg4NjY0IDEwLjM5ODQgMi4yNDAxVjQuOTYwMUMxMC4zOTg0IDUuMzEzNTYgMTAuNjg1IDUuNjAwMSAxMS4wMzg0IDUuNjAwMUgxMy43NTg0QzE0LjExMTkgNS42MDAxIDE0LjM5ODQgNS4zMTM1NiAxNC4zOTg0IDQuOTYwMVYyLjI0MDFDMTQuMzk4NCAxLjg4NjY0IDE0LjExMTkgMS42MDAxIDEzLjc1ODQgMS42MDAxWiIgZmlsbD0iI2ZmZiIvPgo8cGF0aCBkPSJNNCAxMkwxMiA0TDQgMTJaIiBmaWxsPSIjZmZmIi8%2BCjxwYXRoIGQ9Ik00IDEyTDEyIDQiIHN0cm9rZT0iI2ZmZiIgc3Ryb2tlLXdpZHRoPSIxLjUiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIvPgo8L3N2Zz4K&logoColor=ffffff)](https://zread.ai/Jerry-Z07/WindBoard) 


> [!NOTE]
> 本项目处于活跃开发阶段，API 和功能可能会频繁变更。生产环境使用请谨慎。

> [!IMPORTANT]
> 软件尚未成熟，如果将其用于要求较高的生产环境（如课堂等），请保证能够承担由于软件问题导致的一些突发状况（如书写手感异常、崩溃等）的责任。

## 软件截图
<details>
<summary>点击查看截图</summary>

![主界面](./docs/images/MainWindow.png)
![设置](./docs/images/Settings.png)
![导出](./docs/images/Export.png)
</details>

## 功能
- 基础书写（粗细、颜色、擦除、撤销/恢复等）
- 导出/导入功能
- 多页面管理
- 无界书写
- 多语言支持
- ...（开发中）


### 一些特别功能
- 伪装：替换白板的软件图标和标题（~~如果有人问这个软件为什么不一样，就说是更新了~~）
- 视频展台集成：与希沃视频展台的基础集成，提供一个入口供跳转

## 技术栈

- **框架**：.NET 10.0 Windows 
- **UI 框架**：WPF 
- **UI 库**：MaterialDesignThemes 


## 快速开始

> [!IMPORTANT]
> 本项目完全使用 AI 开发，如对此无法接受请寻找其它软件进行替代。

### 用户
可跳转 [Releases](https://github.com/Jerry-Z07/WindBoard/releases/latest) 获取软件，其中：
- 后缀为`.exe`的为安装版，`.zip`为便携版
- 软件名中，带`-fd`后缀的为不带运行库的版本，你需要自行安装 .NET 10运行库 [点这里下载安装包](https://dotnet.microsoft.com/zh-cn/download/dotnet/thank-you/runtime-desktop-10.0.1-windows-x64-installer)，不带`-fd`的则不需要另外安装运行库

**请阅读[快速开始](./docs/user/quick-start.md)获取软件的使用方法**

### 开发者
#### 环境要求
- .NET 10.0 SDK
- Windows 10/11
- 开发工具：
  - Visual Studio 2026 或更高版本
  - JetBrains Rider 2025.3.1 或更高版本
  - Visual Studio Code



## Todo / 仍需完善的部分
- [ ] 更优化的笔迹平滑算法
- [ ] 大量笔迹的缩放/移动性能
- [ ] 触摸面积识别算法（用于掌擦逻辑）
- [ ] 软件整体性能优化
- [ ] 完善文档

## 贡献
我们欢迎 Pull Request 和 Issue，也非常感谢你对本项目的支持！


## 生态
为保存和还原笔迹状态，我们设计了一个私有格式`.wbi`  
你可以查看[.wbi格式介绍](./docs/dev/wbi-format.md)来查看该文件格式的介绍


## 碎碎念
### 为什么要做这个软件？
由于某白板软件的新版本在班上电脑冷启动耗时极长，且砍掉了视频展台的跳转，加之想探探AI的上限，于是便有了这个项目


## 许可证

Apache License 2.0


