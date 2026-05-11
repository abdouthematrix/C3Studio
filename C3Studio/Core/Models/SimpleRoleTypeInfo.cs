namespace C3Studio.Core.Models;

/// <summary>
/// Data parsed from a single section of <c>ini/3DSimpleRole.ini</c>.
///
/// Two mutually exclusive role variants exist in the file:
/// <list type="bullet">
///   <item><term>SimpleObj-based</term>
///     <description>Uses <see cref="SimpleObjId"/> + motion IDs — no equipment.</description></item>
///   <item><term>Equipment-based</term>
///     <description>Uses <see cref="Look"/> + <see cref="RawArmorId"/> + <see cref="RawHairId"/>
///     — no SimpleObj or motions.</description></item>
/// </list>
///
/// Both variants may have front/back 3D effects (<see cref="FEffect"/>, <see cref="BEffect"/>).
///
/// Example sections:
/// <code>
/// [Role0]
/// 3DSimpleObjID=20001
/// 3DStandByMotion=9998800
/// 3DBlazeMotion=9998801
/// BlazeSound=sound/Role0.wav
/// F3DEffect=role-select1
/// B3DEffect=role-select1
///
/// [Role100]
/// Look=3
/// Armor=3135990
/// Hair=3119524
/// F3DEffect=role-select5
/// </code>
/// </summary>
public class SimpleRoleTypeInfo
{
    /// <summary>Raw section key, e.g. "Role0" or "Role100".</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Numeric suffix parsed from the section key, e.g. 0 or 100.</summary>
    public int Index { get; set; }

    // ── SimpleObj-based fields ────────────────────────────────────────────

    /// <summary>3DSimpleObjID value; 0 when absent.</summary>
    public uint SimpleObjId { get; set; }

    /// <summary>3DStandByMotion value; 0 when absent.</summary>
    public ulong StandByMotionId { get; set; }

    /// <summary>3DBlazeMotion value; 0 when absent.</summary>
    public ulong BlazeMotionId { get; set; }

    /// <summary>BlazeSound path; null when absent.</summary>
    public string? BlazeSound { get; set; }

    // ── Equipment-based fields ────────────────────────────────────────────

    /// <summary>
    /// Body-type look (1=SmallFemale, 2=BigFemale, 3=SmallMale, 4=BigMale).
    /// 0 means the field was not present in the section.
    /// </summary>
    public int Look { get; set; }

    /// <summary>
    /// Raw Armor value from the ini (e.g. 3135990).
    /// Pass this together with <see cref="Look"/> to
    /// <see cref="ResolveArmorId"/> to get the actual <c>Armor.ini</c> key.
    /// </summary>
    public uint RawArmorId { get; set; }

    /// <summary>
    /// Raw Hair/Armet value from the ini (e.g. 3119524).
    /// Pass this together with <see cref="Look"/> to
    /// <see cref="ResolveArmorId"/> to get the actual <c>Armet.ini</c> key.
    /// </summary>
    public uint RawHairId { get; set; }

    // ── Shared effect fields ──────────────────────────────────────────────

    /// <summary>F3DEffect key; null when absent.</summary>
    public string? FEffect { get; set; }

    /// <summary>B3DEffect key; null when absent.</summary>
    public string? BEffect { get; set; }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>True when this role is SimpleObj-based rather than equipment-based.</summary>
    public bool IsSimpleObjRole => SimpleObjId != 0;

    /// <summary>True when this role is equipment-based (has a Look value).</summary>
    public bool IsEquipmentRole => Look != 0;

    /// <summary>
    /// Replicates <c>C3DRole::SetArmor</c> / <c>SetArmet</c>:
    /// strips the look prefix from <paramref name="rawId"/> and re-binds it
    /// to <paramref name="look"/>.
    /// <code>
    /// result = look * 1_000_000 + (rawId % 1_000_000) / 10 * 10
    /// </code>
    /// </summary>
    public static uint ResolveArmorId(uint rawId, int look)
    {
        uint stripped = (rawId % 1_000_000) / 1000 * 1000;
        return (uint)(look * 1_000_000) + stripped;
    }

    /// <summary>
    /// Effective armor ini ID derived from <see cref="RawArmorId"/> and <see cref="Look"/>.
    /// Returns 0 when the role has no armor field.
    /// </summary>
    public uint EffectiveArmorId => RawArmorId == 0 ? 0 : ResolveArmorId(RawArmorId, Look);

    /// <summary>
    /// Effective armet ini ID derived from <see cref="RawHairId"/> and <see cref="Look"/>.
    /// Returns 0 when the role has no hair field.
    /// </summary>
    public uint EffectiveHairId => RawHairId == 0 ? 0 : ResolveArmorId(RawHairId, Look);
}
