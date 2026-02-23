using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using WindBoard.Logging;
using WindBoard.Localization;

namespace WindBoard.Features.Import.Services
{
    /// <summary>
    /// 文本导入读取器：以字符数为上限读取文本文件，避免大文件导致 UI 卡顿或内存占用过高。
    /// </summary>
    internal static class TextImportReader
    {
        /// <summary>
        /// 读取文本文件，并限制最大字符数（超过则截断并追加提示）。
        /// </summary>
        public static async Task<string> ReadTextFileWithLimitAsync(string path, int maxChars)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

                var sb = new StringBuilder(Math.Min(maxChars, 4096));
                char[] buffer = new char[2048];

                int remaining = Math.Max(0, maxChars);
                while (remaining > 0)
                {
                    int read = await reader.ReadAsync(buffer, 0, Math.Min(buffer.Length, remaining));
                    if (read <= 0)
                    {
                        break;
                    }

                    sb.Append(buffer, 0, read);
                    remaining -= read;
                }

                // 如果已读满上限，再尝试读取 1 个字符判断是否还有内容（避免在 async 方法中使用 EndOfStream）。
                if (remaining == 0 && await reader.ReadAsync(buffer, 0, 1) > 0)
                {
                    sb.Append("\n\n");
                    sb.Append(L10n.Get("Import_TextTruncated_Notice"));
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                AppLog.Error("Import", $"读取文本失败：'{path}'", ex);
                return L10n.Get("Import_TextReadFailed_Placeholder");
            }
        }
    }
}
