using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using WindBoard.Features.Import.Models;
using WindBoard.Features.Import.Services;
using Windows.Storage;
using Xunit;

namespace WindBoard.Tests.Features.Import;

public sealed class ImportQueueStateTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (string file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }
    }

    private string GetTempFilePath(string ext)
    {
        string path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}{ext}");
        _tempFiles.Add(path);
        return path;
    }

    private async Task<StorageFile> CreateTempStorageFileAsync(string ext)
    {
        string path = GetTempFilePath(ext);
        File.WriteAllBytes(path, Array.Empty<byte>());
        return await StorageFile.GetFileFromPathAsync(path);
    }

    [Fact]
    public async Task AddFiles_WhenWorkspaceSelected_RejectsNonWorkspace()
    {
        var queue = new ImportQueueState();

        StorageFile workspace = await CreateTempStorageFileAsync(".wbix");
        ImportQueueAddFilesResult r1 = queue.AddFiles(new[] { workspace });

        Assert.True(r1.Success);
        Assert.NotNull(queue.WorkspaceItemId);
        Assert.Equal(1, queue.Count);

        StorageFile image = await CreateTempStorageFileAsync(".png");
        ImportQueueAddFilesResult r2 = queue.AddFiles(new[] { image });

        Assert.False(r2.Success);
        Assert.Equal(ImportQueueAddFilesErrorKind.WorkspaceExclusive, r2.Error);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void TryBuildSubmission_WhenEmpty_ReturnsNothingToImport()
    {
        var queue = new ImportQueueState();

        ImportQueueBuildResult result = queue.TryBuildSubmission(ImportWbixMode.AppendAfterLastPage, hasValidWorkspacePreview: false);

        Assert.False(result.Success);
        Assert.Equal(ImportQueueBuildErrorKind.NothingToImport, result.Error);
        Assert.Null(result.Submission);
    }
}

