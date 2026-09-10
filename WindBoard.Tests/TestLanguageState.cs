using System;
using System.Globalization;

namespace WindBoard.Tests;

/// <summary>
/// 进程/线程级语言状态（<see cref="CultureInfo"/> 四项 + MRT <c>PrimaryLanguageOverride</c>）的
/// 捕获与还原，供“改写语言状态”的测试类统一使用，避免残留污染其他测试。
/// </summary>
/// <remarks>
/// 约束与依据（详见 .trellis/spec/backend/quality-guidelines.md）：
/// - MRT override 为进程级全局状态；unpackaged 环境下把 override 赋值为空串会抛“未指定的错误”（实测），
///   因此还原时按 <c>AppLanguageService.ApplyPrimaryLanguageOverride</c> 的降级策略处理
///   （清空失败则退回系统 UI 语言，避免把测试期间设置的 override 残留在进程里）；
/// - 改写语言状态的测试类必须与读取语言的测试类（渲染快照）放进同一 xUnit collection 串行执行，
///   否则并行窗口内会出现“写方已改为 en-US、读方按 zh-CN 断言”的竞态。
/// </remarks>
internal static class TestLanguageState
{
    /// <summary>语言状态快照（override 不可读时 <see cref="PrimaryLanguageOverrideReadable"/> 为 false）。</summary>
    internal sealed record Snapshot(
        CultureInfo CurrentCulture,
        CultureInfo CurrentUiCulture,
        CultureInfo? DefaultThreadCurrentCulture,
        CultureInfo? DefaultThreadCurrentUiCulture,
        string PrimaryLanguageOverride,
        bool PrimaryLanguageOverrideReadable);

    /// <summary>捕获当前线程/进程语言状态（在用例开始时调用）。</summary>
    internal static Snapshot Capture()
    {
        bool readable = TryGetPrimaryLanguageOverride(out string overrideValue);

        return new Snapshot(
            CultureInfo.CurrentCulture,
            CultureInfo.CurrentUICulture,
            CultureInfo.DefaultThreadCurrentCulture,
            CultureInfo.DefaultThreadCurrentUICulture,
            overrideValue,
            readable);
    }

    /// <summary>还原语言状态（在用例 finally 中调用；override 不可读时不改写）。</summary>
    internal static void Restore(Snapshot snapshot)
    {
        CultureInfo.CurrentCulture = snapshot.CurrentCulture;
        CultureInfo.CurrentUICulture = snapshot.CurrentUiCulture;
        CultureInfo.DefaultThreadCurrentCulture = snapshot.DefaultThreadCurrentCulture;
        CultureInfo.DefaultThreadCurrentUICulture = snapshot.DefaultThreadCurrentUiCulture;

        if (!snapshot.PrimaryLanguageOverrideReadable)
        {
            return;
        }

        if (TrySetPrimaryLanguageOverride(snapshot.PrimaryLanguageOverride, out _))
        {
            return;
        }

        // 清空失败（unpackaged 环境赋空串抛异常）：降级为系统 UI 语言，保证不残留测试期间的 override。
        string fallback = CultureInfo.CurrentUICulture.Name;
        if (!string.IsNullOrWhiteSpace(fallback) && TrySetPrimaryLanguageOverride(fallback, out _))
        {
            return;
        }

        Console.Error.WriteLine($"[Tests] PrimaryLanguageOverride 还原失败（captured='{snapshot.PrimaryLanguageOverride}'）");
    }

    /// <summary>尽力设置 MRT override（App SDK 与 Windows.* 两个 API 变体依次尝试）。</summary>
    internal static void SetPrimaryLanguageOverrideBestEffort(string value)
    {
        _ = TrySetPrimaryLanguageOverride(value, out _);
    }

    /// <summary>读取 MRT override；两个 API 变体均不可用时返回 false（value 为空串）。</summary>
    internal static bool TryGetPrimaryLanguageOverride(out string value)
    {
        try
        {
            value = Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride ?? string.Empty;
            return true;
        }
        catch
        {
            // 继续尝试 Windows.* 版本。
        }

        try
        {
            value = Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride ?? string.Empty;
            return true;
        }
        catch
        {
            value = string.Empty;
            return false;
        }
    }

    private static bool TrySetPrimaryLanguageOverride(string value, out Exception? error)
    {
        error = null;

        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = value;
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
        }

        try
        {
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = value;
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }
}
