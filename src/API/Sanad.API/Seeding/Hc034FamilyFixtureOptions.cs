namespace Sanad.API.Seeding;

public sealed record Hc034FamilyFixtureOptions
{
    public const string SectionName = "App:Hc034FamilyFixture";

    public bool Enabled { get; init; }
    public bool ResetExistingPassword { get; init; }
    public string Email { get; init; } = "hc034.family.other@test.sanad.local";
    public string PhoneNumber { get; init; } = "+201000000034";
    public string Password { get; init; } = string.Empty;
    public string FamilyName { get; init; } = "HC-034 Other Family";
}
