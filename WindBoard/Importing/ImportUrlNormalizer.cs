using System;
using System.Collections.Generic;

namespace WindBoard.Importing
{
    /// <summary>
    /// 导入链接的归一化与解析工具。
    /// </summary>
    internal static class ImportUrlNormalizer
    {
        internal static bool TryNormalizeHttpUrl(string raw, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            raw = raw.Trim();

            // 仅允许 http/https：
            // - 没写 scheme（例如 example.com）时，默认补 https://
            // - 写了 scheme（包含 ://）但不是 http(s) 时，直接拒绝，避免把 ftp:// 等错误拼接成 https://ftp://...
            if (raw.Contains("://", StringComparison.Ordinal))
            {
                if (!raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    && !raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            else
            {
                raw = "https://" + raw;
            }

            if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri))
            {
                return false;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            normalized = uri.ToString();
            return true;
        }

        internal static IReadOnlyList<string> ParseAndNormalizeLinkLines(string? linkLines)
        {
            if (string.IsNullOrWhiteSpace(linkLines))
            {
                return Array.Empty<string>();
            }

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();

            string[] lines = linkLines.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (!TryNormalizeHttpUrl(line, out string normalized))
                {
                    continue;
                }

                if (set.Add(normalized))
                {
                    list.Add(normalized);
                }
            }

            return list;
        }
    }
}
