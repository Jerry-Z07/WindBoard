using System;
using System.Collections.Generic;
using WindBoard.Localization;

namespace WindBoard.Features.Export.Services
{
    /// <summary>
    /// 页码范围解析器，输入 1 基页码并输出 0 基索引。
    /// 支持单页、逗号分隔（1,3,5）、区间（2-6）及其组合（1,3-5,8）。
    /// </summary>
    internal static class PageRangeParser
    {
        public static bool TryParse(string? text, int pageCount, out List<int> pageIndices, out string errorMessage)
        {
            pageIndices = new List<int>();
            errorMessage = string.Empty;

            // 页选择来自用户输入，容错与报错信息优先于性能；SortedSet 去重并排序，避免重复页重复导出并保证输出稳定。
            var indices = new SortedSet<int>();

            bool ok = true;
            string[] parts = [];

            if (pageCount <= 0)
            {
                errorMessage = L10n.Get("Export_PageRange_PageCountInvalid_Message");
                ok = false;
            }

            if (ok && string.IsNullOrWhiteSpace(text))
            {
                errorMessage = L10n.Get("Export_PageRange_EmptyInput_Message");
                ok = false;
            }

            if (ok)
            {
                parts = text!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    errorMessage = L10n.Get("Export_PageRange_EmptyInput_Message");
                    ok = false;
                }
            }

            if (ok)
            {
                foreach (string raw in parts)
                {
                    if (!TryParseTokenAndAdd(indices, raw, pageCount, out errorMessage))
                    {
                        ok = false;
                        break;
                    }
                }
            }

            if (ok && indices.Count == 0)
            {
                errorMessage = L10n.Get("Export_PageRange_NoValidPages_Message");
                ok = false;
            }

            if (ok)
            {
                pageIndices.AddRange(indices);
            }

            return ok;
        }

        private static bool TryParseTokenAndAdd(
            SortedSet<int> indices,
            string raw,
            int pageCount,
            out string errorMessage)
        {
            errorMessage = string.Empty;

            string token = raw.Trim();
            if (token.Length == 0)
            {
                // Split 已经做了 RemoveEmptyEntries，这里再兜一层，避免未来改动引入空项。
                return true;
            }

            int dashIndex = token.IndexOf('-');
            if (dashIndex < 0)
            {
                return TryParseSinglePageAndAdd(indices, token, pageCount, out errorMessage);
            }

            return TryParseRangeAndAdd(indices, token, dashIndex, pageCount, out errorMessage);
        }

        private static bool TryParseSinglePageAndAdd(
            SortedSet<int> indices,
            string token,
            int pageCount,
            out string errorMessage)
        {
            if (!TryParsePageNumber(token, out int oneBased, out errorMessage))
            {
                return false;
            }

            if (!ValidateOneBasedRange(oneBased, oneBased, pageCount, out errorMessage))
            {
                return false;
            }

            indices.Add(oneBased - 1);
            return true;
        }

        private static bool TryParseRangeAndAdd(
            SortedSet<int> indices,
            string token,
            int dashIndex,
            int pageCount,
            out string errorMessage)
        {
            errorMessage = string.Empty;

            bool ok = true;
            int startOneBased = 0;
            int endOneBased = 0;
            string startText = string.Empty;
            string endText = string.Empty;

            // 范围：a-b。只允许一个 '-'，避免类似 '1-2-3' 的歧义输入。
            if (token.LastIndexOf('-') != dashIndex)
            {
                errorMessage = L10n.Format("Export_PageRange_InvalidRange_Fmt", token);
                ok = false;
            }

            if (ok)
            {
                startText = token[..dashIndex].Trim();
                endText = token[(dashIndex + 1)..].Trim();
                if (startText.Length == 0 || endText.Length == 0)
                {
                    errorMessage = L10n.Format("Export_PageRange_InvalidRange_Fmt", token);
                    ok = false;
                }
            }

            if (ok)
            {
                ok = TryParsePageNumber(startText, out startOneBased, out errorMessage)
                    && TryParsePageNumber(endText, out endOneBased, out errorMessage);
            }

            if (ok && startOneBased > endOneBased)
            {
                errorMessage = L10n.Format("Export_PageRange_OrderError_Fmt", token);
                ok = false;
            }

            if (ok)
            {
                ok = ValidateOneBasedRange(startOneBased, endOneBased, pageCount, out errorMessage);
            }

            if (ok)
            {
                for (int i = startOneBased; i <= endOneBased; i++)
                {
                    indices.Add(i - 1);
                }
            }

            return ok;
        }

        private static bool TryParsePageNumber(string token, out int oneBased, out string errorMessage)
        {
            oneBased = 0;
            errorMessage = string.Empty;

            if (!int.TryParse(token, out oneBased))
            {
                errorMessage = L10n.Format("Export_PageRange_InvalidPage_Fmt", token);
                return false;
            }

            if (oneBased <= 0)
            {
                errorMessage = L10n.Format("Export_PageRange_PageMustStartFrom1_Fmt", token);
                return false;
            }

            return true;
        }

        private static bool ValidateOneBasedRange(int startOneBased, int endOneBased, int pageCount, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (startOneBased <= 0 || endOneBased <= 0)
            {
                errorMessage = L10n.Get("Export_PageRange_PageMustStartFrom1_Message");
                return false;
            }

            if (startOneBased > pageCount || endOneBased > pageCount)
            {
                errorMessage = L10n.Format("Export_PageRange_OutOfRange_Fmt", pageCount);
                return false;
            }

            return true;
        }
    }
}
