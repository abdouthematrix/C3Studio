namespace C3Studio.Core.Models;

public class NpcTypeInfo
{
    public int    NpcType         { get; set; }
    public string Name            { get; set; } = string.Empty;
    public uint   SimpleObjId     { get; set; }
    public ulong  StandByMotionId { get; set; }
    public ulong  BlazeMotionId   { get; set; }
    public ulong  BlazeMotion1Id  { get; set; }
    public ulong  BlazeMotion2Id  { get; set; }
    public ulong  RestMotionId    { get; set; }
    public string Effect          { get; set; } = string.Empty;
    public int    Asb             { get; set; } = 5;
    public int    Adb             { get; set; } = 6;
    public int    FixDir          { get; set; }
}
