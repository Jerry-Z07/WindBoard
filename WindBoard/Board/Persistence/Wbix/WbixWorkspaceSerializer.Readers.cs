using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WindBoard.Board.Persistence;
using WindBoard.Localization;

namespace WindBoard.Board.Persistence.Wbix
{
    /// <summary>
    /// 读侧版本分派：把 <c>manifest.version</c> 映射到对应的页读取器，是版本契约的唯一落点，写侧只写 <see cref="CurrentVersion"/>。
    /// 未知版本（&lt;= 0 或 &gt; <see cref="CurrentVersion"/>）抛 <see cref="InvalidDataException"/>；破坏性变更需新增 reader、提升 <see cref="CurrentVersion"/> 并补 golden 语料，既有 reader 不改。
    /// </summary>
    internal sealed partial class WbixWorkspaceSerializer
    {
        /// <summary>
        /// 版本 → 页读取器注册表。
        /// </summary>
        private static class WbixPageReaderRegistry
        {
            // 注册顺序即匹配顺序；版本区间不得重叠。
            private static readonly IWbixPageReader[] Readers =
            {
                new WbixPageReaderV1V2(),
                new WbixPageReaderV3(),
            };

            /// <summary>
            /// 取得读取指定版本文件的页读取器；版本不受支持时抛出与旧版本门禁一致的异常。
            /// </summary>
            public static IWbixPageReader For(int version)
            {
                foreach (IWbixPageReader reader in Readers)
                {
                    if (version >= reader.MinVersion && version <= reader.MaxVersion)
                    {
                        return reader;
                    }
                }

                throw new InvalidDataException(L10n.Format("Wbix_VersionNotSupported_Fmt", version));
            }
        }

        /// <summary>
        /// 单页读取器：把 Zip 内一页的 JSON 反序列化为 <see cref="BoardPageSnapshot"/>。
        /// </summary>
        private interface IWbixPageReader
        {
            /// <summary>支持的最低版本（含）。</summary>
            int MinVersion { get; }

            /// <summary>支持的最高版本（含）。</summary>
            int MaxVersion { get; }

            /// <summary>读取一页。</summary>
            ValueTask<BoardPageSnapshot> ReadPageAsync(
                WbixLoadContext loadContext,
                WbixManifestPage pageEntry,
                CancellationToken cancellationToken);
        }

        /// <summary>
        /// 页读取器公共实现：路径规范化 → 安全校验 → 取条目 → 按读模式反序列化 → 元素解析。
        /// </summary>
        private abstract class WbixPageReaderBase : IWbixPageReader
        {
            public abstract int MinVersion { get; }

            public abstract int MaxVersion { get; }

            /// <summary>该版本文件使用的笔迹条目读模式。</summary>
            protected abstract InkItemReadMode ReadMode { get; }

            public virtual async ValueTask<BoardPageSnapshot> ReadPageAsync(
                WbixLoadContext loadContext,
                WbixManifestPage pageEntry,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string pagePath = NormalizeZipPath(pageEntry.Path);
                if (!IsSafeZipPath(pagePath, requiredPrefix: $"{PagesFolder}/"))
                {
                    throw new InvalidDataException(L10n.Format("Wbix_MissingPageFile_Fmt", pageEntry.Path));
                }

                ZipArchiveEntry? pageZipEntry = loadContext.Archive.GetEntry(pagePath);
                if (pageZipEntry is null)
                {
                    throw new InvalidDataException(L10n.Format("Wbix_MissingPageFile_Fmt", pageEntry.Path));
                }

                WbixPagePayload payload;
                await using (Stream pageStream = pageZipEntry.Open())
                {
                    payload = await JsonSerializer
                        .DeserializeAsync<WbixPagePayload>(pageStream, loadContext.GetPageJsonOptions(ReadMode), cancellationToken)
                        .ConfigureAwait(false)
                        ?? throw new InvalidDataException(L10n.Format("Wbix_PageParseFailed_Fmt", pagePath));
                }

                IReadOnlyList<InkItemSnapshot> strokes = payload.Strokes ?? Array.Empty<InkItemSnapshot>();
                (IReadOnlyList<BoardElementSnapshot> below, IReadOnlyList<BoardElementSnapshot> above) = loadContext.ParseElements(payload.Elements);

                return new BoardPageSnapshot(payload.Id, strokes, below, above);
            }
        }

        /// <summary>
        /// v1/v2：<c>strokes</c> 条目为扁平形态（points/colorRgba 等字段直接在条目上）。
        /// </summary>
        private sealed class WbixPageReaderV1V2 : WbixPageReaderBase
        {
            public override int MinVersion => 1;

            public override int MaxVersion => 2;

            protected override InkItemReadMode ReadMode => InkItemReadMode.LegacyFlat;
        }

        /// <summary>
        /// v3：<c>strokes</c> 条目为 <c>{ kind, stroke|shape }</c> 包装形态；
        /// 同时容忍混入的扁平条目（按 <c>kind=stroke</c> 兜底读取，每个文件告警一次）。
        /// </summary>
        private sealed class WbixPageReaderV3 : WbixPageReaderBase
        {
            public override int MinVersion => 3;

            public override int MaxVersion => 3;

            protected override InkItemReadMode ReadMode => InkItemReadMode.Wrapped;
        }
    }

    /// <summary>
    /// 「v3 文件出现扁平笔迹条目」的告警门闩：一个实例对应一次导入（一个文件），
    /// 保证兜底路径最多告警一次，避免超大文件逐条刷日志。
    /// </summary>
    internal sealed class WbixLegacyFlatItemWarningGate
    {
        private int _entered;

        /// <summary>
        /// 尝试进入告警分支：首次返回 true，之后一律返回 false（线程安全）。
        /// </summary>
        public bool TryEnter()
        {
            return Interlocked.Exchange(ref _entered, 1) == 0;
        }
    }
}
