using System;
using System.IO;
using System.IO.Compression;

namespace WindBoard.UITests
{
    /// <summary>
    /// 构造测试用 WBIX 文件（v3 格式，见 docs/dev/guides/wbix.zh-CN.md）：
    /// manifest.json + 每页 page-XXX.json（空 strokes/elements）。
    /// </summary>
    internal static class WbixTestFileFactory
    {
        /// <summary>生成指定页数的 WBIX 文件，返回文件路径。</summary>
        public static string CreateWbix(string directory, int pageCount, string fileName = "e2e-test.wbix")
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageCount, nameof(pageCount));

            string path = Path.Combine(directory, fileName);
            using FileStream stream = new(path, FileMode.CreateNew);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

            var pages = new string[pageCount];
            for (int i = 0; i < pageCount; i++)
            {
                string pageId = Guid.NewGuid().ToString("D");
                pages[i] =
                    "{ \"id\": \"" + pageId + "\", \"index\": " + i + ", \"path\": \"pages/page-"
                    + i.ToString("000", System.Globalization.CultureInfo.InvariantCulture) + ".json\" }";

                ZipArchiveEntry pageEntry = zip.CreateEntry($"pages/page-{i:000}.json");
                using (var writer = new StreamWriter(pageEntry.Open()))
                {
                    // 页面载荷：空笔迹与元素即可满足“页数变化”的导入断言。
                    writer.Write("{ \"id\": \"" + pageId + "\", \"strokes\": [], \"elements\": [] }");
                }
            }

            ZipArchiveEntry manifestEntry = zip.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open()))
            {
                writer.Write(
                    "{ \"format\": \"wbix\", \"version\": 3, \"createdUtc\": \""
                    + DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                    + "\", \"currentIndex\": 0, \"pages\": [ "
                    + string.Join(", ", pages)
                    + " ] }");
            }

            return path;
        }
    }
}
