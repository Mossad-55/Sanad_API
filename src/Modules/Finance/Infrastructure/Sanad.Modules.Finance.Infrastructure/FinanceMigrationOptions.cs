namespace Sanad.Modules.Finance.Infrastructure;

public sealed class FinanceMigrationOptions
{
    public const string SectionName = "FinanceMigrations";
    public bool ApplyOnStartup { get; init; }
}
