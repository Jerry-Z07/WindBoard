namespace WindBoard.Tests;

/// <summary>
/// 依赖源码树（仓库根目录）的测试：找不到 <c>WindBoard.slnx</c> 时（例如测试程序集被复制到仓库外运行）
/// 在发现阶段把测试标记为"已跳过"，而不是让测试变红或静默通过。
/// </summary>
/// <remarks>
/// 不用"运行时抛 <c>Xunit.Sdk.SkipException</c>"的写法：xUnit v2 没有运行时 Skip 基础设施
/// （<c>SkipException.ForSkip</c> 的文档写明 only works in v3 and later），运行时抛该异常只会让测试失败。
/// 同理，反射 <c>Activator.CreateInstance</c> 构造该类型也会抛 <c>MissingMethodException</c>（无公开单 string 构造）。
/// </remarks>
internal sealed class RepoFactAttribute : FactAttribute
{
    public RepoFactAttribute()
    {
        if (RepoRootLocator.Find() is null)
        {
            Skip = "未找到 WindBoard.slnx（不在仓库源码树内运行），跳过依赖源码树的测试。";
        }
    }
}
