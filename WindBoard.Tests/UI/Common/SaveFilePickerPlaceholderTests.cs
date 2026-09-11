using System;
using WindBoard.UI.Common;
using Xunit;

namespace WindBoard.Tests.UI.Common;

/// <summary>
/// FileSavePicker 预创建占位文件判据测试。
/// 覆盖：占位文件（0 字节 + 创建时间在调用窗口内）、用户选中的既有文件、容差边界与时钟回拨。
/// </summary>
public sealed class SaveFilePickerPlaceholderTests
{
    // 固定基准时刻（含偏移），避免依赖本机时区与当前时间。
    private static readonly DateTimeOffset PickStartedAt = new(2026, 9, 11, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void IsPlaceholder_ReturnsTrue_WhenEmptyFileCreatedAfterPickStarted()
    {
        // Picker 预创建的占位文件：0 字节，且创建时间落在本次调用窗口内。
        bool result = SaveFilePickerPlaceholder.IsPlaceholder(
            PickStartedAt.AddMilliseconds(120),
            fileLengthBytes: 0,
            pickStartedAt: PickStartedAt);

        Assert.True(result);
    }

    [Fact]
    public void IsPlaceholder_ReturnsTrue_AtToleranceBoundary()
    {
        // 边界：部分卷（FAT/exFAT）创建时间精度为秒级，等于容差下限时仍按占位文件处理。
        bool result = SaveFilePickerPlaceholder.IsPlaceholder(
            PickStartedAt - SaveFilePickerPlaceholder.TimestampTolerance,
            fileLengthBytes: 0,
            pickStartedAt: PickStartedAt);

        Assert.True(result);
    }

    [Fact]
    public void IsPlaceholder_ReturnsTrue_WhenCreationTimeIsAheadOfPickStarted()
    {
        // 系统时钟回拨：创建时间晚于记录时刻，同样按占位文件处理。
        bool result = SaveFilePickerPlaceholder.IsPlaceholder(
            PickStartedAt.AddSeconds(1),
            fileLengthBytes: 0,
            pickStartedAt: PickStartedAt);

        Assert.True(result);
    }

    [Fact]
    public void IsPlaceholder_ReturnsFalse_WhenEmptyFileCreatedBeforeTolerance()
    {
        // 用户选中的既有空文件：创建时间早于调用窗口，仍需覆盖确认。
        bool result = SaveFilePickerPlaceholder.IsPlaceholder(
            PickStartedAt - SaveFilePickerPlaceholder.TimestampTolerance - TimeSpan.FromSeconds(1),
            fileLengthBytes: 0,
            pickStartedAt: PickStartedAt);

        Assert.False(result);
    }

    [Fact]
    public void IsPlaceholder_ReturnsFalse_WhenFileHasContent()
    {
        // 既有文件被选中时内容不受 Picker 影响：有内容即说明不是占位文件，
        // 即使其创建时间落在调用窗口内也必须弹覆盖确认。
        bool result = SaveFilePickerPlaceholder.IsPlaceholder(
            PickStartedAt.AddMilliseconds(120),
            fileLengthBytes: 1024,
            pickStartedAt: PickStartedAt);

        Assert.False(result);
    }

    [Fact]
    public void IsPlaceholder_ReturnsFalse_ForLongExistingFile()
    {
        bool result = SaveFilePickerPlaceholder.IsPlaceholder(
            PickStartedAt.AddDays(-30),
            fileLengthBytes: 4096,
            pickStartedAt: PickStartedAt);

        Assert.False(result);
    }
}
