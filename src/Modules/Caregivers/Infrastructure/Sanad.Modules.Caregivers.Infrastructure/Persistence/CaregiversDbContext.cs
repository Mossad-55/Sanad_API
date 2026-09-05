using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.Discovery;
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
    public async Task<CaregiverUserHeader?> GetCaregiverUserHeaderAsync(
    UserId userId,
    CancellationToken cancellationToken = default)
    {
        const string sql =
            """
            SELECT
                id                  AS "UserId",
                arabic_full_name    AS "ArabicFullName",
                english_full_name   AS "EnglishFullName",
                gender              AS "Gender",
                avatar_url          AS "AvatarUrl"
            FROM identity.users
            WHERE id = @userId
            LIMIT 1
            """;

        await Database.OpenConnectionAsync(cancellationToken);

        try
        {
            using DbCommand command = Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(CreateParameter(command, "userId", userId.Value));

            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            if (await reader.ReadAsync(cancellationToken))
            {
                int genderOrdinal = reader.GetOrdinal("Gender");
                Gender? gender = reader.IsDBNull(genderOrdinal)
                    ? null
                    : (Gender)reader.GetInt32(genderOrdinal);

                return new CaregiverUserHeader(
                    reader.GetGuid(reader.GetOrdinal("UserId")),
                    reader.GetString(reader.GetOrdinal("ArabicFullName")),
                    reader.GetString(reader.GetOrdinal("EnglishFullName")),
                    gender,
                    GetNullableString(reader, "AvatarUrl"));
            }

            return null;
        }
        finally
        {
            await Database.CloseConnectionAsync();
        }
    }

    public async Task<(IReadOnlyList<CaregiverSearchCardResponse> Items, int TotalCount)> SearchActiveCaregiversAsync(
        string? search,
        int? type,
        int? gender,
        Guid? areaId,
        Guid? specializationId,
        int? availability,
        decimal? minPrice,
        decimal? maxPrice,
        decimal? minRating,
        int? minExperienceYears,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        const string baseSql =
            """
            FROM caregivers.caregivers c
            INNER JOIN identity.users u ON u.id = c.user_id
            LEFT JOIN caregivers.specializations s 
                ON s.id = COALESCE(c.medical_specialization_id, c.companion_specialization_id)
            LEFT JOIN caregivers.professional_titles pt 
                ON pt.id = c.medical_professional_title_id
            WHERE c.status = 3 -- Active only
              AND (@search IS NULL OR u.arabic_full_name ILIKE '%' || @search || '%' OR u.english_full_name ILIKE '%' || @search || '%')
              AND (@typeFilter IS NULL OR c.type = @typeFilter)
              AND (@genderFilter IS NULL OR u.gender = @genderFilter)
              AND (@availabilityFilter IS NULL OR c.availability = @availabilityFilter)
              AND (@minRating IS NULL OR c.average_rating >= @minRating)
              AND (@minExp IS NULL OR COALESCE(c.medical_years_of_experience, c.companion_years_of_experience, 0) >= @minExp)
              AND (@minPrice IS NULL OR COALESCE(c.companion_hourly_price, c.medical_eight_hour_shift_price, 0) >= @minPrice)
              AND (@maxPrice IS NULL OR COALESCE(c.companion_hourly_price, c.medical_eight_hour_shift_price, 0) <= @maxPrice)
              AND (@areaFilter IS NULL OR EXISTS (
                    SELECT 1 FROM caregivers.caregiver_area_selections cas 
                    WHERE cas.caregiver_id = c.id AND cas.area_id = @areaFilter))
              AND (@specFilter IS NULL OR COALESCE(c.medical_specialization_id, c.companion_specialization_id) = @specFilter)
            """;

        string countSql = $"SELECT COUNT(*) {baseSql}";
        string selectSql =
            $"""
            SELECT
                c.id                 AS "CaregiverId",
                c.user_id            AS "UserId",
                u.arabic_full_name   AS "ArabicFullName",
                u.english_full_name  AS "EnglishFullName",
                u.gender             AS "Gender",
                u.avatar_url         AS "AvatarUrl",
                c.type               AS "Type",
                pt.arabic_name       AS "ProfessionalTitleAr",
                pt.english_name      AS "ProfessionalTitleEn",
                COALESCE(c.medical_years_of_experience, c.companion_years_of_experience, 0) AS "ExperienceYears",
                COALESCE(c.companion_hourly_price, c.medical_eight_hour_shift_price, 0)      AS "StartingPrice",
                c.average_rating     AS "AverageRating",
                c.reviews_count      AS "ReviewsCount",
                c.availability       AS "Availability",
                s.arabic_name        AS "SpecializationAr",
                s.english_name       AS "SpecializationEn"
            {baseSql}
            ORDER BY c.average_rating DESC, c.reviews_count DESC, c.id
            LIMIT @take OFFSET @skip
            """;

        List<CaregiverSearchCardResponse> items = [];
        int totalCount = 0;

        await Database.OpenConnectionAsync(cancellationToken);

        try
        {
            // 1. Get Count
            using (DbCommand countCommand = Database.GetDbConnection().CreateCommand())
            {
                countCommand.CommandText = countSql;
                AddSearchParameters(countCommand, search, type, gender, areaId, specializationId, availability, minPrice, maxPrice, minRating, minExperienceYears);
                object? countResult = await countCommand.ExecuteScalarAsync(cancellationToken);
                totalCount = Convert.ToInt32(countResult);
            }

            // 2. Get Paged Items
            using (DbCommand selectCommand = Database.GetDbConnection().CreateCommand())
            {
                selectCommand.CommandText = selectSql;
                AddSearchParameters(selectCommand, search, type, gender, areaId, specializationId, availability, minPrice, maxPrice, minRating, minExperienceYears);
                selectCommand.Parameters.Add(CreateParameter(selectCommand, "take", pageSize));
                selectCommand.Parameters.Add(CreateParameter(selectCommand, "skip", (page - 1) * pageSize));

                await using DbDataReader reader = await selectCommand.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    int genderOrdinal = reader.GetOrdinal("Gender");
                    Gender? genderVal = reader.IsDBNull(genderOrdinal)
                        ? null
                        : (Gender)reader.GetInt32(genderOrdinal);

                    var cType = (CaregiverType)reader.GetInt32(reader.GetOrdinal("Type"));
                    string? specAr = GetNullableString(reader, "SpecializationAr");
                    string? specEn = GetNullableString(reader, "SpecializationEn");

                    items.Add(new CaregiverSearchCardResponse(
                        reader.GetGuid(reader.GetOrdinal("CaregiverId")),
                        reader.GetGuid(reader.GetOrdinal("UserId")),
                        reader.GetString(reader.GetOrdinal("ArabicFullName")),
                        reader.GetString(reader.GetOrdinal("EnglishFullName")),
                        genderVal,
                        GetNullableString(reader, "AvatarUrl"),
                        cType,
                        GetNullableString(reader, "ProfessionalTitleAr"),
                        GetNullableString(reader, "ProfessionalTitleEn"),
                        reader.GetInt32(reader.GetOrdinal("ExperienceYears")),
                        reader.GetDecimal(reader.GetOrdinal("StartingPrice")),
                        reader.GetDecimal(reader.GetOrdinal("AverageRating")),
                        reader.GetInt32(reader.GetOrdinal("ReviewsCount")),
                        (CaregiverAvailability)reader.GetInt32(reader.GetOrdinal("Availability")),
                        specAr != null ? new[] { specAr } : Array.Empty<string>(),
                        specEn != null ? new[] { specEn } : Array.Empty<string>(),
                        Array.Empty<string>()));
                }
            }
        }
        finally
        {
            await Database.CloseConnectionAsync();
        }

        return (items, totalCount);
    }

    private static void AddSearchParameters(
        DbCommand command,
        string? search,
        int? type,
        int? gender,
        Guid? areaId,
        Guid? specializationId,
        int? availability,
        decimal? minPrice,
        decimal? maxPrice,
        decimal? minRating,
        int? minExperienceYears)
    {
        command.Parameters.Add(CreateParameter(command, "search", (object?)search ?? DBNull.Value));
        command.Parameters.Add(CreateParameter(command, "typeFilter", (object?)type ?? DBNull.Value));
        command.Parameters.Add(CreateParameter(command, "genderFilter", (object?)gender ?? DBNull.Value));
        command.Parameters.Add(CreateParameter(command, "areaFilter", (object?)areaId ?? DBNull.Value));
        command.Parameters.Add(CreateParameter(command, "specFilter", (object?)specializationId ?? DBNull.Value));
        command.Parameters.Add(CreateParameter(command, "availabilityFilter", (object?)availability ?? DBNull.Value));
        command.Parameters.Add(CreateParameter(command, "minPrice", (object?)minPrice ?? DBNull.Value));
        command.Parameters.Add(CreateParameter(command, "maxPrice", (object?)maxPrice ?? DBNull.Value));
        command.Parameters.Add(CreateParameter(command, "minRating", (object?)minRating ?? DBNull.Value));
        command.Parameters.Add(CreateParameter(command, "minExp", (object?)minExperienceYears ?? DBNull.Value));
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