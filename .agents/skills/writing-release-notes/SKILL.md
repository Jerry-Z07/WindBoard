---
name: writing-release-notes
description: Use when writing or updating release notes for a new version, or when summarizing git commit history into a changelog
---

# 编写 Release Note

## 概述

从 git 提交历史中提取用户可见的变更，按约定式提交类型分类，编写格式统一的 release note。

## 何时使用

- 需要为新版本编写 release note
- 需要将一段 git 提交范围总结为更新日志
- 用户要求总结功能更改和 bug 修复

## 核心流程

### 1. 确定提交范围

获取用户提到的起始和结束 commit，如果没有就要求用户提供范围，不要假设，使用 `git log <start>..<end>` 查看范围内的所有提交。

### 2. 逐条阅读提交详情

对每个提交执行 `git show <hash> --stat`，理解实际变更内容：
- commit message 可能过于简略，需要通过 diff stat 补充
- 一个 commit 可能包含多个变更点，需要拆分记录
- 合并提交和纯 chore/doc 提交跳过不记录

### 3. 分类与过滤

**记录的类型：**

| 类型 | emoji | 前缀 | 说明 |
|------|-------|------|------|
| 新功能 | ✨ | feat | 用户可感知的新增功能 |
| 修复 | 🐛 | fix | Bug 修复 |
| 性能 | ⚡ | perf | 性能优化 |
| 重构 | ♻️ | refactor | 用户可感知的重构（如交互变更） |

**跳过的类型：**
- `chore`：构建/工具/依赖等内部维护
- `doc`/`docs`：纯文档变更
- `refactor`：仅内部代码整理且用户不可感知的
- Merge 提交

### 4. 编写条目

每条格式：
```
- <emoji> <type>: [影响范围] <描述>
```

**影响范围：**
- 每条必须标注，不能省略
- 中文用 `[桌面端]`、`[网页端]`、`[桌面端、网页端]`；英文用 `[Desktop]`、`[Web]`、`[Desktop & Web]`
- 按实际生效范围标注：只改桌面就写桌面端，两端同步实现才写两端

**描述原则：**
- 面向用户，不说"代码做了什么"，而说"修复/新增了什么"
- 合并相关提交：同一功能的 feat 和对应 fix 可合并为一条
- 拆分无关变更：一个 commit 中涉及多个不相关修复时，拆分为独立条目
- 补充细节：若 commit message 简略，从 diff 中提取用户关心的信息补充

**示例：**
```
- 🐛 fix: [桌面端] 修复框选命中判断，从仅 AABB 判断改为基于笔迹几何与压感宽度计算，避免误选斜线和压感笔迹
- ✨ feat: [网页端] 支持自动保存与恢复，刷新或重新打开页面后可继续上一次的书写
```

### 5. 排序

按以下优先级排列：
1. feat（新功能）
2. fix（修复）
3. perf（性能）
4. refactor（重构）

同类型内按重要性排序，重要变更靠前。

### 6. 文件格式

按端拆分文件，放在 `docs/release-notes/` 目录下：

- 桌面端：`v<版本号>.zh-CN.md`、`v<版本号>.en-US.md`
  - 写入 `latest.json`，会显示在桌面端应用内更新弹窗与 GitHub Release 正文
  - 两端共有的变更写在这里，标 `[桌面端、网页端]`
- 网页端：`v<版本号>.web.zh-CN.md`、`v<版本号>.web.en-US.md`
  - 只并入 GitHub Release 正文，不进入 `latest.json`
  - 只写网页端独有的新增、修复与变更，避免与桌面端日志重复

**中文模板：**
```markdown
# 更新内容（桌面端 v<版本号>）

- ✨ feat: [影响范围] ...
- 🐛 fix: [影响范围] ...
- ⚡ perf: [影响范围] ...
- ♻️ refactor: [影响范围] ...

```

**英文模板：**
```markdown
# Release Notes (Desktop v<版本号>)

- ✨ feat: [Scope] ...
- 🐛 fix: [Scope] ...
- ⚡ perf: [Scope] ...
- ♻️ refactor: [Scope] ...

```

**网页端模板：**
```markdown
# 更新内容（网页端 v<版本号>）

- ✨ feat: [网页端] ...
- 🐛 fix: [网页端] ...

```

```markdown
# Release Notes (Web v<版本号>)

- ✨ feat: [Web] ...
- 🐛 fix: [Web] ...

```

## 常见错误

| 错误 | 正确做法 |
|------|---------|
| 直接复制 commit message 作为条目 | 转化为面向用户的描述 |
| 记录所有 refactor 提交 | 仅记录用户可感知的重构 |
| 将一个 commit 的多个修复混为一条 | 拆分为独立条目，方便用户检索 |
| 条目未标注影响范围 | 每条都写清作用于桌面端、网页端或两端 |
| 把网页端独有的变更写进桌面端日志 | 写入 `.web.*.md`，避免混入桌面端应用内更新弹窗 |
