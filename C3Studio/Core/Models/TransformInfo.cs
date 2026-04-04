namespace C3Studio.Core.Models;

/// <summary>
/// Represents one entry from either <c>AdditiveSize.ini</c> or <c>TransForm.ini</c>.
///
/// Fields shared by both parsers: Index, AdditiveSize, Look, Scale, CanJump.
/// Fields added by TransForm.ini: Armet, RWeapon, LWeapon, Misc, Mount.
/// </summary>
public class TransformInfo
{
    /// <summary>Numeric identifier — section index in the source file.</summary>
    public int Index { get; set; }

    /// <summary>
    /// Size additive applied to the character model.
    /// Key: <c>AdditiveSize</c> (AdditiveSize.ini) / <c>AddSize</c> (TransForm.ini).
    /// </summary>
    public int AdditiveSize { get; set; }

    /// <summary>Look/facing value. Key: <c>Look</c>.</summary>
    public int Look { get; set; }

    /// <summary>Scale percentage (100 = normal). Key: <c>Scale</c>.</summary>
    public int Scale { get; set; } = 100;

    /// <summary>
    /// Whether the transform allows jumping.
    /// AdditiveSize.ini: "ON"/"OFF" string.
    /// TransForm.ini: integer (0 = false, non-zero = true).
    /// </summary>
    public bool CanJump { get; set; }

    // ── TransForm.ini-only equipment-slot overrides ───────────────────────

    /// <summary>Helmet / armet slot override ID. 0 = no override.</summary>
    public int Armet { get; set; }

    /// <summary>Right-hand weapon slot override ID. 0 = no override.</summary>
    public int RWeapon { get; set; }

    /// <summary>Left-hand weapon / shield slot override ID. 0 = no override.</summary>
    public int LWeapon { get; set; }

    /// <summary>Miscellaneous equipment slot override ID. 0 = no override.</summary>
    public int Misc { get; set; }

    /// <summary>Mount slot override ID. 0 = no override.</summary>
    public int Mount { get; set; }
}