namespace C3Studio.Core.Models;

/// <summary>
/// One entry from <c>ini/AdditiveSize.ini</c>, corresponding to
/// <c>CTransformInfo</c> in the original C++ codebase.
/// </summary>
public sealed class TransformInfo
{
    /// <summary>The numeric index used as the section key: <c>[Transform{Index}]</c>.</summary>
    public int Index { get; set; }
    /// <summary>Additive size delta applied to the role.</summary>
    public int AdditiveSize { get; set; }
    /// <summary>Look / appearance override id.</summary>
    public int Look { get; set; }
    /// <summary>Whether this transform allows jumping.</summary>
    public bool CanJump { get; set; }
    /// <summary>Scale percentage (100 = normal).</summary>
    public int Scale { get; set; }
}