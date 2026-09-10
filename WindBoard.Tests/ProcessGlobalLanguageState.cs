using Xunit;

namespace WindBoard.Tests;

/// <summary>
/// “进程/线程级语言状态”测试集合的标记类型（xUnit <see cref="CollectionDefinitionAttribute"/> 载体）：
/// 改写语言状态的测试类（<c>AppSettingsServiceTests</c> 经 <c>AppLanguageService.Apply</c>）与读取语言的
/// 测试类（<c>Rendering/Snapshot</c> 的文本场景）必须同属本集合，由 xUnit 串行执行，
/// 消除“写方改为 en-US、读方按 zh-CN 断言”的并行竞态。
/// </summary>
/// <remarks>
/// 取舍说明（2026-09-11）：原先在程序集级关闭跨类并行（<c>DisableTestParallelization</c>）以规避该竞态，
/// 但 xUnit 官方做法是用 test collection 隔离共享状态——全局关闭会牺牲全部并行收益，集合隔离只串行
/// 真正冲突的少数类。各测试类自身仍须在用例内还原进程级全局状态（见 <see cref="TestLanguageState"/> 与
/// .trellis/spec/backend/quality-guidelines.md）。
/// </remarks>
[CollectionDefinition(ProcessGlobalLanguageState.CollectionName)]
public sealed class ProcessGlobalLanguageState
{
    /// <summary>共享“进程/线程级语言状态”的测试类所属集合名。</summary>
    internal const string CollectionName = nameof(ProcessGlobalLanguageState);
}
