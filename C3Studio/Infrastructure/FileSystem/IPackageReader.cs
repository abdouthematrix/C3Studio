using System.IO;

namespace C3Studio.Infrastructure.FileSystem;

public interface IPackageReader : IDisposable
{
    void   AddPackage(string fileName);
    Stream LoadFile(string fileName);
}
