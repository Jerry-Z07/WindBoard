using System;
using System.Collections.Generic;
using WindBoard.Features.Import.Models;
using WindBoard.Localization;
using Windows.Storage;

namespace WindBoard.Features.Import.Services
{
    /// <summary>
    /// 导入队列分组（用于对话框右侧 TreeView 展示顺序与请求构建顺序）。
    /// </summary>
    internal enum ImportQueueGroup
    {
        Workspace,
        Image,
        Video,
        Audio,
        Text,
        Link,
        File,
    }

    /// <summary>
    /// 导入队列项类型（用于将队列项映射为具体导入行为）。
    /// </summary>
    internal enum ImportQueueItemKind
    {
        WorkspaceWbix,
        WorkspaceWbi,
        ImageFile,
        VideoFile,
        AudioFile,
        TextFile,
        InternetShortcutFile,
        GenericFile,
        TextContent,
        LinkUrl,
    }

    /// <summary>
    /// 导入队列项（作为 UI 展示与“提交请求构建”的共同数据结构）。
    /// </summary>
    internal sealed class ImportQueueItem
    {
        public required Guid Id { get; init; }

        public required ImportQueueItemKind Kind { get; init; }

        public required ImportQueueGroup Group { get; init; }

        public required string DisplayTitle { get; init; }

        public string? DisplaySubtitle { get; init; }

        public StorageFile? File { get; init; }

        public string? TextContent { get; init; }

        public string? Url { get; init; }

        /// <summary>
        /// 序号：用于稳定展示顺序（避免 Dictionary 枚举顺序不稳定）。
        /// </summary>
        public required long Sequence { get; init; }
    }

    internal enum ImportQueueAddFilesErrorKind
    {
        None,
        WorkspaceExclusive,
    }

    internal sealed record ImportQueueAddFilesResult(
        bool Success,
        ImportQueueAddFilesErrorKind Error,
        StorageFile? WorkspaceFile,
        ImportFileContentKind WorkspaceKind,
        int Added,
        int SkippedDuplicate,
        int SkippedInvalid,
        bool WorkspaceExclusiveWarning);

    internal enum ImportQueueAddTextErrorKind
    {
        None,
        WorkspaceExclusive,
        Empty,
    }

    internal sealed record ImportQueueAddTextResult(
        bool Success,
        ImportQueueAddTextErrorKind Error,
        int ContentLength);

    internal enum ImportQueueAddLinksErrorKind
    {
        None,
        WorkspaceExclusive,
        NoValidLinks,
    }

    internal sealed record ImportQueueAddLinksResult(
        bool Success,
        ImportQueueAddLinksErrorKind Error,
        int Parsed,
        int Added,
        int SkippedDuplicate);

    internal enum ImportQueueBuildErrorKind
    {
        InvalidWorkspace,
        NothingToImport,
    }

    internal sealed record ImportQueueBuildResult(
        bool Success,
        ImportDialogSubmission? Submission,
        ImportQueueBuildErrorKind? Error);

    /// <summary>
    /// 导入队列状态机：
    /// - 负责队列项的增删、去重与互斥规则；
    /// - 负责从队列构建最终提交结果（<see cref="ImportDialogSubmission"/>）。
    /// 
    /// 说明：
    /// - 该类不直接输出日志；日志与 UI 提示由对话框层统一处理；
    /// - 该类不持有任何 UI 控件引用，便于单测覆盖关键失败路径。
    /// </summary>
    internal sealed class ImportQueueState
    {
        internal static readonly ImportQueueGroup[] DisplayGroupOrder =
        {
            ImportQueueGroup.Workspace,
            ImportQueueGroup.Image,
            ImportQueueGroup.Video,
            ImportQueueGroup.Audio,
            ImportQueueGroup.Text,
            ImportQueueGroup.Link,
            ImportQueueGroup.File,
        };

        private readonly Dictionary<Guid, ImportQueueItem> _itemsById = new();

        private readonly HashSet<string> _filePathSet = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _urlSet = new(StringComparer.OrdinalIgnoreCase);

        private long _nextSequence = 1;

        public int Count => _itemsById.Count;

        public Guid? WorkspaceItemId { get; private set; }

        public Guid? TextContentItemId { get; private set; }

        public void Clear()
        {
            _itemsById.Clear();
            _filePathSet.Clear();
            _urlSet.Clear();
            WorkspaceItemId = null;
            TextContentItemId = null;
            _nextSequence = 1;
        }

        public bool TryRemove(Guid itemId, out ImportQueueItem? removed)
        {
            removed = null;

            if (!_itemsById.TryGetValue(itemId, out ImportQueueItem? item))
            {
                return false;
            }

            _itemsById.Remove(itemId);

            // 去重集合回收：保证移除后可再次加入相同文件/链接。
            if (item.File is StorageFile file && !string.IsNullOrWhiteSpace(file.Path))
            {
                _filePathSet.Remove(file.Path);
            }

            if (!string.IsNullOrWhiteSpace(item.Url))
            {
                _urlSet.Remove(item.Url);
            }

            if (item.Kind is ImportQueueItemKind.WorkspaceWbix or ImportQueueItemKind.WorkspaceWbi)
            {
                WorkspaceItemId = null;
            }

            if (item.Kind == ImportQueueItemKind.TextContent)
            {
                TextContentItemId = null;
            }

            removed = item;
            return true;
        }

        public IReadOnlyList<ImportQueueItem> GetItemsByGroup(ImportQueueGroup group)
        {
            var list = new List<ImportQueueItem>();

            foreach ((Guid _, ImportQueueItem item) in _itemsById)
            {
                if (item.Group == group)
                {
                    list.Add(item);
                }
            }

            list.Sort(static (a, b) => a.Sequence.CompareTo(b.Sequence));
            return list;
        }

        public ImportQueueAddFilesResult AddFiles(IReadOnlyList<StorageFile> files)
        {
            if (files is null || files.Count == 0)
            {
                return CreateEmptyAddFilesResult();
            }

            // 互斥规则：若已选择工作区文件，则禁止加入其它内容（除非新的选择中也包含工作区文件，用于“替换工作区文件”）。
            if (WorkspaceItemId is not null && !ContainsWorkspaceFile(files))
            {
                return new ImportQueueAddFilesResult(
                    Success: false,
                    Error: ImportQueueAddFilesErrorKind.WorkspaceExclusive,
                    WorkspaceFile: null,
                    WorkspaceKind: ImportFileContentKind.Other,
                    Added: 0,
                    SkippedDuplicate: 0,
                    SkippedInvalid: 0,
                    WorkspaceExclusiveWarning: false);
            }

            int countBefore = Count;
            if (TryFindFirstWorkspaceFile(files, out StorageFile? workspaceFile, out ImportFileContentKind workspaceKind))
            {
                bool shouldWarn = files.Count > 1 || countBefore > 0;
                return ReplaceWithWorkspaceFile(workspaceFile!, workspaceKind, shouldWarn);
            }

            (int added, int skippedDuplicate, int skippedInvalid) = AddNonWorkspaceFiles(files);
            return new ImportQueueAddFilesResult(
                Success: true,
                Error: ImportQueueAddFilesErrorKind.None,
                WorkspaceFile: null,
                WorkspaceKind: ImportFileContentKind.Other,
                Added: added,
                SkippedDuplicate: skippedDuplicate,
                SkippedInvalid: skippedInvalid,
                WorkspaceExclusiveWarning: false);
        }

        public ImportQueueAddTextResult AddText(string? raw)
        {
            if (WorkspaceItemId is not null)
            {
                return new ImportQueueAddTextResult(Success: false, Error: ImportQueueAddTextErrorKind.WorkspaceExclusive, ContentLength: 0);
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                return new ImportQueueAddTextResult(Success: false, Error: ImportQueueAddTextErrorKind.Empty, ContentLength: 0);
            }

            string content = raw.TrimEnd();

            if (TextContentItemId is Guid existingId && existingId != Guid.Empty)
            {
                _ = TryRemove(existingId, out _);
            }

            string title = BuildTextSummaryTitle(content);
            string subtitle = L10n.Format("ImportDialog_TextContent_Subtitle_Fmt", content.Length);

            var item = new ImportQueueItem
            {
                Id = Guid.NewGuid(),
                Kind = ImportQueueItemKind.TextContent,
                Group = ImportQueueGroup.Text,
                DisplayTitle = title,
                DisplaySubtitle = subtitle,
                TextContent = content,
                Sequence = _nextSequence++,
            };

            _itemsById[item.Id] = item;
            TextContentItemId = item.Id;

            return new ImportQueueAddTextResult(Success: true, Error: ImportQueueAddTextErrorKind.None, ContentLength: content.Length);
        }

        public ImportQueueAddLinksResult AddLinks(string? raw)
        {
            if (WorkspaceItemId is not null)
            {
                return new ImportQueueAddLinksResult(Success: false, Error: ImportQueueAddLinksErrorKind.WorkspaceExclusive, Parsed: 0, Added: 0, SkippedDuplicate: 0);
            }

            IReadOnlyList<string> urls = ImportUrlNormalizer.ParseAndNormalizeLinkLines(raw ?? string.Empty);
            if (urls.Count == 0)
            {
                return new ImportQueueAddLinksResult(Success: false, Error: ImportQueueAddLinksErrorKind.NoValidLinks, Parsed: 0, Added: 0, SkippedDuplicate: 0);
            }

            int added = 0;
            int skippedDuplicate = 0;

            for (int i = 0; i < urls.Count; i++)
            {
                string url = urls[i];
                if (!_urlSet.Add(url))
                {
                    skippedDuplicate++;
                    continue;
                }

                var item = new ImportQueueItem
                {
                    Id = Guid.NewGuid(),
                    Kind = ImportQueueItemKind.LinkUrl,
                    Group = ImportQueueGroup.Link,
                    DisplayTitle = url,
                    Url = url,
                    Sequence = _nextSequence++,
                };

                _itemsById[item.Id] = item;
                added++;
            }

            return new ImportQueueAddLinksResult(Success: true, Error: ImportQueueAddLinksErrorKind.None, Parsed: urls.Count, Added: added, SkippedDuplicate: skippedDuplicate);
        }

        public ImportQueueBuildResult TryBuildSubmission(ImportWbixMode mode, bool hasValidWorkspacePreview)
        {
            // 工作区导入：与元素导入互斥。
            if (WorkspaceItemId is Guid workspaceItemId && workspaceItemId != Guid.Empty)
            {
                return TryBuildWorkspaceSubmission(workspaceItemId, mode, hasValidWorkspacePreview);
            }

            return TryBuildElementsSubmission();
        }

        private static ImportQueueAddFilesResult CreateEmptyAddFilesResult()
        {
            return new ImportQueueAddFilesResult(
                Success: true,
                Error: ImportQueueAddFilesErrorKind.None,
                WorkspaceFile: null,
                WorkspaceKind: ImportFileContentKind.Other,
                Added: 0,
                SkippedDuplicate: 0,
                SkippedInvalid: 0,
                WorkspaceExclusiveWarning: false);
        }

        private static bool ContainsWorkspaceFile(IReadOnlyList<StorageFile> files)
        {
            for (int i = 0; i < files.Count; i++)
            {
                StorageFile f = files[i];
                ImportFileContentKind k = ImportFileTypeResolver.Resolve(f.Name);
                if (k is ImportFileContentKind.Wbix or ImportFileContentKind.Wbi)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindFirstWorkspaceFile(IReadOnlyList<StorageFile> files, out StorageFile? workspaceFile, out ImportFileContentKind workspaceKind)
        {
            workspaceFile = null;
            workspaceKind = ImportFileContentKind.Other;

            for (int i = 0; i < files.Count; i++)
            {
                StorageFile file = files[i];
                ImportFileContentKind kind = ImportFileTypeResolver.Resolve(file.Name);
                if (kind is ImportFileContentKind.Wbix or ImportFileContentKind.Wbi)
                {
                    workspaceFile = file;
                    workspaceKind = kind;
                    return true;
                }
            }

            return false;
        }

        private ImportQueueAddFilesResult ReplaceWithWorkspaceFile(StorageFile workspaceFile, ImportFileContentKind workspaceKind, bool shouldWarn)
        {
            Clear();

            ImportQueueItemKind itemKind = workspaceKind == ImportFileContentKind.Wbix
                ? ImportQueueItemKind.WorkspaceWbix
                : ImportQueueItemKind.WorkspaceWbi;

            var item = new ImportQueueItem
            {
                Id = Guid.NewGuid(),
                Kind = itemKind,
                Group = ImportQueueGroup.Workspace,
                DisplayTitle = workspaceFile.Name,
                DisplaySubtitle = workspaceFile.Path,
                File = workspaceFile,
                Sequence = _nextSequence++,
            };

            _itemsById[item.Id] = item;
            WorkspaceItemId = item.Id;

            return new ImportQueueAddFilesResult(
                Success: true,
                Error: ImportQueueAddFilesErrorKind.None,
                WorkspaceFile: workspaceFile,
                WorkspaceKind: workspaceKind,
                Added: 0,
                SkippedDuplicate: 0,
                SkippedInvalid: 0,
                WorkspaceExclusiveWarning: shouldWarn);
        }

        private (int added, int skippedDuplicate, int skippedInvalid) AddNonWorkspaceFiles(IReadOnlyList<StorageFile> files)
        {
            int added = 0;
            int skippedDuplicate = 0;
            int skippedInvalid = 0;

            for (int i = 0; i < files.Count; i++)
            {
                StorageFile file = files[i];
                if (string.IsNullOrWhiteSpace(file.Path))
                {
                    skippedInvalid++;
                    continue;
                }

                if (!_filePathSet.Add(file.Path))
                {
                    skippedDuplicate++;
                    continue;
                }

                ImportFileContentKind kind = ImportFileTypeResolver.Resolve(file.Name);
                (ImportQueueItemKind itemKind, ImportQueueGroup group) = MapFileContentKindToQueueItem(kind);

                var item = new ImportQueueItem
                {
                    Id = Guid.NewGuid(),
                    Kind = itemKind,
                    Group = group,
                    DisplayTitle = file.Name,
                    DisplaySubtitle = file.Path,
                    File = file,
                    Sequence = _nextSequence++,
                };

                _itemsById[item.Id] = item;
                added++;
            }

            return (added, skippedDuplicate, skippedInvalid);
        }

        private static (ImportQueueItemKind itemKind, ImportQueueGroup group) MapFileContentKindToQueueItem(ImportFileContentKind kind)
        {
            return kind switch
            {
                ImportFileContentKind.Image => (ImportQueueItemKind.ImageFile, ImportQueueGroup.Image),
                ImportFileContentKind.Video => (ImportQueueItemKind.VideoFile, ImportQueueGroup.Video),
                ImportFileContentKind.Audio => (ImportQueueItemKind.AudioFile, ImportQueueGroup.Audio),
                ImportFileContentKind.Text => (ImportQueueItemKind.TextFile, ImportQueueGroup.Text),
                ImportFileContentKind.UrlShortcut => (ImportQueueItemKind.InternetShortcutFile, ImportQueueGroup.Link),
                _ => (ImportQueueItemKind.GenericFile, ImportQueueGroup.File),
            };
        }

        private ImportQueueBuildResult TryBuildWorkspaceSubmission(Guid workspaceItemId, ImportWbixMode mode, bool hasValidWorkspacePreview)
        {
            if (!hasValidWorkspacePreview)
            {
                return new ImportQueueBuildResult(Success: false, Submission: null, Error: ImportQueueBuildErrorKind.InvalidWorkspace);
            }

            if (!_itemsById.TryGetValue(workspaceItemId, out ImportQueueItem? workspaceItem)
                || workspaceItem.File is not StorageFile workspaceFile)
            {
                return new ImportQueueBuildResult(Success: false, Submission: null, Error: ImportQueueBuildErrorKind.InvalidWorkspace);
            }

            ImportDialogSubmission? submission = workspaceItem.Kind switch
            {
                ImportQueueItemKind.WorkspaceWbix => new ImportDialogSubmission.Wbix(new ImportWbixRequest(workspaceFile, mode)),
                ImportQueueItemKind.WorkspaceWbi => new ImportDialogSubmission.Wbi(new ImportWbiRequest(workspaceFile, mode)),
                _ => null,
            };

            return submission is null
                ? new ImportQueueBuildResult(Success: false, Submission: null, Error: ImportQueueBuildErrorKind.InvalidWorkspace)
                : new ImportQueueBuildResult(Success: true, Submission: submission, Error: null);
        }

        private ImportQueueBuildResult TryBuildElementsSubmission()
        {
            var context = new ElementsSubmissionContext();

            for (int gi = 0; gi < DisplayGroupOrder.Length; gi++)
            {
                ImportQueueGroup group = DisplayGroupOrder[gi];
                IReadOnlyList<ImportQueueItem> items = GetItemsByGroup(group);

                for (int i = 0; i < items.Count; i++)
                {
                    ImportQueueItem item = items[i];
                    AppendElementsRequestData(item, context);
                }
            }

            string? linkLines = context.Links.Count > 0 ? string.Join('\n', context.Links) : null;
            int count = context.GetImportCount();

            if (count <= 0)
            {
                return new ImportQueueBuildResult(Success: false, Submission: null, Error: ImportQueueBuildErrorKind.NothingToImport);
            }

            var request = new ImportElementsRequest(
                context.ImageFiles,
                context.MediaFiles,
                context.TextFiles,
                context.OtherFiles,
                context.TextContent,
                linkLines);

            return new ImportQueueBuildResult(Success: true, Submission: new ImportDialogSubmission.Elements(request), Error: null);
        }

        private sealed class ElementsSubmissionContext
        {
            internal List<StorageFile> ImageFiles { get; } = new();

            internal List<StorageFile> MediaFiles { get; } = new();

            internal List<StorageFile> TextFiles { get; } = new();

            internal List<StorageFile> OtherFiles { get; } = new();

            internal List<string> Links { get; } = new();

            internal string? TextContent { get; set; }

            internal int GetImportCount()
            {
                return ImageFiles.Count
                    + MediaFiles.Count
                    + TextFiles.Count
                    + OtherFiles.Count
                    + (string.IsNullOrWhiteSpace(TextContent) ? 0 : 1)
                    + Links.Count;
            }
        }

        private static void AppendElementsRequestData(ImportQueueItem item, ElementsSubmissionContext context)
        {
            switch (item.Kind)
            {
                case ImportQueueItemKind.ImageFile:
                    if (item.File is not null)
                    {
                        context.ImageFiles.Add(item.File);
                    }
                    break;
                case ImportQueueItemKind.VideoFile:
                case ImportQueueItemKind.AudioFile:
                    if (item.File is not null)
                    {
                        context.MediaFiles.Add(item.File);
                    }
                    break;
                case ImportQueueItemKind.TextFile:
                case ImportQueueItemKind.InternetShortcutFile:
                    if (item.File is not null)
                    {
                        context.TextFiles.Add(item.File);
                    }
                    break;
                case ImportQueueItemKind.GenericFile:
                    if (item.File is not null)
                    {
                        context.OtherFiles.Add(item.File);
                    }
                    break;
                case ImportQueueItemKind.TextContent:
                    if (!string.IsNullOrWhiteSpace(item.TextContent))
                    {
                        context.TextContent = item.TextContent;
                    }
                    break;
                case ImportQueueItemKind.LinkUrl:
                    if (!string.IsNullOrWhiteSpace(item.Url))
                    {
                        context.Links.Add(item.Url);
                    }
                    break;
            }
        }

        private static string BuildTextSummaryTitle(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return L10n.Get("ImportDialog_Tab_Text");
            }

            // 取首个非空行作为摘要标题，避免队列项标题过长影响布局。
            string[] lines = content.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                const int maxLen = 60;
                return line.Length <= maxLen ? line : line.Substring(0, maxLen) + "…";
            }

            return L10n.Get("ImportDialog_Tab_Text");
        }
    }
}
