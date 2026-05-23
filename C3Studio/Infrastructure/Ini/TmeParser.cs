using C3Studio.Core.Models;
using System.IO;
using System.Text;

namespace C3Studio.Infrastructure.Ini;

public static class TmeParser
{
    private const int NameLength = 128;

    public static TmeEntry[] Parse(string path)
    {
        if (!File.Exists(path))
            return [];

        using var br = new BinaryReader(File.OpenRead(path), Encoding.ASCII);

        uint count = br.ReadUInt32();
        var entries = new TmeEntry[count];

        for (int i = 0; i < count; i++)
        {
            var nameBytes = br.ReadBytes(NameLength);

            int terminator = Array.IndexOf(nameBytes, (byte)0);
            string key = Encoding.ASCII.GetString(
                nameBytes, 0,
                terminator < 0 ? NameLength : terminator);

            entries[i] = new TmeEntry
            {
                EffectKey = key,
                Delay = br.ReadUInt32(),
                Reserved = br.ReadUInt32(),
                Duration = br.ReadInt32(),
                Interval = br.ReadInt32(),
            };
        }

        return entries;
    }
}