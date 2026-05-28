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
///   <item>All mounted archives whose key starts with the first path segment
///         (e.g. <c>c3/textures/foo.dds</c> → searches <c>c3</c>, <c>c31</c>, …)</item>
/// </list>
/// Files with the <c>.7z</c> extension are transparently decompressed;
/// the first <c>.dmap</c> entry inside the archive is returned.
/// </para>
/// </summary>
public sealed class TqPackageReader : IPackageReader
{
    // ── State ────────────────────────────────────────────────────────────
    // Keyed by full base name: "c3", "c31", "data", "data1", …
    // LoadFile collects every reader whose key starts with the path prefix.
    private readonly Dictionary<string, List<IPackageReader>> _packages = new();
    private readonly string _conquerDirectory;

    // ── Constructor ──────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the reader, automatically mounting well-known archives
    /// that are present in <paramref name="conquerDirectory"/>.
    /// </summary>
    public TqPackageReader(string conquerDirectory)
    {
        _conquerDirectory = conquerDirectory;
        AddPackage("c3.wdf");
        AddPackage("data.wdf");
        AddPackage("c3.tpi");
        AddPackage("data.tpi");
        AddPackage("c31.tpi");
        AddPackage("data1.tpi");
    }

    // ── IPackageReader ───────────────────────────────────────────────────

    /// <summary>
    /// Mounts an additional archive file.
    /// Supported extensions: <c>.wdf</c>, <c>.tpi</c>, <c>.dnp</c>.
    /// Files that don't exist or use unsupported extensions are silently ignored.
    /// </summary>
    public void AddPackage(string fileName)
    {
        var fullPath = Path.Combine(_conquerDirectory, fileName);
        if (!File.Exists(fullPath)) return;

        var dot = fileName.IndexOf('.');
        if (dot < 0) return;

        var key = fileName[..dot].ToLowerInvariant();
        var extension = fileName[(dot + 1)..].ToLowerInvariant();

        IPackageReader reader = extension switch
        {
            "wdf" => new WdfPackageReader(fullPath),
            "tpi" => new TpiPackageReader(fullPath),
            "dnp" => new DnpPackageReader(fullPath),
            _ => null!
        };

        if (reader is null) return;

        if (!_packages.TryGetValue(key, out var list))
        {
            list = [];
            _packages[key] = list;
        }
        list.Add(reader);
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

        // 3 – inside a mounted archive.
        //     The first path segment is the prefix, e.g. "c3" from "c3/ani/hero.c3".
        //     Collect every registered package whose key starts with that prefix so
        //     that both "c3" and "c31" are searched when the prefix is "c3".
        var prefix = fileName.Split('/', '\\')[0].ToLowerInvariant();
        var readers = _packages
            .Where(kvp => kvp.Key.StartsWith(prefix))
            .SelectMany(kvp => kvp.Value)
            .ToList();

        foreach (var reader in readers)
        {
            try { return reader.LoadFile(fileName); }
            catch (FileNotFoundException) { /* try next */ }
        }

        throw new FileNotFoundException($"Asset not found: {fileName}");
    }

    // ── IDisposable ──────────────────────────────────────────────────────
    public void Dispose()
    {
        foreach (var list in _packages.Values)
            foreach (var reader in list)
                reader.Dispose();
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