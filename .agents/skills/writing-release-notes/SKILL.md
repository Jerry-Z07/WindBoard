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

获取用户提到的起始和结束 commit，如果没有就要求用户提供，使用 `git log <start>..<end>` 查看范围内的所有提交。

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
- <emoji> <type>: <描述>
```

**描述原则：**
- 面向用户，不说"代码做了什么"，而说"修复/新增了什么"
- 合并相关提交：同一功能的 feat 和对应 fix 可合并为一条
- 拆分无关变更：一个 commit 中涉及多个不相关修复时，拆分为独立条目
- 补充细节：若 commit message 简略，从 diff 中提取用户关心的信息补充

**示例：**
```
- 🐛 fix: 修复框选命中判断，从仅 AABB 判断改为基于笔迹几何与压感宽度计算，避免误选斜线和压感笔迹
- ✨ feat: 添加应用数据清理功能，启动时自动清理过期的下载安装包与旧版本图标缓存
```

### 5. 排序

按以下优先级排列：
1. feat（新功能）
2. fix（修复）
3. perf（性能）
4. refactor（重构）

同类型内按重要性排序，重要变更靠前。

### 6. 文件格式

同时生成中文和英文两个文件，放在 `docs/release-notes/` 目录下：

- `v<版本号>.zh-CN.md`
- `v<版本号>.en-US.md`

**中文模板：**
```markdown
# 更新内容（v<版本号>）

- ✨ feat: ...
- 🐛 fix: ...
- ⚡ perf: ...
- ♻️ refactor: ...

# 已知问题

- 位于v1.x版本的用户直接覆盖安装可能会有未知兼容性问题，建议完全卸载旧版并重装。
```

**英文模板：**
```markdown
# Changelog (v<版本号>)

- ✨ feat: ...
- 🐛 fix: ...
- ⚡ perf: ...
- ♻️ refactor: ...

# Known Issues

- Users upgrading directly from v1.x by overwriting the installation may encounter unknown compatibility issues. It is recommended to completely uninstall the old version and reinstall the new one.
```

## 常见错误

| 错误 | 正确做法 |
|------|---------|
| 直接复制 commit message 作为条目 | 转化为面向用户的描述 |
| 记录所有 refactor 提交 | 仅记录用户可感知的重构 |
| 将一个 commit 的多个修复混为一条 | 拆分为独立条目，方便用户检索 |
| 遗漏已知问题章节 | 保留已知问题，如无新增则沿用上一版 |
