namespace C3Studio.Core.Models;
public enum RoleLook
{
    Other = 0,
    SmallFemale = 1,
    BigFemale = 2,
    SmallMale = 3,
    BigMale = 4,
    Female = 7,
    Male = 8
}
public static class RoleLookExtensions
{
    public static string ToDisplayString(this RoleLook look) => look switch
    {
        RoleLook.SmallFemale => "Small Female",
        RoleLook.BigFemale => "Big Female",
        RoleLook.SmallMale => "Small Male",
        RoleLook.BigMale => "Big Male",
        RoleLook.Female => "Female",
        RoleLook.Male => "Male",
        _ => "Other"
    };
}
