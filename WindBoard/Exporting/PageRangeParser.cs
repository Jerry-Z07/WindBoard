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

            if (pageCount <= 0)
            {
                errorMessage = "页面数量非法。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                errorMessage = "请输入页码范围，例如：1,3-5。";
                return false;
            }

            // 设计说明：
            // - 导出页选择属于用户输入，容错与报错信息比“极致性能”更重要；
            // - 这里使用 SortedSet 去重 + 排序，避免重复页导致重复导出，也保证输出稳定。
            var indices = new SortedSet<int>();

            string[] parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                errorMessage = "请输入页码范围，例如：1,3-5。";
                return false;
            }

            foreach (string raw in parts)
            {
                string token = raw.Trim();
                if (token.Length == 0)
                {
                    continue;
                }

                int dashIndex = token.IndexOf('-');
                if (dashIndex < 0)
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
                    continue;
                }

                // 范围：a-b。只允许一个 '-'，避免类似 '1-2-3' 的歧义输入。
                if (token.LastIndexOf('-') != dashIndex)
                {
                    errorMessage = $"非法页码范围：{token}。";
                    return false;
                }

                string startText = token[..dashIndex].Trim();
                string endText = token[(dashIndex + 1)..].Trim();

                if (startText.Length == 0 || endText.Length == 0)
                {
                    errorMessage = $"非法页码范围：{token}。";
                    return false;
                }

                if (!TryParsePageNumber(startText, out int startOneBased, out errorMessage))
                {
                    return false;
                }

                if (!TryParsePageNumber(endText, out int endOneBased, out errorMessage))
                {
                    return false;
                }

                if (startOneBased > endOneBased)
                {
                    errorMessage = $"页码范围起止顺序错误：{token}。";
                    return false;
                }

                if (!ValidateOneBasedRange(startOneBased, endOneBased, pageCount, out errorMessage))
                {
                    return false;
                }

                for (int i = startOneBased; i <= endOneBased; i++)
                {
                    indices.Add(i - 1);
                }
            }

            if (indices.Count == 0)
            {
                errorMessage = "未解析到任何有效页码。";
                return false;
            }

            pageIndices.AddRange(indices);
            return true;
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
