namespace C3Studio.Core.Models;

/// <summary>
/// One entry from <c>ini/ItemTexture.ini</c>.
/// Each section maps a base item ID to a set of color-variant textures.
/// <code>
/// [900000]
/// Amount=7
/// Color0=3
/// Texture0=900300
/// Color1=4
/// Texture1=900400
/// </code>
/// </summary>
public sealed class ItemTextureInfo
{
    public const int MaxColors = 16;

    /// <summary>Base item ID (numeric section header).</summary>
    public uint Id { get; set; }
    public int Look => (int)(Id / 1_000_000);    
    public int SubType
    {
        get
        {
            // Keep only last 6 digits
            int trimmed = (int)(Id % 1_000_000);

            // Extract the first 3 digits of those 6
            int type = trimmed / 1000;

            return type;
        }
    }

    /// <summary>Number of active color/texture pairs declared by <c>Amount=</c>.</summary>
    public int Amount { get; set; }

    /// <summary>
    /// Color value for slot <c>i</c> — matches the <see cref="ItemColor"/> enum.
    /// </summary>
    public byte[] Colors { get; } = new byte[MaxColors];

    /// <summary>Texture ID for slot <c>i</c> (key into <c>3dtexture.ini</c>).</summary>
    public uint[] TextureIds { get; } = new uint[MaxColors];

    /// <summary>
    /// Returns the texture ID for the requested <paramref name="color"/>,
    /// or <c>0</c> if no matching slot is found.
    /// </summary>
    public uint GetTexture(ItemColor color)
    {
        var v = (byte)color;
        for (int i = 0; i < Amount && i < MaxColors; i++)
            if (Colors[i] == v) return TextureIds[i];
        return 0;
    }

    /// <summary>
    /// Returns the texture ID for the first slot whose color matches
    /// <paramref name="colorValue"/>, or <c>0</c> if not found.
    /// </summary>
    public uint GetTexture(byte colorValue)
    {
        for (int i = 0; i < Amount && i < MaxColors; i++)
            if (Colors[i] == colorValue) return TextureIds[i];
        return 0;
    }
}

/// <summary>
/// Item color variants as stored in <c>ItemTexture.ini</c> Color fields.
/// </summary>
public enum ItemColor : byte
{
    Black = 2,
    Orange = 3,
    LightBlue = 4,
    Red = 5,
    Blue = 6,
    Yellow = 7,
    Purple = 8,
    White = 9,
}
/*
Black = 2,
White = 3,
Red = 4,
Orange = 5,
LightBlue = 6,
Green = 7,
Yellow = 8,
Purple = 9,    
*/