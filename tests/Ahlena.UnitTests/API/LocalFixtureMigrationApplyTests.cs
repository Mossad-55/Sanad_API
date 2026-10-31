using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ahlena.API.Seeding;
using Ahlena.Modules.Chat.Infrastructure.Persistence;
using Ahlena.Modules.Identity.Infrastructure.Persistence;

namespace Ahlena.UnitTests.API;

/// <summary>
/// Focused coverage for the L1 migration-application gate. Every test is
/// offline: the expectation is parsed, shaped and compared without a database,
/// and the guard must reject an unapproved target, a malformed expectation or an
/// unexpected pending-migration set *before* it can open a connection, so a
/// passing test also proves no migration was attempted.
///
/// The observed-versus-expected id comparison needs an approved target to read
/// the real pending ids, so the pure <c>DescribePendingSetMismatch</c> helper
/// carries that coverage offline; the approved-target happy path itself is
/// covered by the Owner-authorized execution recorded in the L1 brief and is
/// deliberately never exercised here.
/// </summary>
public sealed class LocalFixtureMigrationApplyTests
{
    private const string AdminUserActions = "20261012064508_AdminUserActions";
    private const string AddChatMessagesPreference = "20261013104759_AddChatMessagesPreference";
    private const string AddVerificationRequestVersion = "20261016133316_AddVerificationRequestVersion";
    private const string InitialChat = "20261011111627_InitialChat";
    private const string AddChatMessageLocation = "20261011145248_AddChatMessageLocation";
    private const string AddChatPlusChanges = "20261013000659_AddChatPlusChanges";
    private const string AddAttachmentCleanupIndex = "20261013171755_AddAttachmentCleanupIndex";

    [Theory]
    [InlineData("Host=localhost;Port=5432;Database=AhlenaDb;Username=test;Password=test")]
    [InlineData("Host=127.0.0.1;Port=5432;Database=AhlenaDb;Username=test;Password=test")]
    [InlineData("Host=::1;Port=5432;Database=AhlenaDb;Username=test;Password=test")]
    [InlineData("Host=LOCALHOST;Port=5432;Database=AhlenaDb;Username=test;Password=test")]
    public void ApprovedTarget_AcceptsOnlyLocalAhlenaDb(string connectionString)
    {
        Assert.True(TestUserSeedTargetGuard.IsApprovedPersistentLocalTarget(connectionString));
    }

    [Theory]
    [InlineData("Host=localhost;Port=5432;Database=SanadBrunoTestDb;Username=test;Password=test")]
    [InlineData("Host=localhost;Port=5433;Database=AhlenaDb;Username=test;Password=test")]
    [InlineData("Host=remote.example.test;Port=5432;Database=AhlenaDb;Username=test;Password=test")]
    [InlineData("Host=localhost;Port=5432;Database=ahlenadb;Username=test;Password=test")]
    [InlineData("Host=localhost;Port=5432;Database=postgres;Username=test;Password=test")]
    [InlineData("not-a-connection-string")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void UnapprovedTarget_IsRejected(string? connectionString)
    {
        Assert.False(TestUserSeedTargetGuard.IsApprovedPersistentLocalTarget(connectionString));
    }

    [Fact]
    public void UnapprovedTarget_NeverRevealsConnectionDetails()
    {
        // The predicate reports a bool only: no caller can log host, user or
        // password through it, so the failure classification stays safe.
        Assert.False(TestUserSeedTargetGuard.IsApprovedPersistentLocalTarget(
            "Host=remote.example.test;Port=5432;Database=AhlenaDb;Username=secret-user;Password=secret-password"));
    }

    [Theory]
    [InlineData(AdminUserActions)]
    [InlineData("a")]
    [InlineData("_")]
    [InlineData("A1_b2")]
    public void IsValidMigrationId_AcceptsAnEfMigrationId(string id)
    {
        Assert.True(TestUserSeedTargetGuard.IsValidMigrationId(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Add-Chat")]
    [InlineData("Add Chat")]
    [InlineData("Add.Chat")]
    [InlineData("Add=Chat")]
    [InlineData("Add;Chat")]
    [InlineData("Add,Chat")]
    [InlineData("Addümlaut")]
    public void IsValidMigrationId_RejectsAnythingThatIsNotAnId(string? id)
    {
        Assert.False(TestUserSeedTargetGuard.IsValidMigrationId(id));
    }

    [Fact]
    public void TryParseExpectation_ReadsAnExactIdSetPerModule()
    {
        bool parsed = TestUserSeedTargetGuard.TryParseExpectedPendingIds(
            $"Identity={AdminUserActions};{AddChatMessagesPreference},Chat={AddChatMessageLocation},Caregivers=",
            out Dictionary<string, IReadOnlyList<string>> expected,
            out string problem);

        Assert.True(parsed);
        Assert.Equal(string.Empty, problem);
        Assert.Equal(3, expected.Count);
        Assert.Equal(
            [AdminUserActions, AddChatMessagesPreference],
            expected["Identity"]);
        Assert.Equal([AddChatMessageLocation], expected["Chat"]);
        Assert.Empty(expected["Caregivers"]);
    }

    [Fact]
    public void TryParseExpectation_IgnoresSurroundingWhitespace()
    {
        bool parsed = TestUserSeedTargetGuard.TryParseExpectedPendingIds(
            $"  Identity = {AdminUserActions} ; {AddChatMessagesPreference} , Caregivers = ",
            out Dictionary<string, IReadOnlyList<string>> expected,
            out string problem);

        Assert.True(parsed);
        Assert.Equal(string.Empty, problem);
        Assert.Equal(2, expected.Count);
        Assert.Equal(
            [AdminUserActions, AddChatMessagesPreference],
            expected["Identity"]);
        Assert.Empty(expected["Caregivers"]);
    }

    [Theory]
    [InlineData(null, "expectation.not-provided")]
    [InlineData("", "expectation.not-provided")]
    [InlineData("   ", "expectation.not-provided")]
    [InlineData("Identity", "expectation.malformed-entry")]
    [InlineData("=abc", "expectation.malformed-entry")]
    [InlineData("Identity=a,Identity=b", "expectation.duplicate-module")]
    [InlineData("Identity=Add-Chat", "expectation.malformed-migration-id")]
    [InlineData("Identity=Add Chat", "expectation.malformed-migration-id")]
    [InlineData("Identity=;", "expectation.malformed-migration-id")]
    [InlineData("Identity=abc;abc", "expectation.duplicate-migration-id")]
    public void TryParseExpectation_RefusesAMalformedOrMissingExpectation(
        string? argument,
        string expectedProblem)
    {
        bool parsed = TestUserSeedTargetGuard.TryParseExpectedPendingIds(
            argument,
            out Dictionary<string, IReadOnlyList<string>> expected,
            out string problem);

        Assert.False(parsed);
        Assert.Equal(expectedProblem, problem);
        Assert.Empty(expected);
    }

    [Theory]
    [InlineData(",")]
    [InlineData(",,,")]
    [InlineData("   ,   ")]
    [InlineData(",Identity=a")]
    [InlineData("Identity=a,")]
    [InlineData("Identity=a,,Chat=b")]
    [InlineData("Identity=a,  ,Chat=b")]
    public void TryParseExpectation_RefusesLeadingTrailingOrRepeatedEmptyCommaEntries(
        string argument)
    {
        // An empty comma entry is a malformed expectation, not an absent one:
        // it must fail closed instead of being silently dropped.
        bool parsed = TestUserSeedTargetGuard.TryParseExpectedPendingIds(
            argument,
            out Dictionary<string, IReadOnlyList<string>> expected,
            out string problem);

        Assert.False(parsed);
        Assert.Equal("expectation.malformed-entry", problem);
        Assert.Empty(expected);
    }

    [Fact]
    public void TryParseExpectation_AcceptsTheSameEntriesWithoutEmptyOnes()
    {
        // The counterpart of the empty-entry theory: the identical entries with
        // the stray commas removed parse exactly as expected, so the rejection
        // above is about the empty entry and not about the entry shape.
        bool parsed = TestUserSeedTargetGuard.TryParseExpectedPendingIds(
            $"Identity={AdminUserActions},Chat={AddChatMessageLocation}",
            out Dictionary<string, IReadOnlyList<string>> expected,
            out string problem);

        Assert.True(parsed);
        Assert.Equal(string.Empty, problem);
        Assert.Equal(2, expected.Count);
        Assert.Equal([AdminUserActions], expected["Identity"]);
        Assert.Equal([AddChatMessageLocation], expected["Chat"]);
    }

    [Fact]
    public void DescribeExpectationShape_AcceptsAnExactNonEmptyModuleSet()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> expected = Expectation(
            ("Identity", [AdminUserActions, AddChatMessagesPreference, AddVerificationRequestVersion]),
            ("Caregivers", []));

        Assert.Null(TestUserSeedTargetGuard.DescribeExpectationShape(
            expected,
            ["Identity", "Caregivers"]));
    }

    [Fact]
    public void DescribeExpectationShape_RefusesAnEmptyExpectation()
    {
        Assert.Equal(
            "expectation.not-provided",
            TestUserSeedTargetGuard.DescribeExpectationShape(
                new Dictionary<string, IReadOnlyList<string>>(),
                ["Identity"]));
    }

    [Fact]
    public void DescribeExpectationShape_RefusesAMissingOrExtraModule()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> expected = Expectation(
            ("Identity", [AdminUserActions]));

        Assert.Equal(
            "expectation.module-set-mismatch",
            TestUserSeedTargetGuard.DescribeExpectationShape(expected, ["Identity", "Chat"]));
    }

    [Fact]
    public void DescribeExpectationShape_RefusesASameCountModuleSubstitution()
    {
        // Same number of entries, different names: the names must match exactly.
        IReadOnlyDictionary<string, IReadOnlyList<string>> expected = Expectation(
            ("Identity", [AdminUserActions]),
            ("Community", [AddChatPlusChanges]));

        Assert.Equal(
            "expectation.module-set-mismatch",
            TestUserSeedTargetGuard.DescribeExpectationShape(expected, ["Identity", "Chat"]));
    }

    [Fact]
    public void DescribeExpectationShape_RefusesADuplicateExpectedId()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> expected = Expectation(
            ("Identity", [AdminUserActions, AdminUserActions]));

        Assert.Equal(
            "expectation.duplicate-migration-id",
            TestUserSeedTargetGuard.DescribeExpectationShape(expected, ["Identity"]));
    }

    [Fact]
    public void DescribePendingSetMismatch_AcceptsAnExactlyMatchingIdSet()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> expected = Expectation(
            ("Identity", [AdminUserActions, AddChatMessagesPreference, AddVerificationRequestVersion]),
            ("Chat", [InitialChat, AddChatMessageLocation, AddChatPlusChanges, AddAttachmentCleanupIndex]),
            ("Caregivers", []));

        Assert.Null(TestUserSeedTargetGuard.DescribePendingSetMismatch(
            "Identity",
            [AdminUserActions, AddChatMessagesPreference, AddVerificationRequestVersion],
            expected));
        Assert.Null(TestUserSeedTargetGuard.DescribePendingSetMismatch(
            "Chat",
            [InitialChat, AddChatMessageLocation, AddChatPlusChanges, AddAttachmentCleanupIndex],
            expected));
        Assert.Null(TestUserSeedTargetGuard.DescribePendingSetMismatch("Caregivers", [], expected));
    }

    [Fact]
    public void DescribePendingSetMismatch_ComparesIdsAsASetNotASequence()
    {
        // EF returns pending migrations in a stable order, but membership is the
        // contract: a reordering of the same exact set is still the same set.
        IReadOnlyDictionary<string, IReadOnlyList<string>> expected = Expectation(
            ("Identity", [AdminUserActions, AddChatMessagesPreference]));

        Assert.Null(TestUserSeedTargetGuard.DescribePendingSetMismatch(
            "Identity",
            [AddChatMessagesPreference, AdminUserActions],
            expected));
    }

    [Fact]
    public void DescribePendingSetMismatch_RejectsASameCountDifferentIdSubstitution()
    {
        // The regression that motivated the exact-id gate: the count is
        // unchanged, but the observed set is a different migration.
        IReadOnlyDictionary<string, IReadOnlyList<string>> expected = Expectation(
            ("Identity", [AdminUserActions, AddChatMessagesPreference, AddVerificationRequestVersion]));

        Assert.Equal(
            "pending-set-mismatch",
            TestUserSeedTargetGuard.DescribePendingSetMismatch(
                "Identity",
                [AdminUserActions, AddChatMessagesPreference, InitialChat],
                expected));
    }

    [Fact]
    public void DescribePendingSetMismatch_RejectsAMissingOrExtraId()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> expected = Expectation(
            ("Identity", [AdminUserActions, AddChatMessagesPreference]));

        Assert.Equal(
            "pending-set-mismatch",
            TestUserSeedTargetGuard.DescribePendingSetMismatch(
                "Identity",
                [AdminUserActions],
                expected));

        Assert.Equal(
            "pending-set-mismatch",
            TestUserSeedTargetGuard.DescribePendingSetMismatch(
                "Identity",
                [AdminUserActions, AddChatMessagesPreference, AddVerificationRequestVersion],
                expected));
    }

    [Fact]
    public void DescribePendingSetMismatch_RejectsADuplicatedObservedId()
    {
        // A duplicate can never be a faithful reading of the pending set, even
        // when the observed list happens to hold the expected ids.
        IReadOnlyDictionary<string, IReadOnlyList<string>> expected = Expectation(
            ("Identity", [AdminUserActions, AdminUserActions]));

        Assert.Equal(
            "pending-set-mismatch",
            TestUserSeedTargetGuard.DescribePendingSetMismatch(
                "Identity",
                [AdminUserActions, AdminUserActions],
                expected));
    }

    [Fact]
    public void DescribePendingSetMismatch_RejectsAModuleOutsideTheApprovedSet()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> expected = Expectation(
            ("Identity", [AdminUserActions]));

        Assert.Equal(
            "unexpected-module",
            TestUserSeedTargetGuard.DescribePendingSetMismatch(
                "Cms",
                [AdminUserActions],
                expected));
    }

    [Fact]
    public async Task Apply_WithoutAnExpectationCoveringEveryModule_AppliesNothing()
    {
        using IdentityDbContext identity = NpgsqlIdentity("Host=localhost;Port=5432;Database=AhlenaDb;Username=test;Password=test");
        using ChatDbContext chat = NpgsqlChat("Host=localhost;Port=5432;Database=AhlenaDb;Username=test;Password=test");

        // One entry for two contexts: the expectation cannot be trusted.
        IReadOnlyDictionary<string, IReadOnlyList<string>> expected = Expectation(
            ("Identity", [AdminUserActions]));

        LocalFixtureMigrationApplyReport report =
            await TestUserSeedTargetGuard.ApplyPersistentLocalMigrationsAsync(
                Modules(identity, chat),
                expected);

        Assert.False(report.Passed);
        Assert.False(report.AppliedAnyMigrations);
        Assert.Empty(report.Modules);
        Assert.Equal(["expectation.module-set-mismatch"], report.FailedChecks);
    }

    [Fact]
    public async Task Apply_WithNoExpectationAtAll_AppliesNothing()
    {
        using IdentityDbContext identity = NpgsqlIdentity("Host=localhost;Port=5432;Database=AhlenaDb;Username=test;Password=test");
        using ChatDbContext chat = NpgsqlChat("Host=localhost;Port=5432;Database=AhlenaDb;Username=test;Password=test");

        LocalFixtureMigrationApplyReport report =
            await TestUserSeedTargetGuard.ApplyPersistentLocalMigrationsAsync(
                Modules(identity, chat),
                new Dictionary<string, IReadOnlyList<string>>());

        Assert.False(report.Passed);
        Assert.False(report.AppliedAnyMigrations);
        Assert.Empty(report.Modules);
        Assert.Equal(["expectation.not-provided"], report.FailedChecks);
    }

    [Fact]
    public async Task Apply_WithADuplicateExpectedId_AppliesNothing()
    {
        using IdentityDbContext identity = NpgsqlIdentity("Host=localhost;Port=5432;Database=AhlenaDb;Username=test;Password=test");
        using ChatDbContext chat = NpgsqlChat("Host=localhost;Port=5432;Database=AhlenaDb;Username=test;Password=test");

        IReadOnlyDictionary<string, IReadOnlyList<string>> expected = Expectation(
            ("Identity", [AdminUserActions, AdminUserActions]),
            ("Chat", [InitialChat]));

        LocalFixtureMigrationApplyReport report =
            await TestUserSeedTargetGuard.ApplyPersistentLocalMigrationsAsync(
                Modules(identity, chat),
                expected);

        Assert.False(report.Passed);
        Assert.False(report.AppliedAnyMigrations);
        Assert.Empty(report.Modules);
        Assert.Equal(["expectation.duplicate-migration-id"], report.FailedChecks);
    }

    [Fact]
    public async Task Apply_WithANonLocalTarget_FailsClosedBeforeAnyConnectionOrMigration()
    {
        // Both contexts resolve to a real relational provider whose target is
        // not the approved local AhlenaDb. The guard must classify them and stop
        // without connecting, so nothing is applied and a bogus remote host is
        // never contacted.
        using IdentityDbContext identity = NpgsqlIdentity(
            "Host=remote.example.test;Port=5432;Database=AhlenaDb;Username=test;Password=test");
        using ChatDbContext chat = NpgsqlChat(
            "Host=127.0.0.1;Port=5433;Database=AhlenaDb;Username=test;Password=test");

        IReadOnlyDictionary<string, IReadOnlyList<string>> expected = Expectation(
            ("Identity", [AdminUserActions]),
            ("Chat", [InitialChat]));

        LocalFixtureMigrationApplyReport report =
            await TestUserSeedTargetGuard.ApplyPersistentLocalMigrationsAsync(
                Modules(identity, chat),
                expected);

        Assert.False(report.Passed);
        Assert.False(report.AppliedAnyMigrations);
        Assert.Equal(["module.identity", "module.chat"], report.FailedChecks);
        Assert.All(report.Modules, module =>
        {
            Assert.False(module.Applied);
            Assert.Empty(module.AppliedMigrationIds);
            Assert.Equal("unexpected-target", module.SafeError);
            Assert.Equal(-1, module.PendingBefore);
        });
    }

    [Fact]
    public async Task Apply_WithASameCountModuleSubstitution_FailsClosedWithoutApplyingTheOthers()
    {
        using IdentityDbContext identity = NpgsqlIdentity(
            "Host=remote.example.test;Port=5432;Database=AhlenaDb;Username=test;Password=test");
        using ChatDbContext chat = NpgsqlChat(
            "Host=127.0.0.1;Port=5433;Database=AhlenaDb;Username=test;Password=test");

        // Same number of entries, but the names do not match the contexts: the
        // expectation is refused before any connection, so the unapproved
        // targets are never even classified.
        IReadOnlyDictionary<string, IReadOnlyList<string>> expected = Expectation(
            ("Identity", [AdminUserActions]),
            ("Community", [AddChatPlusChanges]));

        LocalFixtureMigrationApplyReport report =
            await TestUserSeedTargetGuard.ApplyPersistentLocalMigrationsAsync(
                Modules(identity, chat),
                expected);

        Assert.False(report.Passed);
        Assert.False(report.AppliedAnyMigrations);
        Assert.Empty(report.Modules);
        Assert.Equal(["expectation.module-set-mismatch"], report.FailedChecks);
    }

    [Fact]
    public void ApplyReport_CarriesNoConnectionDetails()
    {
        LocalFixtureMigrationApplyReport report = new(
            Passed: false,
            TargetDatabase: "AhlenaDb",
            AppliedAnyMigrations: false,
            Modules: [],
            FailedChecks: ["expectation.not-provided"]);

        string serialized = JsonSerializer.Serialize(
            report,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("Password", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Username", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AhlenaDb", serialized, StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Expectation(
        params (string Module, string[] Ids)[] modules)
    {
        Dictionary<string, IReadOnlyList<string>> expected = new(StringComparer.Ordinal);

        foreach ((string module, string[] ids) in modules)
        {
            expected[module] = ids;
        }

        return expected;
    }

    private static LocalFixtureModuleContext[] Modules(
        IdentityDbContext identity,
        ChatDbContext chat) =>
    [
        new LocalFixtureModuleContext("Identity", identity),
        new LocalFixtureModuleContext("Chat", chat)
    ];

    private static IdentityDbContext NpgsqlIdentity(string connectionString) =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(connectionString)
            .Options);

    private static ChatDbContext NpgsqlChat(string connectionString) =>
        new(new DbContextOptionsBuilder<ChatDbContext>()
            .UseNpgsql(connectionString)
            .Options);
}
