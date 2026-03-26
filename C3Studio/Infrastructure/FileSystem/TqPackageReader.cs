using SevenZipExtractor;
using System.IO;

namespace C3Studio.Infrastructure.FileSystem;

/// <summary>
/// Top-level Conquer Online asset resolver.
/// <para>
/// Resolution order for a given <paramref name="fileName"/>:
/// <list type="number">
///   <item>Absolute path on the filesystem (file exists as-is)</item>
///   <item>Relative to <c>conquerDirectory</c> on the filesystem</item>
///   <item>WDF archive whose key matches the first path segment
///         (e.g. <c>c3/textures/foo.dds</c> → looks in <c>c3.wdf</c>)</item>
/// </list>
/// Files with the <c>.7z</c> extension are transparently decompressed;
/// the first <c>.dmap</c> entry inside the archive is returned.
/// </para>
/// </summary>
public sealed class TqPackageReader : IPackageReader
{
    // ── State ────────────────────────────────────────────────────────────
    private readonly Dictionary<string, IPackageReader> _packages = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _conquerDirectory;

    // ── Constructor ──────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the reader, automatically mounting <c>c3.wdf</c> and
    /// <c>data.wdf</c> if they are present in <paramref name="conquerDirectory"/>.
    /// </summary>
    public TqPackageReader(string conquerDirectory)
    {
        _conquerDirectory = conquerDirectory;
        AddPackage("c3.wdf");
        AddPackage("data.wdf");
    }

    // ── IPackageReader ───────────────────────────────────────────────────

    /// <summary>
    /// Mounts an additional archive file.
    /// Only <c>.wdf</c> archives are currently supported; other extensions are silently ignored.
    /// </summary>
    public void AddPackage(string fileName)
    {
        var fullPath = Path.Combine(_conquerDirectory, fileName);
        if (!File.Exists(fullPath)) return;

        var dot       = fileName.LastIndexOf('.');
        if (dot < 0) return;

        var extension = fileName[(dot + 1)..].ToLowerInvariant();
        var key       = fileName[..dot].ToLowerInvariant();

        switch (extension)
        {
            case "wdf":
                _packages[key] = new WdfPackageReader(fullPath);
                break;
            // Future: case "pak": ... break;
        }
    }

    /// <inheritdoc/>
    public Stream LoadFile(string fileName)
    {
        // 1 – absolute path
        if (File.Exists(fileName))
            return LoadFromFileSystem(fileName);

        // 2 – relative to conquer directory
        var fullPath = Path.Combine(_conquerDirectory, fileName);
        if (File.Exists(fullPath))
            return LoadFromFileSystem(fullPath);

        // 3 – inside a mounted WDF archive
        //     The first path segment is the archive key
        //     e.g. "c3/ani/hero.c3" → key "c3"
        var separatorIdx = fileName.IndexOfAny(['/', '\\']);
        var packageKey   = separatorIdx >= 0
            ? fileName[..separatorIdx]
            : fileName;

        if (_packages.TryGetValue(packageKey, out var package))
            return package.LoadFile(fileName);

        throw new FileNotFoundException($"Asset not found: {fileName}");
    }

    // ── IDisposable ──────────────────────────────────────────────────────
    public void Dispose()
    {
        foreach (var pkg in _packages.Values)
            pkg.Dispose();
        _packages.Clear();
    }

    // ── Private helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Reads a file from the real filesystem into a <see cref="MemoryStream"/>.
    /// If the file has a <c>.7z</c> extension the first <c>.dmap</c> entry
    /// inside the archive is extracted instead.
    /// </summary>
    private static Stream LoadFromFileSystem(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".7z", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = new ArchiveFile(path);
            var dmapEntry = archive.Entries.FirstOrDefault(e =>
                Path.GetExtension(e.FileName).ToLowerInvariant() == ".dmap");

            if (dmapEntry != null)
            {
                var ms = new MemoryStream();
                dmapEntry.Extract(ms);
                ms.Position = 0;
                return ms;
            }
        }

        // Plain file – read entirely into memory so the FileStream can be closed
        var buffer = File.ReadAllBytes(path);
        return new MemoryStream(buffer, writable: false);
    }
}
