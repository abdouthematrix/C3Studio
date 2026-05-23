using System.Reflection;

namespace C3Studio.Core.Models;

public enum MagicSort
{
    Type = 0,
    Attack = 1,
    Recruit = 2,
    Cross = 3,
    Sector = 4,
    Bomb = 5,
    AttachStatus = 6,
    DetachStatus = 7,
    Square = 8,
    JumpAttack = 9,
    RandomTransport = 10,
    DispatchXp = 11,
    Collide = 12,
    SerialCut = 13,
    Line = 14,
    AtkRange = 15,
    AttackStatus = 16,
    CallTeamMember = 17,
    RecordTransportSpell = 18,
    Transform = 19,
    AddMana = 20,
    LayTrap = 21,
    Dance = 22,
    CallPet = 23,
    Vampire = 24,
    Instead = 25,
    DecLife = 26,
    Toxic = 27,
    ShurikenVortex = 28,
    CounterKill = 29,

    Spook = 30,
    WarCry = 31,
    Riding = 32,
    Shock = 34,
    ChainBolt = 37,
    StarArrow = 38,
    DragonWhirl = 40,
    RemoveBuffers = 46,
    Tranquility = 47,
    DirectAttack = 48,
    Compasion = 50,
    Auras = 51,
    ShieldBlock = 52,
    Oblivion = 53,
    WhirlwindKick = 54,
    PhysicalSpells = 55,
    ScurvyBomb = 56,
    CannonBarrage = 57,
    BlackSpot = 58,
    Summon = 72,
    BombLine = 60,
    MoveLine = 61,
    AddBlackSpot = 62,
    PirateXpSkill = 63,
    ChargingVortex = 64,
    MortalDrag = 65,
    KineticSpark = 67,
    BladeFlurry = 68,
    SectorBack = 69,
    BreathFocus = 70,
    FatalCross = 71,
    Summons = 72,
    FatalSpin = 73,
    DragonCyclone = 75,
    StraightFist = 76,
    Perimeter = 78,
    AirKick = 79,
    Strike = 81,
    SectorPasive = 82,
    ManiacDance = 83,
    Omnipotence = 85,
    Pounce = 84,
    BurntFrost = 86,

    Rectangle = 87,
    RemoveStamin = 88,
    PetAttachStatus = 89,
    Wildwind = 90,
    SwirlingStorm = 91,
    Pitching = 92,
    ThunderRampage = 94,
    HeavensWrath = 95,
    HellVortex = 98,
    TripleAttack = 99,
    ArrowBlades = 103,
    CrackShot = 104,
    PirateSkill = 107,
    BeastControl = 108,
    Kunpeng = 109,
    SuanniCommand = 110,
    LeeLong1 = 111,
    SupremeLeadership = 112,
    ChaoticDance = 113,
    ChaoticDanceAttack = 114,
}
public class MagicEffect
{
    public string Name { get; set; }
    public string ActEffect { get; set; }
    public string ActOfAttacker { get; set; }
    public string ActOfCastAnimation { get; set; }
    public string ActOfTarget { get; set; }
    public string ActOfTargetHit { get; set; }
    public string AttackerLWeapon { get; set; }
    public string AttackerRWeapon { get; set; }
    public float AttackerScale { get; set; }
    public int BindNeedPower { get; set; }
    public string BindPowerEffect { get; set; }
    public int BindPowerStatus { get; set; }
    public bool Bystander { get; set; }
    public bool CanBeUsedDirectly { get; set; }
    public bool CanBeUsedInMarket { get; set; }
    public bool CanBeUsedInTransform { get; set; }
    public bool CanBeUseDirectly { get; set; }
    public bool CDReduceByGouYu { get; set; }
    public int CheckWeapon { get; set; }
    public string ClientRepresent { get; set; }
    public string ClientStatusEffect { get; set; }
    public int ClrPosOfPlayerClientStatus { get; set; }
    public int CoolDown { get; set; }
    public int DelayOfTarget3DEffect { get; set; }
    public string Desc { get; set; }
    public string DescEx { get; set; }
    public bool DoNotAddNewSkill { get; set; }
    public int EarthQuakeTimes { get; set; }
    public bool EnableUpdateLife { get; set; }
    public int ExtraCmdTime { get; set; }
    public int FirstTraceEffectDelay { get; set; }
    public float FlyOffsetStep { get; set; }
    public float FlyOffsetStepDown { get; set; }
    public bool GetEffAngle { get; set; }
    public int GouyuType { get; set; }
    public int HadStatus { get; set; }
    public bool Hide { get; set; }
    public bool HideCD { get; set; }
    public string HideStatus3DEffectOfTarget { get; set; }
    public string HideStatus3DEffectOfTargetRecoverExtraCmdEnd { get; set; }
    public float HitMaxHeight { get; set; }
    public int HitNumPerTarget { get; set; }
    public int IntoneDuration { get; set; }
    public string IntoneEffect { get; set; }
    public bool IsCDShare { get; set; }
    public bool IsDirTerrianEffect { get; set; }
    public bool isNewTip { get; set; }
    public bool IsRuneSkill { get; set; }
    public bool IsTraceEffectAutoDel { get; set; }
    public string KeyHitNumEffect { get; set; }
    public float LinkEffectLength { get; set; }
    public float MagicLayerOffsetX { get; set; }
    public float MagicLayerOffsetY { get; set; }
    public float MagicLayerOffsetZ { get; set; }
    public string MagicRangeAlertEffect { get; set; }
    public string MagicRangeAttackEffect { get; set; }
    public int MagicReplace { get; set; }
    public string MainDlgEffect { get; set; }
    public string MapEffect { get; set; }
    public string MapIDDisable { get; set; }
    public string MapTypeDisable { get; set; }
    public string MapTypeEnable { get; set; }
    public int MoveGhostInterval { get; set; }
    public int MultiHitInteravalTime { get; set; }
    public int MultiHitPercentHit { get; set; }
    public int MultiHitTimes { get; set; }
    public int MutexStatus { get; set; }
    public int ndir { get; set; }
    public bool NeedCheckMap { get; set; }
    public int NeedStatus { get; set; }
    public bool NeedTurn { get; set; }
    public string NonRotateEffectOfAttacker { get; set; }
    public bool RandomMagicFlag { get; set; }
    public int RepulsionType { get; set; }
    public string Role3DEffectOfAffectTarget { get; set; }
    public string Role3DEffectOfAttacker { get; set; }
    public string Role3DEffectOfAttaker { get; set; }
    public bool Role3DEffectOfAttakerInitDir { get; set; }
    public string Role3DEffectOfAttakerSpecial { get; set; }
    public string Role3DEffectOfTarget { get; set; }
    public string Role3DEffectOfTargetExtraCmdBegin { get; set; }
    public string Role3DEffectOfTargetExtraCmdEndDel { get; set; }
    public string Role3DEffectOfTargetHit { get; set; }
    public string Role3DEffectOfTargetMiss { get; set; }
    public float RraceEffectSpeed { get; set; }
    public string ScreenEffect { get; set; }
    public string ScreenRepresent { get; set; }
    public int ServantID { get; set; }
    public int SetPosOfClientStatus { get; set; }
    public int SetProcessStatus { get; set; }
    public int ShowType { get; set; }
    public int SkillLastTime { get; set; }
    public bool SkipActOfAttacker { get; set; }
    public bool SkipChoose { get; set; }
    public bool SkipHitNum { get; set; }
    public bool SkipJustActOfAttacker { get; set; }
    public bool SkipWound { get; set; }
    public MagicSort SortOfAct { get; set; }
    public int SortOfArt { get; set; }
    public string SoundOfAlert { get; set; }
    public string SoundOfAttacker { get; set; }
    public string SoundOfIntone { get; set; }
    public string SoundOfTarget { get; set; }
    public string SoundOfTargetExtraCmdEnd { get; set; }
    public int Status { get; set; }
    public int StickingFrame { get; set; }
    public int StickingFrameNear { get; set; }
    public int StickingLastTimeNear { get; set; }
    public int SubType { get; set; }
    public bool Synchro { get; set; }
    public int TargetType { get; set; }
    public int TargetWarningTime { get; set; }
    public int TargetWoundDelayOfTarget { get; set; }
    public string TerrainEffect { get; set; }
    public bool TerrainEffectInitDir { get; set; }
    public string TextEffect { get; set; }
    public string TraceEffect { get; set; }
    public float TraceEffectAcceleration { get; set; }
    public int TraceEffectAliveTime { get; set; }
    public float TraceEffectAngleDegree { get; set; }
    public int TraceEffectDelay { get; set; }
    public bool TraceEffectInitDir { get; set; }
    public bool TraceEffectInitDirHit { get; set; }
    public float TraceEffectOriginYOffset { get; set; }
    public float TraceEffectSpeed { get; set; }
    public float TraceEffectTargetYOffset { get; set; }
    public int TraceEffectType { get; set; }
    public int TraceObjID { get; set; }
    public string TurnRole3DEffectOfAttacker { get; set; }
    public bool UseSkillEditorActOfTarget { get; set; }
    public string WarningEffectOfTarget { get; set; }
    public string WarningEffOnTarget { get; set; }
    public int WeaponTypeSkipStatus { get; set; }
    public int WoundDelayOfTarget { get; set; }
    public int WoundDurationOfTarget { get; set; }
    public int WoundDurationOfTarget2 { get; set; }
    public int WoundNumType { get; set; }
    public int WoundNumTypeCritical { get; set; }
    public int Xp { get; set; }
    public int YuanShenMagicPro { get; set; }
    public int ZoomNumVisable { get; set; }

    /// <summary>Shallow copy — safe because every field is a value type or immutable string.</summary>
    public MagicEffect Clone() => (MagicEffect)MemberwiseClone();
}

public class MagicSkillGroup
{
    /// <summary>All level entries keyed by their full section ID (e.g. 100000, 100001…).</summary>
    public Dictionary<int, MagicEffect> Levels { get; set; } = new();

    /// <summary>
    /// Skin cosmetic overrides keyed by skin index (1, 2…).
    /// Each value maps property name → raw string value as it appears in the ini.
    /// </summary>
    public Dictionary<int, Dictionary<string, string>> Skins { get; set; } = new();

    /// <summary>
    /// Returns the <see cref="MagicEffect"/> for <paramref name="levelId"/>, optionally
    /// with skin overrides applied.  Returns <c>null</c> when the level is not found.
    /// </summary>
    public MagicEffect? GetEffect(int levelId, int skinId = 0)
    {
        if (!Levels.TryGetValue(levelId, out var baseEffect))
            return null;

        if (skinId == 0 || !Skins.TryGetValue(skinId, out var skinOverrides))
            return baseEffect;

        // Clone so the base level is never mutated by skin application.
        var skinned = baseEffect.Clone();

        foreach (var (propName, rawValue) in skinOverrides)
        {
            var pi = typeof(MagicEffect).GetProperty(propName,
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (pi is null || !pi.CanWrite) continue;

            pi.SetValue(skinned, ParseValue(rawValue, pi.PropertyType));
        }

        return skinned;
    }

    // ── Value parser (no external dependency) ────────────────────────────────
    // Previously lived in MagicEngine.MagicEffectLoader; inlined here so the
    // model assembly has no outward dependencies.

    private static object ParseValue(string raw, Type targetType)
    {
        if (targetType == typeof(bool))
            return raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase);

        if (targetType == typeof(int))
            return int.TryParse(raw, out int i) ? i : 0;

        if (targetType == typeof(float))
            return float.TryParse(raw,
                       System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture,
                       out float f) ? f : 0f;

        // string and anything else (Convert handles enums if ever added)
        return Convert.ChangeType(raw, targetType);
    }
}