using C3Studio.Core.Models;
using System.IO;

namespace C3Studio.Core.Services;

public interface IAssetExportService
{
    /// <summary>
    /// Exports all files referenced by <paramref name="data"/> into <paramref name="destFolder"/>.
    /// Folder structure mirrors the relative paths known to <see cref="IAssetFileService"/>.
    /// </summary>
    /// <param name="data">Asset data from the selected node.</param>
    /// <param name="destFolder">Absolute path to the export root.</param>
    /// <param name="includeMotions">Whether to also export animation files.</param>
    /// <param name="conflictMode">How to handle files that already exist at the destination.</param>
    /// <param name="progress">Optional — receives a status string per file written.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Paths of every file successfully written (relative to <paramref name="destFolder"/>).</returns>
    Task<ExportResult> ExportNodeAsync(
        AssetData data,
        string assetLabel,
        string destFolder,
        ExportLayout layout = ExportLayout.NamedFolder,
        bool includeMotions = true,
        ExportConflict conflictMode = ExportConflict.Skip,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}

// ── Supporting types ──────────────────────────────────────────────────────────

public enum ExportConflict
{
    Skip,       // leave the existing file untouched
    Overwrite,  // always replace
    Rename,     // write as filename_1.ext, filename_2.ext …
}

public enum ExportLayout
{
    /// <summary>destFolder\{Label}\...relative path...  e.g. out\ArcherNpc\npc\archer\body.c3</summary>
    NamedFolder,
    /// <summary>destFolder\{Label}\filename  e.g. out\ArcherNpc\body.c3  (flat, no sub-dirs)</summary>
    NamedFolderFlat,
    /// <summary>destFolder\...relative path...  e.g. out\npc\archer\body.c3</summary>
    Direct,
    /// <summary>destFolder\filename  e.g. out\body.c3  (flat, no sub-dirs)</summary>
    DirectFlat,
}

public sealed class ExportResult
{
    public IReadOnlyList<string> Written { get; init; } = [];
    public IReadOnlyList<string> Skipped { get; init; } = [];
    public IReadOnlyList<(string Path, string Reason)> Failed { get; init; } = [];

    public int TotalAttempted => Written.Count + Skipped.Count + Failed.Count;
    public bool HasErrors => Failed.Count > 0;

    public override string ToString() =>
        $"Exported {Written.Count} file(s)" +
        (Skipped.Count > 0 ? $", {Skipped.Count} skipped" : "") +
        (Failed.Count > 0 ? $", {Failed.Count} failed" : "") + ".";
}

/// <summary>
/// Copies asset files (meshes, textures, optionally motions) to a local folder,
/// reading them through <see cref="IAssetFileService"/> so WDF and loose-file
/// workspaces are both supported transparently.
///
/// Folder structure is preserved: the relative path returned by
/// <see cref="IAssetFileService"/> becomes the sub-path under <paramref name="destFolder"/>.
/// </summary>
public sealed class AssetExportService : IAssetExportService
{
    private readonly IAssetFileService _assets;

    public AssetExportService(IAssetFileService assets) => _assets = assets;

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<ExportResult> ExportNodeAsync(
        AssetData data,
        string assetLabel,
        string destFolder,
        ExportLayout layout = ExportLayout.NamedFolder,
        bool includeMotions = true,
        ExportConflict conflictMode = ExportConflict.Skip,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var written = new List<string>();
        var skipped = new List<string>();
        var failed = new List<(string, string)>();

        // Named layouts create a sub-folder named after the asset label
        string root = layout is ExportLayout.NamedFolder or ExportLayout.NamedFolderFlat
            ? Path.Combine(destFolder, SanitizeFolderName(assetLabel))
            : destFolder;

        bool flat = layout is ExportLayout.NamedFolderFlat or ExportLayout.DirectFlat;

        foreach (var relativePath in CollectPaths(data, includeMotions))
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(relativePath)) continue;

            try
            {
                string destPath = ResolveDestPath(root, relativePath, flat, conflictMode);

                if (destPath == string.Empty)   // Skip mode — file already exists
                {
                    skipped.Add(relativePath);
                    progress?.Report($"[skip]  {relativePath}");
                    continue;
                }

                await CopyAssetAsync(relativePath, destPath, ct);
                written.Add(relativePath);
                progress?.Report($"[write] {relativePath}");
            }
            catch (Exception ex)
            {
                failed.Add((relativePath, ex.Message));
                progress?.Report($"[error] {relativePath}: {ex.Message}");
            }
        }

        return new ExportResult { Written = written, Skipped = skipped, Failed = failed };
    }

    // ── Path collection ───────────────────────────────────────────────────────

    private static IEnumerable<string> CollectPaths(AssetData data, bool includeMotions)
    {
        // Deduplicate: same file may appear in multiple parts (e.g. shared texture)
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in data.MeshPaths)
            if (seen.Add(p)) yield return p;

        foreach (var p in data.TexturePaths)
            if (seen.Add(p)) yield return p;

        if (includeMotions)
            foreach (var m in data.Motions)
                if (seen.Add(m.Path)) yield return m.Path;
    }

    // ── Destination path resolution ───────────────────────────────────────────

    /// <summary>
    /// Returns the absolute destination path to write, or <c>string.Empty</c> when
    /// the file exists and <paramref name="mode"/> is <see cref="ExportConflict.Skip"/>.
    /// </summary>
    private static string ResolveDestPath(string root, string relativePath,
                                          bool flat, ExportConflict mode)
    {
        string subPath = flat
            ? Path.GetFileName(relativePath)
            : relativePath.Replace('/', Path.DirectorySeparatorChar)
                          .TrimStart(Path.DirectorySeparatorChar);

        string full = Path.GetFullPath(Path.Combine(root, subPath));

        // Guard against path traversal
        if (!full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Relative path '{relativePath}' would escape the export folder.");

        if (!File.Exists(full)) return full;

        return mode switch
        {
            ExportConflict.Overwrite => full,
            ExportConflict.Skip => string.Empty,
            ExportConflict.Rename => BuildRenamedPath(full),
            _ => full,
        };
    }

    private static string SanitizeFolderName(string label)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(label.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
    }

    private static string BuildRenamedPath(string full)
    {
        string dir = Path.GetDirectoryName(full)!;
        string stem = Path.GetFileNameWithoutExtension(full);
        string ext = Path.GetExtension(full);
        int index = 1;
        string candidate;
        do { candidate = Path.Combine(dir, $"{stem}_{index++}{ext}"); }
        while (File.Exists(candidate));
        return candidate;
    }

    // ── Stream copy ───────────────────────────────────────────────────────────

    private async Task CopyAssetAsync(string relativePath, string destPath,
                                      CancellationToken ct)
    {
        string? dir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using Stream src = _assets.Open(relativePath);
        using Stream dest = File.Create(destPath);
        await src.CopyToAsync(dest, bufferSize: 81_920, ct);
    }
}