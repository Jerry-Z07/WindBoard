using Xunit;

// E2E 禁止并行：多个用例会同时启动被测应用实例，桌面级 UIA 查找（全局 AutomationId）
// 与设置文件备份互相串台，必须严格串行（与 design.md 环境隔离方案 B 的前提一致）。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
