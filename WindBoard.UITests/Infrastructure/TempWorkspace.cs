using System;
using System.IO;

namespace WindBoard.UITests.Infrastructure
{
    /// <summary>
    /// 用例级临时目录：在 %TEMP% 下按 GUID 隔离，Dispose 时整体清理（导入样例/导出产物均放这里）。
    /// </summary>
    public sealed class TempWorkspace : IDisposable
    {
        public string RootDirectory { get; }

        public TempWorkspace()
        {
            RootDirectory = Path.Combine(
                Path.GetTempPath(),
                "windboard-uitests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootDirectory);
        }

        public string CreateSubDirectory(string name)
        {
            string path = Path.Combine(RootDirectory, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootDirectory))
                {
                    Directory.Delete(RootDirectory, recursive: true);
                }
            }
            catch (Exception ex)
            {
                // 清理失败不掩盖用例结果：仅提示残留位置。
                Console.Error.WriteLine($"[UITests] 临时目录清理失败：'{RootDirectory}', {ex.Message}");
            }
        }
    }
}
