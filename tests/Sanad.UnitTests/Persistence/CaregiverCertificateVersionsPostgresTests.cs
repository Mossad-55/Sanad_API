using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.Discovery;
using Sanad.Modules.Caregivers.Application.Lookups;
using Sanad.Modules.Caregivers.Application.Onboarding;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Persistence;

/// <summary>
/// Relational proof for the caregiver-certificate versioning migration. The
/// target is a uniquely named disposable database derived from
/// <c>ConnectionStrings__IdentityIntegrationDatabase</c>; the source host must
/// be loopback on port 5432 and the generated name is verified absent before
/// creation. Only the Caregivers migrations are applied and only that exact
/// nonce database is deleted.
///
/// The proof pins the two behaviours the in-memory provider cannot express:
/// the partial unique index admits at most one <em>live</em> current version
/// per qualification (a second one is a 23505 on
/// <c>ux_caregiver_certificate_versions_current</c>) while a soft-removed
/// history row is exempt, and the nested composite foreign key cascades when
/// the owning certificate is deleted.
/// </summary>
[Collection("CaregiverCertificateVersionsVerification")]
public sealed class CaregiverCertificateVersionsPostgresTests : IAsyncLifetime
{
    private const string ConnectionStringVariable =
        "ConnectionStrings__IdentityIntegrationDatabase";

    private static readonly string ExpectedDatabase =
        "SanadCertVersionVerify_" + Guid.NewGuid().ToString("N");

    private static string? _connectionString;
    private static bool _createdTarget;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (!_createdTarget || string.IsNullOrWhiteSpace(_connectionString))
            return;

        await using CaregiversDbContext caregivers =
            CreateCaregiversDb(_connectionString);
        await caregivers.Database.EnsureDeletedAsync();

        _createdTarget = false;
        _connectionString = null;
    }

    [CaregiverCertificateVersionsPostgresFact]
    public async Task Version_table_enforces_one_live_current_and_exempts_removed_rows()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);

        await using (CaregiversDbContext check =
            CreateCaregiversDb(connectionString))
        {
            Assert.Contains(
                "20261027120000_AddCaregiverCertificateVersions",
                await check.Database.GetAppliedMigrationsAsync());
        }

        // The row-level tombstone + concurrency columns were added additively.
        int tombstoneColumns;
        string indexDefinition;
        string foreignKeyDeleteAction;
        await using (CaregiversDbContext schema =
            CreateCaregiversDb(connectionString))
        {
            tombstoneColumns = await schema.Database.SqlQueryRaw<int>(
                """
                SELECT COUNT(*)::int AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'caregivers'
                  AND table_name = 'caregiver_certificates'
                  AND column_name IN ('is_removed', 'removed_on_utc', 'revision')
                """).SingleAsync();

            indexDefinition = await schema.Database.SqlQueryRaw<string>(
                """
                SELECT indexdef AS "Value"
                FROM pg_indexes
                WHERE schemaname = 'caregivers'
                  AND indexname = 'ux_caregiver_certificate_versions_current'
                """).SingleAsync();

            foreignKeyDeleteAction = await schema.Database.SqlQueryRaw<string>(
                """
                SELECT confdeltype::text AS "Value"
                FROM pg_constraint
                WHERE conname LIKE
                    'FK_caregiver_certificate_versions_caregiver_certificates%'
                """).SingleAsync();
        }

        Assert.Equal(3, tombstoneColumns);
        Assert.Contains("UNIQUE", indexDefinition);
        Assert.Contains("is_current", indexDefinition);
        Assert.Contains("is_removed", indexDefinition);
        // confdeltype 'c' = ON DELETE CASCADE on the nested composite FK.
        Assert.Equal("c", foreignKeyDeleteAction);

        CaregiverId caregiverId;
        Guid certificateId;

        await using (CaregiversDbContext write =
            CreateCaregiversDb(connectionString))
        {
            Caregiver caregiver =
                Caregiver.Create(UserId.New(), CaregiverType.Medical);

            caregiver.AddCertificate(
                CaregiverCertificateType.PracticeLicense,
                "certificates/practice-license.pdf",
                expiryDate: null,
                new DateOnly(2026, 1, 1));

            write.Caregivers.Add(caregiver);
            await write.SaveChangesAsync();

            caregiverId = caregiver.Id;
            certificateId = caregiver.Certificates.Single().Id.Value;
        }

        Guid firstCurrent = Guid.NewGuid();
        Guid secondCurrent = Guid.NewGuid();
        Guid thirdCurrent = Guid.NewGuid();

        await InsertVersionAsync(
            connectionString,
            caregiverId,
            certificateId,
            firstCurrent,
            isCurrent: true);

        PostgresException duplicate = await Assert.ThrowsAsync<PostgresException>(
            () => InsertVersionAsync(
                connectionString,
                caregiverId,
                certificateId,
                secondCurrent,
                isCurrent: true));

        Assert.Equal("23505", duplicate.SqlState);
        Assert.Equal(
            "ux_caregiver_certificate_versions_current",
            duplicate.ConstraintName);

        // Soft-removing the current row takes it out of the partial index, so a
        // replacement current is now admitted: removed history never blocks a
        // new approval.
        await MarkVersionRemovedAsync(
            connectionString,
            caregiverId,
            certificateId,
            firstCurrent);

        await InsertVersionAsync(
            connectionString,
            caregiverId,
            certificateId,
            secondCurrent,
            isCurrent: true);

        // And a third live current is again refused: the exemption is for
        // removed rows only.
        PostgresException duplicateAgain =
            await Assert.ThrowsAsync<PostgresException>(
                () => InsertVersionAsync(
                    connectionString,
                    caregiverId,
                    certificateId,
                    thirdCurrent,
                    isCurrent: true));

        Assert.Equal("23505", duplicateAgain.SqlState);

        int liveCurrent;
        await using (CaregiversDbContext read =
            CreateCaregiversDb(connectionString))
        {
            liveCurrent = await read.Database.SqlQueryRaw<int>(
                """
                SELECT COUNT(*)::int AS "Value"
                FROM caregivers.caregiver_certificate_versions
                WHERE caregiver_id = @caregiverId
                  AND certificate_id = @certificateId
                  AND is_current
                  AND NOT is_removed
                """,
                new NpgsqlParameter("caregiverId", caregiverId.Value),
                new NpgsqlParameter("certificateId", certificateId))
                .SingleAsync();
        }

        Assert.Equal(1, liveCurrent);
    }

    /// <summary>
    /// Primitive negative control for <see cref="CaregiversDbContext
    /// .ExecuteInTransactionAsync{TResult}"/>: a save that already ran inside
    /// the unit of work is reverted when the callback later throws, so a
    /// multi-save promotion can never leave a half-applied state.
    /// </summary>
    [CaregiverCertificateVersionsPostgresFact]
    public async Task Execute_in_transaction_rolls_back_an_earlier_save_on_failure()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);

        (CaregiverId caregiverId, Guid certificateId, _) =
            await SeedVerifiedWithPendingReplacementAsync(connectionString);

        await using (CaregiversDbContext context =
            CreateCaregiversDb(connectionString))
        {
            Caregiver caregiver = await context.Caregivers
                .Include(c => c.Certificates)
                .SingleAsync(c => c.Id == caregiverId);

            CaregiverCertificate certificate =
                caregiver.Certificates.Single();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                context.ExecuteInTransactionAsync<object?>(
                    async token =>
                    {
                        // This save lands inside the transaction and is then
                        // abandoned when the callback throws below.
                        caregiver.UpdateCertificateFile(
                            certificate.Id,
                            "certificates/rolled-back.pdf",
                            expiryDate: null,
                            new DateOnly(2026, 1, 1),
                            DateTime.UtcNow);

                        await context.SaveChangesAsync(token);

                        throw new InvalidOperationException("boom");
                    },
                    default));
        }

        // The rows are read back through a fresh context: the change tracker of
        // the rolled-back context still holds its abandoned insert, so only a
        // new context proves the database state was reverted.
        await using CaregiversDbContext read =
            CreateCaregiversDb(connectionString);

        Caregiver reloaded = await read.Caregivers
            .Include(c => c.Certificates)
            .SingleAsync(c => c.Id == caregiverId);

        CaregiverCertificate reloadedCertificate =
            reloaded.Certificates.Single();

        Assert.Equal(2, reloadedCertificate.Versions.Count);
        Assert.DoesNotContain(
            reloadedCertificate.Versions,
            version => version.FilePath == "certificates/rolled-back.pdf");
        Assert.DoesNotContain(
            await read.Database.SqlQueryRaw<string>(
                """
                SELECT file_path AS "Value"
                FROM caregivers.caregiver_certificate_versions
                WHERE caregiver_id = @caregiverId
                  AND certificate_id = @certificateId
                """,
                new NpgsqlParameter("caregiverId", caregiverId.Value),
                new NpgsqlParameter("certificateId", certificateId))
                .ToListAsync(),
            path => path == "certificates/rolled-back.pdf");
    }

    /// <summary>
    /// The promoted half of the two-phase verify writes the second save. When
    /// that save fails, the demotion already persisted by the first save must
    /// roll back with it: the qualification keeps its previous approved current
    /// version and the replacement stays pending (never a zero-current,
    /// still-Verified intermediate).
    /// </summary>
    [CaregiverCertificateVersionsPostgresFact]
    public async Task Verify_promotion_second_save_failure_rolls_back_the_demotion()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);

        (CaregiverId caregiverId, Guid certificateId, Guid originalVersionId) =
            await SeedVerifiedWithPendingReplacementAsync(connectionString);

        await using (CaregiversDbContext inner =
            CreateCaregiversDb(connectionString))
        {
            var failing = new FailSecondSaveCaregiversDbContext(inner);

            Result result = await new VerifyCertificateCommandHandler(failing)
                .Handle(
                    new VerifyCertificateCommand(
                        caregiverId,
                        new CaregiverCertificateId(certificateId)),
                    default);

            Assert.True(result.IsFailure);
            Assert.Equal(OnboardingErrors.CertificateConflict, result.Error);
        }

        await using CaregiversDbContext read =
            CreateCaregiversDb(connectionString);

        Caregiver reloaded = await read.Caregivers
            .Include(c => c.Certificates)
            .SingleAsync(c => c.Id == caregiverId);

        CaregiverCertificate certificate = reloaded.Certificates.Single();

        // The effective row snapshot and the current version are untouched.
        Assert.Equal(
            CertificateVerificationStatus.Verified,
            certificate.VerificationStatus);
        Assert.Equal("certificates/original.pdf", certificate.FilePath);

        Assert.NotNull(certificate.CurrentVersion);
        Assert.Equal(originalVersionId, certificate.CurrentVersion!.Id.Value);
        Assert.Equal(
            CertificateVerificationStatus.Verified,
            certificate.CurrentVersion.VerificationStatus);

        // The replacement was never promoted: it is still the live pending
        // candidate, and exactly one version is current.
        Assert.NotNull(certificate.PendingVersion);
        Assert.Equal(
            CertificateVerificationStatus.Pending,
            certificate.PendingVersion!.VerificationStatus);
        Assert.Single(
            certificate.Versions,
            version => version.IsCurrent);
    }

    /// <summary>
    /// A concurrent writer bumping the qualification revision makes the
    /// promotion's first save lose the optimistic-concurrency race. The whole
    /// unit rolls back: the previous approved current stays in force and no
    /// version (current or pending) is lost or mutated by the losing attempt.
    /// </summary>
    [CaregiverCertificateVersionsPostgresFact]
    public async Task Verify_promotion_revision_race_loses_without_lost_history()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);

        (CaregiverId caregiverId, Guid certificateId, Guid originalVersionId) =
            await SeedVerifiedWithPendingReplacementAsync(connectionString);

        await using (CaregiversDbContext inner =
            CreateCaregiversDb(connectionString))
        {
            var racing = new ConcurrentRevisionBumpCaregiversDbContext(
                inner,
                () => BumpCertificateRevisionAsync(
                    connectionString,
                    caregiverId,
                    certificateId));

            Result result = await new VerifyCertificateCommandHandler(racing)
                .Handle(
                    new VerifyCertificateCommand(
                        caregiverId,
                        new CaregiverCertificateId(certificateId)),
                    default);

            Assert.True(result.IsFailure);
            Assert.Equal(OnboardingErrors.CertificateConflict, result.Error);
        }

        await using CaregiversDbContext read =
            CreateCaregiversDb(connectionString);

        Caregiver reloaded = await read.Caregivers
            .Include(c => c.Certificates)
            .SingleAsync(c => c.Id == caregiverId);

        CaregiverCertificate certificate = reloaded.Certificates.Single();

        Assert.Equal(2, certificate.Versions.Count);
        Assert.Single(
            certificate.Versions,
            version => version.IsCurrent);

        Assert.Equal(originalVersionId, certificate.CurrentVersion!.Id.Value);
        Assert.Equal(
            CertificateVerificationStatus.Verified,
            certificate.CurrentVersion.VerificationStatus);
        Assert.Equal(
            CertificateVerificationStatus.Pending,
            certificate.PendingVersion!.VerificationStatus);
        Assert.Equal(
            CertificateVerificationStatus.Verified,
            certificate.VerificationStatus);
        Assert.Equal("certificates/original.pdf", certificate.FilePath);
    }

    private static async Task<(CaregiverId CaregiverId, Guid CertificateId, Guid OriginalVersionId)>
        SeedVerifiedWithPendingReplacementAsync(string connectionString)
    {
        await using CaregiversDbContext seed =
            CreateCaregiversDb(connectionString);

        Caregiver caregiver =
            Caregiver.Create(UserId.New(), CaregiverType.Medical);

        caregiver.AddCertificate(
            CaregiverCertificateType.PracticeLicense,
            "certificates/original.pdf",
            expiryDate: null,
            new DateOnly(2026, 1, 1));

        CaregiverCertificateId certificateId =
            caregiver.Certificates.Single().Id;

        caregiver.VerifyCertificate(certificateId);

        caregiver.UpdateCertificateFile(
            certificateId,
            "certificates/replacement.pdf",
            expiryDate: null,
            new DateOnly(2026, 1, 1),
            DateTime.UtcNow);

        Guid originalVersionId =
            caregiver.Certificates.Single().CurrentVersion!.Id.Value;

        seed.Caregivers.Add(caregiver);
        await seed.SaveChangesAsync();

        return (caregiver.Id, certificateId.Value, originalVersionId);
    }

    private static async Task BumpCertificateRevisionAsync(
        string connectionString,
        CaregiverId caregiverId,
        Guid certificateId)
    {
        await using var connection =
            new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE caregivers.caregiver_certificates
            SET revision = revision + 1
            WHERE caregiver_id = @caregiverId
              AND id = @certificateId
            """;
        command.Parameters.AddWithValue("caregiverId", caregiverId.Value);
        command.Parameters.AddWithValue("certificateId", certificateId);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertVersionAsync(
        string connectionString,
        CaregiverId caregiverId,
        Guid certificateId,
        Guid versionId,
        bool isCurrent)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO caregivers.caregiver_certificate_versions
                (caregiver_id, certificate_id, id, file_path, expiry_date,
                 verification_status, review_reason, is_current,
                 superseded_on_utc, is_removed, removed_on_utc,
                 removed_by_user_id, created_on_utc, updated_on_utc)
            VALUES
                (@caregiverId, @certificateId, @versionId, @filePath, NULL,
                 2, NULL, @isCurrent,
                 NULL, FALSE, NULL,
                 NULL, @now, @now)
            """;
        command.Parameters.AddWithValue("caregiverId", caregiverId.Value);
        command.Parameters.AddWithValue("certificateId", certificateId);
        command.Parameters.AddWithValue("versionId", versionId);
        command.Parameters.AddWithValue(
            "filePath",
            "certificates/history.pdf");
        command.Parameters.AddWithValue("isCurrent", isCurrent);
        command.Parameters.AddWithValue("now", DateTime.UtcNow);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task MarkVersionRemovedAsync(
        string connectionString,
        CaregiverId caregiverId,
        Guid certificateId,
        Guid versionId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE caregivers.caregiver_certificate_versions
            SET is_removed = TRUE,
                removed_on_utc = @now,
                removed_by_user_id = @actor
            WHERE caregiver_id = @caregiverId
              AND certificate_id = @certificateId
              AND id = @versionId
            """;
        command.Parameters.AddWithValue("caregiverId", caregiverId.Value);
        command.Parameters.AddWithValue("certificateId", certificateId);
        command.Parameters.AddWithValue("versionId", versionId);
        command.Parameters.AddWithValue("actor", Guid.NewGuid());
        command.Parameters.AddWithValue("now", DateTime.UtcNow);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task EnsureMigratedAsync(string connectionString)
    {
        if (_createdTarget && _connectionString == connectionString)
            return;

        await EnsureFreshDatabaseAbsentAsync(connectionString);

        _createdTarget = true;

        await using CaregiversDbContext caregivers =
            CreateCaregiversDb(connectionString);
        await caregivers.Database.MigrateAsync();

        _connectionString = connectionString;
    }

    private static string GetValidatedConnectionString()
    {
        string? raw =
            Environment.GetEnvironmentVariable(ConnectionStringVariable);

        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                "The caregiver certificate version proof requires " +
                ConnectionStringVariable + ".");

        var builder = new NpgsqlConnectionStringBuilder(raw);

        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1") ||
            builder.Port != 5432)
            throw new InvalidOperationException(
                "Refusing to write: the certificate version verification " +
                "source must be localhost:5432 PostgreSQL.");

        builder.Database = ExpectedDatabase;
        return builder.ConnectionString;
    }

    private static async Task EnsureFreshDatabaseAbsentAsync(
        string connectionString)
    {
        var maintenance = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres"
        };

        await using var connection =
            new NpgsqlConnection(maintenance.ConnectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @database)";
        command.Parameters.AddWithValue("database", ExpectedDatabase);

        bool exists = (bool)(await command.ExecuteScalarAsync() ?? false);

        if (exists)
            throw new InvalidOperationException(
                "Refusing to write: the randomly named certificate version " +
                "target already exists.");
    }

    private static CaregiversDbContext CreateCaregiversDb(
        string connectionString) =>
        new(new DbContextOptionsBuilder<CaregiversDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    CaregiversDbContext.Schema))
            .Options);

    /// <summary>
    /// Delegates every read to a real (Postgres) context, but the promotion's
    /// SECOND <see cref="SaveChangesAsync"/> throws a concurrency failure. The
    /// transaction opened by <see cref="ExecuteInTransactionAsync{TResult}"/>
    /// therefore has to roll the already-persisted demotion back. This is the
    /// only way to provoke a "save 2 fails after save 1 succeeded" on a real
    /// provider without racing two connections.
    /// </summary>
    private sealed class FailSecondSaveCaregiversDbContext(
        ICaregiversDbContext inner) : ICaregiversDbContext
    {
        private int _saveCount;

        public DbSet<Caregiver> Caregivers => inner.Caregivers;
        public DbSet<CaregiverRating> CaregiverRatings => inner.CaregiverRatings;
        public DbSet<CaregiverRatingModerationAction> RatingModerationActions => inner.RatingModerationActions;
        public DbSet<CaregiverReviewNote> ReviewNotes => inner.ReviewNotes;
        public DbSet<Service> Services => inner.Services;
        public DbSet<Language> Languages => inner.Languages;
        public DbSet<Bank> Banks => inner.Banks;
        public DbSet<CaregiverPayoutAccount> PayoutAccounts => inner.PayoutAccounts;
        public DbSet<CaregiverPayoutAccountReview> PayoutAccountReviews => inner.PayoutAccountReviews;
        public DbSet<CaregiverPayout> Payouts => inner.Payouts;
        public DbSet<Governorate> Governorates => inner.Governorates;
        public DbSet<City> Cities => inner.Cities;
        public DbSet<Area> Areas => inner.Areas;
        public DbSet<Specialization> Specializations => inner.Specializations;
        public DbSet<ProfessionalTitle> ProfessionalTitles => inner.ProfessionalTitles;
        public DbSet<AcademicDegree> AcademicDegrees => inner.AcademicDegrees;

        public Task<IReadOnlyList<AdminCaregiverListItem>> GetAdminCaregiversAsync(
            int page, int pageSize, int? status, int? type,
            CancellationToken cancellationToken = default) =>
            inner.GetAdminCaregiversAsync(page, pageSize, status, type, cancellationToken);

        public Task<int> CountAdminCaregiversAsync(
            int? status, int? type,
            CancellationToken cancellationToken = default) =>
            inner.CountAdminCaregiversAsync(status, type, cancellationToken);

        public Task<(IReadOnlyList<AdminCaregiverLookupItem> Items, int TotalCount)> GetAdminCaregiverLookupAsync(
            string? search, int page, int pageSize,
            CancellationToken cancellationToken = default) =>
            inner.GetAdminCaregiverLookupAsync(search, page, pageSize, cancellationToken);

        public Task<(IReadOnlyList<CaregiverSearchCardResponse> Items, int TotalCount)> SearchActiveCaregiversAsync(
            string? search, int? type, int? gender, Guid? governorateId, Guid? cityId, Guid? areaId, Guid? specializationId,
            int? availability, decimal? minPrice, decimal? maxPrice, decimal? minRating,
            int? minExperienceYears, int page, int pageSize,
            CancellationToken cancellationToken = default) =>
            inner.SearchActiveCaregiversAsync(
                search, type, gender, governorateId, cityId, areaId, specializationId, availability,
                minPrice, maxPrice, minRating, minExperienceYears, page, pageSize,
                cancellationToken);

        public Task<(IReadOnlyList<MedicalCaregiverRecipientItem> Items, int TotalCount)> SearchActiveMedicalCaregiversAsync(
            string? search, int page, int pageSize,
            CancellationToken cancellationToken = default) =>
            inner.SearchActiveMedicalCaregiversAsync(search, page, pageSize, cancellationToken);

        public Task<CaregiverUserHeader?> GetCaregiverUserHeaderAsync(
            UserId userId,
            CancellationToken cancellationToken = default) =>
            inner.GetCaregiverUserHeaderAsync(userId, cancellationToken);

        public Task<string?> GetDiscoverableCaregiverAvatarKeyAsync(
            CaregiverId caregiverId,
            CancellationToken cancellationToken = default) =>
            inner.GetDiscoverableCaregiverAvatarKeyAsync(caregiverId, cancellationToken);

        public Task<IReadOnlyList<TopRatedCaregiverCard>> GetTopRatedCaregiversAsync(
            CancellationToken cancellationToken = default) =>
            inner.GetTopRatedCaregiversAsync(cancellationToken);

        public Task<PayoutRecordSaveResult> SavePayoutIfAccountRevisionVerifiedAsync(
            CaregiverId caregiverId, int expectedRevision, CaregiverPayout payout,
            CancellationToken cancellationToken = default) =>
            inner.SavePayoutIfAccountRevisionVerifiedAsync(
                caregiverId, expectedRevision, payout, cancellationToken);

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default) =>
            inner.ExecuteInTransactionAsync(operation, cancellationToken);

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            _saveCount++;

            if (_saveCount >= 2)
            {
                throw new DbUpdateConcurrencyException(
                    "Injected failure on the promotion's second save.");
            }

            return inner.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Delegates every read to a real (Postgres) context, but immediately before
    /// its FIRST <see cref="SaveChangesAsync"/> it lets a concurrent writer bump
    /// the qualification revision on a separate connection. The promotion's
    /// optimistic-concurrency check (the revision token on the owning
    /// certificate row, touched by the demotion) therefore loses the race and the
    /// whole unit must roll back without losing the previous approved current
    /// version. The bump happens after this context loaded the aggregate, which
    /// is what makes the optimistic check fail; bumping before the load would be
    /// masked by the fresh read.
    /// </summary>
    private sealed class ConcurrentRevisionBumpCaregiversDbContext(
        ICaregiversDbContext inner,
        Func<Task> bumpAsync) : ICaregiversDbContext
    {
        private int _saveCount;

        public DbSet<Caregiver> Caregivers => inner.Caregivers;
        public DbSet<CaregiverRating> CaregiverRatings => inner.CaregiverRatings;
        public DbSet<CaregiverRatingModerationAction> RatingModerationActions => inner.RatingModerationActions;
        public DbSet<CaregiverReviewNote> ReviewNotes => inner.ReviewNotes;
        public DbSet<Service> Services => inner.Services;
        public DbSet<Language> Languages => inner.Languages;
        public DbSet<Bank> Banks => inner.Banks;
        public DbSet<CaregiverPayoutAccount> PayoutAccounts => inner.PayoutAccounts;
        public DbSet<CaregiverPayoutAccountReview> PayoutAccountReviews => inner.PayoutAccountReviews;
        public DbSet<CaregiverPayout> Payouts => inner.Payouts;
        public DbSet<Governorate> Governorates => inner.Governorates;
        public DbSet<City> Cities => inner.Cities;
        public DbSet<Area> Areas => inner.Areas;
        public DbSet<Specialization> Specializations => inner.Specializations;
        public DbSet<ProfessionalTitle> ProfessionalTitles => inner.ProfessionalTitles;
        public DbSet<AcademicDegree> AcademicDegrees => inner.AcademicDegrees;

        public Task<IReadOnlyList<AdminCaregiverListItem>> GetAdminCaregiversAsync(
            int page, int pageSize, int? status, int? type,
            CancellationToken cancellationToken = default) =>
            inner.GetAdminCaregiversAsync(page, pageSize, status, type, cancellationToken);

        public Task<int> CountAdminCaregiversAsync(
            int? status, int? type,
            CancellationToken cancellationToken = default) =>
            inner.CountAdminCaregiversAsync(status, type, cancellationToken);

        public Task<(IReadOnlyList<AdminCaregiverLookupItem> Items, int TotalCount)> GetAdminCaregiverLookupAsync(
            string? search, int page, int pageSize,
            CancellationToken cancellationToken = default) =>
            inner.GetAdminCaregiverLookupAsync(search, page, pageSize, cancellationToken);

        public Task<(IReadOnlyList<CaregiverSearchCardResponse> Items, int TotalCount)> SearchActiveCaregiversAsync(
            string? search, int? type, int? gender, Guid? governorateId, Guid? cityId, Guid? areaId, Guid? specializationId,
            int? availability, decimal? minPrice, decimal? maxPrice, decimal? minRating,
            int? minExperienceYears, int page, int pageSize,
            CancellationToken cancellationToken = default) =>
            inner.SearchActiveCaregiversAsync(
                search, type, gender, governorateId, cityId, areaId, specializationId, availability,
                minPrice, maxPrice, minRating, minExperienceYears, page, pageSize,
                cancellationToken);

        public Task<(IReadOnlyList<MedicalCaregiverRecipientItem> Items, int TotalCount)> SearchActiveMedicalCaregiversAsync(
            string? search, int page, int pageSize,
            CancellationToken cancellationToken = default) =>
            inner.SearchActiveMedicalCaregiversAsync(search, page, pageSize, cancellationToken);

        public Task<CaregiverUserHeader?> GetCaregiverUserHeaderAsync(
            UserId userId,
            CancellationToken cancellationToken = default) =>
            inner.GetCaregiverUserHeaderAsync(userId, cancellationToken);

        public Task<string?> GetDiscoverableCaregiverAvatarKeyAsync(
            CaregiverId caregiverId,
            CancellationToken cancellationToken = default) =>
            inner.GetDiscoverableCaregiverAvatarKeyAsync(caregiverId, cancellationToken);

        public Task<IReadOnlyList<TopRatedCaregiverCard>> GetTopRatedCaregiversAsync(
            CancellationToken cancellationToken = default) =>
            inner.GetTopRatedCaregiversAsync(cancellationToken);

        public Task<PayoutRecordSaveResult> SavePayoutIfAccountRevisionVerifiedAsync(
            CaregiverId caregiverId, int expectedRevision, CaregiverPayout payout,
            CancellationToken cancellationToken = default) =>
            inner.SavePayoutIfAccountRevisionVerifiedAsync(
                caregiverId, expectedRevision, payout, cancellationToken);

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default) =>
            inner.ExecuteInTransactionAsync(operation, cancellationToken);

        public async Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            _saveCount++;

            if (_saveCount == 1)
            {
                await bumpAsync();
            }

            return await inner.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class CaregiverCertificateVersionsPostgresFactAttribute
        : FactAttribute
    {
        public CaregiverCertificateVersionsPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(ConnectionStringVariable)))
            {
                Skip = "Caregiver certificate version PostgreSQL proof is " +
                       "skipped. Set " +
                       ConnectionStringVariable +
                       " to a local PostgreSQL instance to run it against a " +
                       "uniquely named disposable target.";
            }
        }
    }
}

[CollectionDefinition(
    "CaregiverCertificateVersionsVerification",
    DisableParallelization = true)]
public sealed class CaregiverCertificateVersionsVerificationCollection
{
}
