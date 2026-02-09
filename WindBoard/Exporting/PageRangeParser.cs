using System;
using System.Collections.Generic;

namespace WindBoard.Exporting
{
    /// <summary>
    /// 页码范围解析器（1 基输入 → 0 基索引）。
    /// 
    /// 支持格式示例：
    /// - 1
    /// - 1,3,5
    /// - 2-6
    /// - 1,3-5,8
    /// </summary>
    internal static class PageRangeParser
    {
        public static bool TryParse(string? text, int pageCount, out List<int> pageIndices, out string errorMessage)
        {
            pageIndices = new List<int>();
            errorMessage = string.Empty;

            // 设计说明：
            // - 导出页选择属于用户输入，容错与报错信息比“极致性能”更重要；
            // - 这里使用 SortedSet 去重 + 排序，避免重复页导致重复导出，也保证输出稳定。
            var indices = new SortedSet<int>();

            bool ok = true;
            string[] parts = Array.Empty<string>();

            if (pageCount <= 0)
            {
                errorMessage = "页面数量非法。";
                ok = false;
            }

            if (ok && string.IsNullOrWhiteSpace(text))
            {
                errorMessage = "请输入页码范围，例如：1,3-5。";
                ok = false;
            }

            if (ok)
            {
                parts = text!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    errorMessage = "请输入页码范围，例如：1,3-5。";
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
                errorMessage = "未解析到任何有效页码。";
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
                errorMessage = $"非法页码范围：{token}。";
                ok = false;
            }

            if (ok)
            {
                startText = token[..dashIndex].Trim();
                endText = token[(dashIndex + 1)..].Trim();
                if (startText.Length == 0 || endText.Length == 0)
                {
                    errorMessage = $"非法页码范围：{token}。";
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
                errorMessage = $"页码范围起止顺序错误：{token}。";
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
                errorMessage = $"非法页码：{token}。";
                return false;
            }

            if (oneBased <= 0)
            {
                errorMessage = $"页码必须从 1 开始：{token}。";
                return false;
            }

            return true;
        }

        private static bool ValidateOneBasedRange(int startOneBased, int endOneBased, int pageCount, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (startOneBased <= 0 || endOneBased <= 0)
            {
                errorMessage = "页码必须从 1 开始。";
                return false;
            }

            if (startOneBased > pageCount || endOneBased > pageCount)
            {
                errorMessage = $"页码超出范围：当前共 {pageCount} 页。";
                return false;
            }

            return true;
        }
    }
}
