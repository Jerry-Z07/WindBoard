using Xunit;

// 关闭测试程序集内跨测试类（集合）的并行执行。
//
// 原因：部分测试会改动进程级全局状态且现有清理不完整（如 AppSettingsServiceTests 经
// AppLanguageService.Apply 设置 MRT PrimaryLanguageOverride、CultureInfo Default*），
// 并行时会与渲染本地化内容的快照测试（Rendering/Snapshot）产生竞态，导致 L10n 解析
// 回退为 key 字符串、基准比对随机失败（实测复现）。
// 串行后全量 wall time 仍在秒级（538 用例 < 2s），确定性优先于并行收益。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
