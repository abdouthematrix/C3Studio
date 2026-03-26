using System.IO;
using C3Studio.Infrastructure.FileSystem;

namespace C3Studio.Core.Services;

public interface IAssetFileService
{
    void   Initialize(string conquerPath);
    Stream Open(string relativePath);
    string? TryResolvePath(string relativePath);
}

public class AssetFileService : IAssetFileService, IDisposable
{
    private TqPackageReader? _reader;
    private string           _root = string.Empty;

    public void Initialize(string conquerPath)
    {
        _reader?.Dispose();
        _root   = conquerPath;
        _reader = new TqPackageReader(conquerPath);
    }

    public Stream Open(string relativePath)
    {
        if (_reader == null) throw new InvalidOperationException("AssetFileService not initialized.");
        return _reader.LoadFile(relativePath);
    }

    public string? TryResolvePath(string relativePath)
    {
        var full = Path.Combine(_root, relativePath);
        return File.Exists(full) ? full : null;
    }

    public void Dispose() => _reader?.Dispose();
}
