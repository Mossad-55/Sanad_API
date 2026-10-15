using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers;

// Bounded PORTAL-04 fixture helper (test-only, synthetic data).
// Provides one SQLite connection with attached `caregivers` + `identity`
// schemas so the REAL production list/count SQL (which references
// caregivers.caregivers and identity.users) can execute against minimal
// isolated tables. This proves supported join/reader semantics only; it is
// NOT PostgreSQL execution proof (see phase-04 report).
internal static class CaregiverIntegrityFixture
{
    // Fixed synthetic ids (not real users). Read back after seeding.
    internal static readonly Guid MatchedMedicalUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    internal static readonly Guid OrphanCompanionUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    internal static readonly Guid WrongRoleUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    internal static readonly Guid SecondOrphanUserId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    internal static readonly Guid MatchedCompanionUserId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    internal static readonly Guid MatchedMedicalCaregiverId = Guid.Parse("a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1");
    internal static readonly Guid OrphanCompanionCaregiverId = Guid.Parse("b2b2b2b2-b2b2-b2b2-b2b2-b2b2b2b2b2b2");
    internal static readonly Guid WrongRoleCaregiverId = Guid.Parse("c3c3c3c3-c3c3-c3c3-c3c3-c3c3c3c3c3c3");
    internal static readonly Guid SecondOrphanCaregiverId = Guid.Parse("d4d4d4d4-d4d4-d4d4-d4d4-d4d4d4d4d4d4");
    internal static readonly Guid MatchedCompanionCaregiverId = Guid.Parse("e5e5e5e5-e5e5-e5e5-e5e5-e5e5e5e5e5e5");
    internal static readonly Guid DuplicateMedicalCaregiverId = Guid.Parse("f6f6f6f6-f6f6-f6f6-f6f6-f6f6f6f6f6f6");

    internal static readonly DateTime Newest = new(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);
    internal static readonly DateTime Recent = new(2026, 10, 4, 10, 0, 0, DateTimeKind.Utc);
    internal static readonly DateTime Middle = new(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc);
    internal static readonly DateTime Older = new(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc);
    internal static readonly DateTime Oldest = new(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);

    internal sealed record SeededIds(
        Guid MatchedMedicalCaregiver,
        Guid OrphanCompanionCaregiver,
        Guid WrongRoleCaregiver,
        Guid SecondOrphanCaregiver,
        Guid MatchedCompanionCaregiver);

    // Opens a kept-open :memory: connection, attaches the two schemas, creates
    // minimal tables, seeds the core rows, and reads them back. The caller owns
    // the connection lifetime: it must stay open while the DbContext runs,
    // because EF leaves externally opened connections open (so ATTACH survives
    // the production method's Open/CloseConnection pair).
    internal static (SqliteConnection Connection, SeededIds Ids) CreateSeeded()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                """
                ATTACH DATABASE ':memory:' AS caregivers;
                ATTACH DATABASE ':memory:' AS identity;
                CREATE TABLE caregivers.caregivers (
                    id TEXT NOT NULL PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    type INTEGER NOT NULL,
                    status INTEGER NOT NULL,
                    availability INTEGER NOT NULL,
                    updated_on_utc TEXT NOT NULL
                );
                CREATE TABLE identity.users (
                    id TEXT NOT NULL PRIMARY KEY,
                    arabic_full_name TEXT NOT NULL,
                    english_full_name TEXT NOT NULL,
                    phone_number TEXT NOT NULL
                );
                CREATE TABLE identity.user_accounts (
                    user_id TEXT NOT NULL,
                    account_type INTEGER NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        InsertCaregiver(connection, MatchedMedicalCaregiverId, MatchedMedicalUserId, type: 1, status: 4, Newest);
        InsertCaregiver(connection, OrphanCompanionCaregiverId, OrphanCompanionUserId, type: 2, status: 2, Recent);
        InsertCaregiver(connection, WrongRoleCaregiverId, WrongRoleUserId, type: 2, status: 3, Middle);
        InsertCaregiver(connection, SecondOrphanCaregiverId, SecondOrphanUserId, type: 1, status: 5, Older);
        InsertCaregiver(connection, MatchedCompanionCaregiverId, MatchedCompanionUserId, type: 2, status: 4, Oldest);

        InsertUser(connection, MatchedMedicalUserId, "اسم مطابق", "Matched Medical", "+10000000001");
        InsertUser(connection, WrongRoleUserId, "اسم دور", "Wrong Role", "+10000000003");
        InsertUser(connection, MatchedCompanionUserId, "اسم مرافق", "Matched Companion", "+10000000005");

        // Account types: 2 = MedicalCaregiver, 3 = CompanionCaregiver, 1 = Family.
        InsertAccount(connection, MatchedMedicalUserId, 2);
        InsertAccount(connection, WrongRoleUserId, 1);
        InsertAccount(connection, MatchedCompanionUserId, 3);

        // Readback: the exact rows the scenarios will consume.
        int caregiverRows = ScalarInt(connection, "SELECT COUNT(*) FROM caregivers.caregivers;");
        int userRows = ScalarInt(connection, "SELECT COUNT(*) FROM identity.users;");
        if (caregiverRows != 5 || userRows != 3)
        {
            throw new InvalidOperationException(
                $"Fixture readback failed: caregivers={caregiverRows}, users={userRows}.");
        }

        return (connection, new SeededIds(
            MatchedMedicalCaregiverId,
            OrphanCompanionCaregiverId,
            WrongRoleCaregiverId,
            SecondOrphanCaregiverId,
            MatchedCompanionCaregiverId));
    }

    internal static CaregiversDbContext CreateDbContext(SqliteConnection connection)
    {
        DbContextOptions<CaregiversDbContext> options =
            new DbContextOptionsBuilder<CaregiversDbContext>()
                .UseSqlite(connection)
                .Options;

        return new CaregiversDbContext(options);
    }

    internal static void InsertCaregiver(
        SqliteConnection connection,
        Guid caregiverId,
        Guid userId,
        int type,
        int status,
        DateTime updatedOnUtc)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO caregivers.caregivers (id, user_id, type, status, availability, updated_on_utc) " +
            "VALUES ($id, $userId, $type, $status, 1, $updated);";
        command.Parameters.AddWithValue("$id", caregiverId.ToString("D"));
        command.Parameters.AddWithValue("$userId", userId.ToString("D"));
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$updated", updatedOnUtc.ToString("o"));
        command.ExecuteNonQuery();
    }

    internal static void InsertUser(
        SqliteConnection connection,
        Guid userId,
        string arabicName,
        string englishName,
        string phone)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO identity.users (id, arabic_full_name, english_full_name, phone_number) " +
            "VALUES ($id, $ar, $en, $phone);";
        command.Parameters.AddWithValue("$id", userId.ToString("D"));
        command.Parameters.AddWithValue("$ar", arabicName);
        command.Parameters.AddWithValue("$en", englishName);
        command.Parameters.AddWithValue("$phone", phone);
        command.ExecuteNonQuery();
    }

    internal static void InsertAccount(SqliteConnection connection, Guid userId, int accountType)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO identity.user_accounts (user_id, account_type) VALUES ($userId, $type);";
        command.Parameters.AddWithValue("$userId", userId.ToString("D"));
        command.Parameters.AddWithValue("$type", accountType);
        command.ExecuteNonQuery();
    }

    private static int ScalarInt(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
