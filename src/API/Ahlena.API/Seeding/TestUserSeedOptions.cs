namespace Ahlena.API.Seeding;

/// <summary>
/// Opt-in configuration for <see cref="TestUserDataSeeder"/>.
///
/// <list type="bullet">
/// <item><see cref="TargetMode"/> selects the guarded database target.
/// <c>BrunoTest</c> (default) keeps the existing automated-test guard that
/// pins every configured module database to
/// <c>localhost:5432/SanadBrunoTestDb</c>. <c>PersistentLocal</c> is the
/// explicit persistent-local fixture mode: it validates that every
/// configured module database targets <c>localhost:5432/AhlenaDb</c> and
/// never rewrites configuration or creates a database.</item>
/// <item><see cref="Scope"/> selects fixture breadth. <c>Full</c> (default)
/// keeps the existing bundle unchanged. <c>Minimal</c> seeds only the
/// deterministic identities required by the L1 verification path — no
/// families, dependents, caregivers, bookings, subscriptions or catalog
/// rows.</item>
/// </list>
///
/// Unknown values fail closed in the guard/seeder; a disabled seed never
/// inspects either value.
/// </summary>
public sealed record TestUserSeedOptions
{
    public const string SectionName = "App:TestUserSeed";

    public const string TargetModeBrunoTest = "BrunoTest";

    public const string TargetModePersistentLocal = "PersistentLocal";

    public const string ScopeFull = "Full";

    public const string ScopeMinimal = "Minimal";

    public bool Enabled { get; init; }

    public string Password { get; init; } = "Test-1234!";

    public string? ElderlyPassword { get; init; }

    /// <summary>Database target mode; defaults to the existing Bruno pinning guard.</summary>
    public string TargetMode { get; init; } = TargetModeBrunoTest;

    /// <summary>Fixture breadth; defaults to the existing full bundle.</summary>
    public string Scope { get; init; } = ScopeFull;

    public bool IsPersistentLocalTarget =>
        string.Equals(
            TargetMode,
            TargetModePersistentLocal,
            StringComparison.OrdinalIgnoreCase);

    public bool IsMinimalScope =>
        string.Equals(
            Scope,
            ScopeMinimal,
            StringComparison.OrdinalIgnoreCase);
}
