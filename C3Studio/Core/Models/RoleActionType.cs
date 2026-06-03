
namespace C3Studio.Core.Models;

// ── Role Action Types ─────────────────────────────────────────
public enum RoleActionType
{
    // Social / Dance
    Dance1 = 1,
    Dance2 = 2,
    Dance3 = 3,
    Dance4 = 4,
    Dance5 = 5,
    Dance6 = 6,
    Dance7 = 7,
    Dance8 = 8,
    Dance9 = 9,
    Dance10 = 10,
    Dance11 = 11,
    Dance12 = 12,
    Dance13 = 13,
    Dance14 = 14,
    Dance15 = 15,
    Dance16 = 16,
    Dance17 = 17,
    Dance18 = 18,
    Dance19 = 19,
    Dance20 = 20,
    Dance21 = 21,

    StandBy = 100,
    Rest1 = 101,
    Rest2 = 102,
    Rest3 = 103,
    StandBy_Injured = 105,

    WalkL = 110,
    WalkR = 111,
    WalkL_Injured = 115,
    WalkR_Injured = 116,
    RunL = 120,
    RunR = 121,
    Transform = 122,
    RunL_Injured = 125,
    RunR_Injured = 126,

    Jump = 130,
    JumpBack = 131,
    JumpRun = 132,

    JumpAtk = 140,//Laugh
    JumpAtkEnd = 141,

    Happy = 150,
    Guffaw = 151,

    Angry = 160,
    Sad = 170,
    Excitement = 180,
    Wave = 190,
    Bow = 200,
    Kneel = 210,
    Kneel2 = 220,
    PokerSitDown = 221,
    PokerSit = 222,
    PokerStandUp = 223,
    PokerFold = 224,
    PokerCall = 225,
    PokerAllIn = 226,
    PokerWin = 227,
    PokerLose = 228,
    PokerWait = 229,

    Cool = 230,
    CoolPose = 231,
    Swim = 240,

    Sit = 250,
    SitDown_Static = 251,

    Bless = 260,
    BlessCool = 261,
    Pickup = 262,
    LieDown = 270,
    Lie = 271,
    PickUp = 280,
    Mine = 290,

    LoveStroll_WalkL = 291,
    LoveStroll_WalkR = 292,
    LoveStroll_RunL = 293,
    LoveStroll_RunR = 294,
    LoveStroll_Jump = 295,
    LoveStroll_StandBy = 296,

    Alert = 300,
    Alert_Injured = 305,
    DodgePhysicalAttack = 310,
    Dodge0 = 311,
    HitPhysicalAttack = 320,

    Bruise = 321,
    Bruise1 = 324,
    Bruise2 = 325,
    Bruise3 = 326,
    Bruise4 = 327,
    Bruise5 = 328,
    Bruise6 = 329,

    Die = 330,
    DeadBody = 331,
    Die1 = 332,
    DeadBody1 = 333,
    Die2 = 334,
    DeadBody2 = 335,
    Die3 = 336,
    DeadBody3 = 337,

    DieWithoutBlood = 340,
    DeadWithoutBloodBody = 341,
    WalkBack = 342,

    Attack0 = 350,
    Attack1 = 351,
    Attack2 = 352,
    Mine2 = 360,

    Intone = 390,
    Intone_Duration = 391,
    Intone_Launch = 392,

    Relive = 400,
    PhysicalAttack_401 = 401,
    PhysicalAttack_402 = 402,
    PhysicalAttack_403 = 403,
    PhysicalAttack_404 = 404,
    PhysicalAttack_405 = 405,
    PhysicalAttack_406 = 406,
    PhysicalAttack_407 = 407,
    PhysicalAttack_408 = 408,

    Puncture = 451,
    AirStrike = 452,
    Tornado = 453,
    BodyShield = 460,
    BodyBlock = 461,
    GodBelieve = 465,
    Bump = 470,
    LoveStroll_Down = 473,
    LoveStroll_Sit = 474,
    LoveStroll_LeftUp = 475,
    LoveStroll_SitDown = 477,
    LoveStroll_Sit2 = 478,
    LoveStroll_StandUp = 479,

    Challenge = 480,
    Enligtened = 481,
    Kicked = 482,
    RiseUp = 483,


    Fly_StandBy = 501,
    Fly_Alert = 502,

    LoveStroll_Stand = 503,
    LoveStroll_End = 504,
    LoveStroll_Up = 505,

    Fly_Move = 510,

    SweetKiss = 514,
    SweetKiss_End = 515,
    SweetKiss_Start = 516,

    Fly_Attack = 520,

    HoldHands = 521,
    HoldHands_StandBy = 522,
    HoldHands_WalkL = 523,
    HoldHands_WalkR = 524,
    HoldHands_RunL = 525,
    HoldHands_RunR = 526,
    HoldHands_Jump = 527,
    HoldHands_StandBy2 = 528,
    HoldHands2 = 529,

    Fly_Down = 530,
    Fly_Up = 540,
    Fly_Die = 550,
    Fly_Wound = 560,

    HoldHands2_WalkL = 598,
    HoldHands2_WalkR = 599,

    JumpBack_Shoot = 610,
    Fast_Shoot = 620,
    ChargeUp_Begin = 630,
    ChargeUp_End = 631,

    TM_Fast_Shoot = 800,
    TM_Dispersion_Shoot = 801,

    SomethingMagical1 = 901,
    SomethingMagical2 = 902,
    MagicalAttackCast = 903
}
