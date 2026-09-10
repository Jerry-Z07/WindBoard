using System;
using System.IO;
using Windows.Storage;
using WindBoard.Logging;

namespace WindBoard.UI.Common
{
    /// <summary>
    /// 识别 <c>FileSavePicker.PickSaveFileAsync()</c> 为用户输入的新文件名预创建的空文件。
    ///
    /// 背景：该 API 会在返回 <see cref="StorageFile"/> 之前，先把目标文件创建到磁盘（0 字节占位文件）；
    /// 新旧两代 Picker（<c>Windows.Storage.Pickers</c> / <c>Microsoft.Windows.Storage.Pickers</c>）
    /// 都存在该行为，且实测依环境而异（部分环境不预创建）。
    /// 因此“路径上是否已有文件”无法区分“用户新建了文件”与“用户选中了已存在的同名文件”——
    /// 单看 <see cref="File.Exists(string)"/> 会把每次新建都误判为覆盖，弹出多余的覆盖确认。
    ///
    /// 判据：
    /// - 占位文件一定为 0 字节；
    /// - 占位文件的创建时间落在本次 Picker 调用窗口内；用户选中的既有文件创建时间早于调用时刻
    ///   （Windows 只在文件被创建时写入创建时间，后续写入/截断不会刷新该时间）。
    ///
    /// 不预创建占位文件的环境下，调用方应先判 <see cref="File.Exists(string)"/> 为 false 并直接返回；
    /// 本判据只用于“文件确实存在”时进一步区分。
    /// </summary>
    internal static class SaveFilePickerPlaceholder
    {
        /// <summary>
        /// 创建时间比较容差：部分卷（FAT/exFAT）创建时间精度为秒级，同时容忍系统时钟微小回拨。
        /// </summary>
        internal static readonly TimeSpan TimestampTolerance = TimeSpan.FromSeconds(2);

        /// <summary>
        /// 判断目标文件是否为本次 Picker 调用预创建的空文件。
        /// </summary>
        /// <param name="file">本次 Picker 返回的文件。</param>
        /// <param name="pickStartedAt">调用 Picker 之前记录的时刻。</param>
        internal static bool IsCreatedByPicker(StorageFile file, DateTimeOffset pickStartedAt)
        {
            ArgumentNullException.ThrowIfNull(file);

            try
            {
                return IsPlaceholder(file.DateCreated, new FileInfo(file.Path).Length, pickStartedAt);
            }
            catch (Exception ex)
            {
                // 读取文件信息失败时按“已存在的文件”处理：宁可多一次覆盖确认，也不静默覆盖用户文件。
                AppLog.Warn("SaveFilePicker", $"读取目标文件信息失败，按已存在文件处理：path='{file.Path}'", ex);
                return false;
            }
        }

        /// <summary>
        /// 判据本体（纯函数，便于单测覆盖边界）。
        /// </summary>
        /// <param name="fileCreatedAt">目标文件的创建时间。</param>
        /// <param name="fileLengthBytes">目标文件当前长度。</param>
        /// <param name="pickStartedAt">调用 Picker 之前记录的时刻。</param>
        internal static bool IsPlaceholder(DateTimeOffset fileCreatedAt, long fileLengthBytes, DateTimeOffset pickStartedAt)
        {
            return fileLengthBytes == 0 && fileCreatedAt >= pickStartedAt - TimestampTolerance;
        }
    }
}
