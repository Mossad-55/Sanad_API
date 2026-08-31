using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.Onboarding;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence;

public sealed class CaregiversDbContext :
    DbContext,
    ICaregiversDbContext
{
    public const string Schema = "caregivers";

    public CaregiversDbContext(
        DbContextOptions<CaregiversDbContext> options)
        : base(options)
    {
    }

    public DbSet<Caregiver> Caregivers => Set<Caregiver>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Language> Languages => Set<Language>();
    public DbSet<Governorate> Governorates => Set<Governorate>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<Specialization> Specializations => Set<Specialization>();
    public DbSet<ProfessionalTitle> ProfessionalTitles => Set<ProfessionalTitle>();
    public DbSet<AcademicDegree> AcademicDegrees => Set<AcademicDegree>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(CaregiversDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }

    public async Task<IReadOnlyList<AdminCaregiverListItem>>
    GetAdminCaregiversAsync(
        int page,
        int pageSize,
        int? status,
        int? type,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            """
            SELECT
                c.id                AS "CaregiverId",
                c.user_id           AS "UserId",
                c.type              AS "Type",
                c.status            AS "Status",
                c.availability      AS "Availability",
                u.arabic_full_name  AS "ArabicFullName",
                u.english_full_name AS "EnglishFullName",
                u.phone_number      AS "PhoneNumber",
                c.updated_on_utc    AS "UpdatedOnUtc"
            FROM caregivers.caregivers c
            LEFT JOIN identity.users u
                ON u.id = c.user_id
            WHERE (@statusFilter IS NULL OR c.status = @statusFilter)
              AND (@typeFilter IS NULL OR c.type = @typeFilter)
            ORDER BY c.updated_on_utc DESC, c.id
            LIMIT @take OFFSET @skip
            """;

        List<AdminCaregiverListItem> items = [];

        await Database.OpenConnectionAsync(cancellationToken);

        try
        {
            using DbCommand command =
                Database.GetDbConnection().CreateCommand();

            command.CommandText = sql;

            AddFilterParameters(command, status, type);
            command.Parameters.Add(
                CreateParameter(command, "take", pageSize));
            command.Parameters.Add(
                CreateParameter(
                    command,
                    "skip",
                    (page - 1) * pageSize));

            await using DbDataReader reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(
                    new AdminCaregiverListItem(
                        reader.GetGuid(
                            reader.GetOrdinal("CaregiverId")),
                        reader.GetGuid(
                            reader.GetOrdinal("UserId")),
                        (CaregiverType)reader.GetInt32(
                            reader.GetOrdinal("Type")),
                        (CaregiverStatus)reader.GetInt32(
                            reader.GetOrdinal("Status")),
                        (CaregiverAvailability)reader.GetInt32(
                            reader.GetOrdinal("Availability")),
                        GetNullableString(reader, "ArabicFullName"),
                        GetNullableString(reader, "EnglishFullName"),
                        GetNullableString(reader, "PhoneNumber"),
                        reader.GetDateTime(
                            reader.GetOrdinal("UpdatedOnUtc"))));
            }
        }
        finally
        {
            await Database.CloseConnectionAsync();
        }

        return items;
    }

    public async Task<int> CountAdminCaregiversAsync(
        int? status,
        int? type,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            """
            SELECT COUNT(*)
            FROM caregivers.caregivers c
            WHERE (@statusFilter IS NULL OR c.status = @statusFilter)
              AND (@typeFilter IS NULL OR c.type = @typeFilter)
            """;

        await Database.OpenConnectionAsync(cancellationToken);

        try
        {
            using DbCommand command =
                Database.GetDbConnection().CreateCommand();

            command.CommandText = sql;

            AddFilterParameters(command, status, type);

            object? result =
                await command.ExecuteScalarAsync(cancellationToken);

            return Convert.ToInt32(result);
        }
        finally
        {
            await Database.CloseConnectionAsync();
        }
    }

    private static void AddFilterParameters(
        DbCommand command,
        int? status,
        int? type)
    {
        command.Parameters.Add(
            CreateParameter(
                command,
                "statusFilter",
                status.HasValue ? status.Value : DBNull.Value));

        command.Parameters.Add(
            CreateParameter(
                command,
                "typeFilter",
                type.HasValue ? type.Value : DBNull.Value));
    }

    private static DbParameter CreateParameter(
        DbCommand command,
        string name,
        object value)
    {
        DbParameter parameter =
            command.CreateParameter();

        parameter.ParameterName = name;
        parameter.Value = value;

        return parameter;
    }

    private static string? GetNullableString(
        DbDataReader reader,
        string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);

        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetString(ordinal);
    }
}