using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;

namespace Sanad.API.Seeding;

public sealed record TestUserSeedOptions
{
    public const string SectionName = "App:TestUserSeed";

    public bool Enabled { get; init; }

    public string Password { get; init; } = "Test-1234!";
}

// Opt-in idempotent fixture for end-to-end testing (TEST environments ONLY).
// Mirrors SuperAdminSeeder. Never enable against a live Paymob key or real users.
//
// Seeds: family owner + viewer (password login), two elderly login users,
// the family aggregate with two elderly dependents, one Active+Available MEDICAL
// and one Active+Available COMPANION caregiver (built through the real domain
// readiness flow), and a booking portfolio: Completed, Confirmed, PendingPayment,
// CancelledByFamily, CancelledByCaregiver (refunded) and DeclinedByCaregiver (refunded).
public sealed class TestUserDataSeeder
{
    private readonly IdentityDbContext _identityDbContext;
    private readonly IFamiliesDbContext _familiesDbContext;
    private readonly CaregiversDbContext _caregiversDbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly TestUserSeedOptions _options;
    private readonly IConfiguration _configuration;

    public TestUserDataSeeder(
        IdentityDbContext identityDbContext,
        IFamiliesDbContext familiesDbContext,
        CaregiversDbContext caregiversDbContext,
        IPasswordHasher passwordHasher,
        IDateTimeProvider dateTimeProvider,
        IOptions<TestUserSeedOptions> options,
        IConfiguration configuration)
    {
        _identityDbContext = identityDbContext;
        _familiesDbContext = familiesDbContext;
        _caregiversDbContext = caregiversDbContext;
        _passwordHasher = passwordHasher;
        _dateTimeProvider = dateTimeProvider;
        _options = options.Value;
        _configuration = configuration;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        string? paymobSecretKey =
            _configuration["Paymob:SecretKey"];

        if (!string.IsNullOrWhiteSpace(paymobSecretKey)
            && paymobSecretKey.StartsWith("sk_live", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "App:TestUserSeed:Enabled must never be true against a LIVE Paymob key.");
        }

        DateTime utcNow = _dateTimeProvider.UtcNow;
        DateOnly today = DateOnly.FromDateTime(utcNow);

        // ---------------- Identity: accounts ----------------

        User owner = await EnsureUserAsync(
            arabicFullName: "مالك العيلة التجريبي",
            englishFullName: "Test Family Owner",
            email: "family.owner@test.sanad.local",
            phoneNumber: "+201000000001",
            AccountType.Family,
            utcNow,
            cancellationToken);

        User viewer = await EnsureUserAsync(
            arabicFullName: "فرد العيلة التجريبي",
            englishFullName: "Test Family Viewer",
            email: "family.viewer@test.sanad.local",
            phoneNumber: "+201000000002",
            AccountType.Family,
            utcNow,
            cancellationToken);

        User medicalCaregiverUser = await EnsureUserAsync(
            arabicFullName: "الكيرجيفر الطبي التجريبي",
            englishFullName: "Test Medical Caregiver",
            email: "medical.caregiver@test.sanad.local",
            phoneNumber: "+201000000003",
            AccountType.MedicalCaregiver,
            utcNow,
            cancellationToken);

        User companionCaregiverUser = await EnsureUserAsync(
            arabicFullName: "الكيرجيفر المرافق التجريبي",
            englishFullName: "Test Companion Caregiver",
            email: "companion.caregiver@test.sanad.local",
            phoneNumber: "+201000000004",
            AccountType.CompanionCaregiver,
            utcNow,
            cancellationToken);

        User grandfatherUser = await EnsureElderlyUserAsync(
            arabicFullName: "الجد التجريبي",
            englishFullName: "Test Grandfather",
            phoneNumber: "+201000000005",
            Gender.Male,
            new DateOnly(1950, 1, 15),
            utcNow,
            cancellationToken);

        User grandmotherUser = await EnsureElderlyUserAsync(
            arabicFullName: "الجدة التجريبية",
            englishFullName: "Test Grandmother",
            phoneNumber: "+201000000006",
            Gender.Female,
            new DateOnly(1952, 3, 20),
            utcNow,
            cancellationToken);

        // ---------------- Families: aggregate + dependents ----------------

        Family? family = await _familiesDbContext.Families
            .Include(f => f.Members)
            .FirstOrDefaultAsync(f => f.OwnerUserId == owner.Id, cancellationToken);

        if (family is null)
        {
            family = Family.Create(owner.Id, "Test Family");

            family.AddMember(
                FamilyMember.Create(
                    viewer.Id,
                    owner.Id,
                    FamilyRelationshipType.Son,
                    FamilyRole.Viewer));

            _familiesDbContext.Families.Add(family);

            // Elderly dependents are their own aggregate root (FamiliesDbContext.Elderlies).
            _familiesDbContext.Elderlies.Add(
                Elderly.Create(
                    owner.Id,
                    grandfatherUser.Id,
                    family.Id,
                    FamilyRelationshipType.Grandfather,
                    FullName.Create("الجد التجريبي"),
                    FullName.Create("Test Grandfather"),
                    Gender.Male,
                    new DateOnly(1950, 1, 15),
                    today));

            _familiesDbContext.Elderlies.Add(
                Elderly.Create(
                    owner.Id,
                    grandmotherUser.Id,
                    family.Id,
                    FamilyRelationshipType.Grandmother,
                    FullName.Create("الجدة التجريبية"),
                    FullName.Create("Test Grandmother"),
                    Gender.Female,
                    new DateOnly(1952, 3, 20),
                    today));

            await _familiesDbContext.SaveChangesAsync(cancellationToken);
        }

        // ---------------- Caregivers: lookups + two Active caregivers ----------------

        // Load-or-create: a crashed earlier run may have persisted the caregivers
        // but never reached the bookings section - never early-return past it.
        Caregiver? medical = await _caregiversDbContext.Caregivers
            .FirstOrDefaultAsync(c => c.UserId == medicalCaregiverUser.Id, cancellationToken);

        Caregiver? companion = await _caregiversDbContext.Caregivers
            .FirstOrDefaultAsync(c => c.UserId == companionCaregiverUser.Id, cancellationToken);

        if (medical is null || companion is null)
        {
            Governorate governorate = await EnsureLookupAsync(
            _caregiversDbContext,
            _caregiversDbContext.Governorates,
            g => g.EnglishName == "Alexandria",
            () => Governorate.Create("الإسكندرية", "Alexandria"),
            cancellationToken);

            City city = await EnsureLookupAsync(
                _caregiversDbContext,
                _caregiversDbContext.Cities,
                c => c.EnglishName == "Smouha" && c.GovernorateId == governorate.Id,
                () => City.Create(governorate.Id, "سموحة", "Smouha"),
                cancellationToken);

            Area area = await EnsureLookupAsync(
                _caregiversDbContext,
                _caregiversDbContext.Areas,
                a => a.EnglishName == "Smouha Area" && a.CityId == city.Id,
                () => Area.Create(city.Id, "سموحة", "Smouha Area"),
                cancellationToken);

            Language language = await EnsureLookupAsync(
                _caregiversDbContext,
                _caregiversDbContext.Languages,
                l => l.EnglishName == "Arabic",
                () => Language.Create("ar", "العربية", "Arabic"),
                cancellationToken);

            Service medicalService = await EnsureLookupAsync(
                _caregiversDbContext,
                _caregiversDbContext.Services,
                s => s.EnglishName == "Test Medical Care",
                () => Service.Create("رعاية طبية تجريبية", "Test Medical Care", "test-data/icons/medical.png", CaregiverType.Medical, true),
                cancellationToken);

            Service companionService = await EnsureLookupAsync(
                _caregiversDbContext,
                _caregiversDbContext.Services,
                s => s.EnglishName == "Test Companion Care",
                () => Service.Create("رعاية مرافقة تجريبية", "Test Companion Care", "test-data/icons/companion.png", CaregiverType.Companion, true),
                cancellationToken);

            Specialization medicalSpecialization = await EnsureLookupAsync(
                _caregiversDbContext,
                _caregiversDbContext.Specializations,
                s => s.EnglishName == "Test Geriatric Care",
                () => Specialization.Create("رعاية مسنين تجريبية", "Test Geriatric Care", true, CaregiverType.Medical),
                cancellationToken);

            Specialization companionSpecialization = await EnsureLookupAsync(
                _caregiversDbContext,
                _caregiversDbContext.Specializations,
                s => s.EnglishName == "Test Elderly Companionship",
                () => Specialization.Create("مرافقة مسنين تجريبية", "Test Elderly Companionship", true, CaregiverType.Companion),
                cancellationToken);

            ProfessionalTitle professionalTitle = await EnsureLookupAsync(
                _caregiversDbContext,
                _caregiversDbContext.ProfessionalTitles,
                t => t.EnglishName == "Test Nurse",
                () => ProfessionalTitle.Create("ممرض تجريبي", "Test Nurse", true),
                cancellationToken);

            AcademicDegree academicDegree = await EnsureLookupAsync(
                _caregiversDbContext,
                _caregiversDbContext.AcademicDegrees,
                d => d.EnglishName == "Test BSc Nursing",
                () => AcademicDegree.Create("بكالوريوس تمريض تجريبي", "Test BSc Nursing", true),
                cancellationToken);

            // ---------------- Medical caregiver (real readiness flow) ----------------

            medical = Caregiver.Create(
            medicalCaregiverUser.Id,
            CaregiverType.Medical);

            medical.SelectService(medicalService);
            medical.SelectLanguage(language);
            medical.SelectArea(area);

            medical.UpdateMedicalProfile(
                professionalTitle,
                yearsOfExperience: 8,
                medicalSpecialization,
                academicDegree,
                currentWorkplace: "Test Clinic",
                biography: "Seeded test medical caregiver.",
                utcNow);

            medical.UpdateMedicalPricing(
                homeVisitPrice: 500m,
                eightHourShiftPrice: 900m,
                twelveHourShiftPrice: 1300m,
                twentyFourHourShiftPrice: 1800m);

            medical.AddMedicalHomeVisitWindow(DayOfWeek.Saturday, new TimeOnly(8, 0), new TimeOnly(22, 0));
            medical.AddMedicalHomeVisitWindow(DayOfWeek.Wednesday, new TimeOnly(8, 0), new TimeOnly(22, 0));
            // A day cannot combine a shift with Home Visit windows (MedicalWeeklySchedule rule) —
            // shift lives on Monday; windows on Saturday + Wednesday.
            medical.AddMedicalShift(DayOfWeek.Monday, MedicalShiftType.EightHourMorning);

            medical.AddCertificate(
                CaregiverCertificateType.PracticeLicense,
                "test-data/certificates/practice-license.pdf",
                expiryDate: today.AddYears(1),
                today);

            medical.AddCertificate(
                CaregiverCertificateType.GraduationCertificate,
                "test-data/certificates/graduation.pdf",
                expiryDate: null,
                today);

            medical.SubmitForReview(utcNow, today);

            foreach (CaregiverCertificate certificate in medical.Certificates)
            {
                medical.VerifyCertificate(certificate.Id);
            }

            medical.Approve(utcNow, today);
            medical.BecomeAvailable(today);

            _caregiversDbContext.Caregivers.Add(medical);
            await _caregiversDbContext.SaveChangesAsync(cancellationToken);

            // ---------------- Companion caregiver (real readiness flow) ----------------

            companion = Caregiver.Create(
            companionCaregiverUser.Id,
            CaregiverType.Companion);

            companion.SelectService(companionService);
            companion.SelectLanguage(language);
            companion.SelectArea(area);

            companion.UpdateCompanionProfile(
                yearsOfExperience: 5,
                companionSpecialization,
                biography: "Seeded test companion caregiver.",
                utcNow);

            companion.UpdateCompanionPricing(
                hourlyPrice: 80m,
                eightHourDayPrice: 500m,
                overnightPrice: 700m);

            companion.AddCompanionAvailabilityWindow(CompanionBookingType.Hourly, DayOfWeek.Saturday, new TimeOnly(9, 0), new TimeOnly(18, 0));
            companion.AddCompanionAvailabilityWindow(CompanionBookingType.Hourly, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(18, 0));

            companion.SubmitForReview(utcNow, today);
            companion.Approve(utcNow, today);
            companion.BecomeAvailable(today);

            _caregiversDbContext.Caregivers.Add(companion);
            await _caregiversDbContext.SaveChangesAsync(cancellationToken);
        }

        // ---------------- Bookings portfolio (no slot conflicts: distinct dates per caregiver) ----------------

        bool bookingsSeeded = await _familiesDbContext.Bookings
            .AnyAsync(b => b.FamilyId == family.Id, cancellationToken);

        if (bookingsSeeded)
        {
            return;
        }

        Elderly grandfather = await _familiesDbContext.Elderlies
            .FirstAsync(e => e.FamilyId == family.Id, cancellationToken);

        decimal medicalHomeVisitFee = medical.MedicalPricing!.HomeVisitPrice;
        decimal companionHourlyFee = companion.CompanionPricing!.HourlyPrice;

        // 1. Completed (the full successful lifecycle)
        Booking completed = NewBooking(family, owner, grandfather, medical, BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit, today.AddDays(1), new TimeOnly(10, 0), new TimeOnly(12, 0),
            utcNow, medicalHomeVisitFee);
        PayAndAccept(completed, utcNow);
        completed.StartVisit(utcNow.AddMinutes(2));
        completed.CompleteVisit("Seeded completed visit.", utcNow.AddMinutes(3));

        // 2. Confirmed (paid + accepted — the "successful active" booking)
        Booking confirmed = NewBooking(family, owner, grandfather, medical, BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit, today.AddDays(3), new TimeOnly(14, 0), new TimeOnly(16, 0),
            utcNow, medicalHomeVisitFee);
        PayAndAccept(confirmed, utcNow);

        // 3. PendingPayment (ready for the Postman payment-intent test)
        Booking pending = NewBooking(family, owner, grandfather, medical, BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit, today.AddDays(4), new TimeOnly(14, 0), new TimeOnly(16, 0),
            utcNow, medicalHomeVisitFee);

        // 4. CancelledByFamily (never paid)
        Booking cancelledByFamily = NewBooking(family, owner, grandfather, medical, BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit, today.AddDays(5), new TimeOnly(14, 0), new TimeOnly(16, 0),
            utcNow, medicalHomeVisitFee);
        cancelledByFamily.CancelByFamily("Seeded family cancellation (unpaid).", utcNow);

        // 5. CancelledByCaregiver after payment → refunded (feeds the admin cancellation summary)
        Booking cancelledByCaregiver = NewBooking(family, owner, grandfather, companion, BookingCaregiverType.Companion,
            BookingShiftType.Hourly, today.AddDays(3), new TimeOnly(10, 0), new TimeOnly(12, 0),
            utcNow, companionHourlyFee);
        PayAndAccept(cancelledByCaregiver, utcNow);
        cancelledByCaregiver.CancelByCaregiver("Seeded caregiver cancellation.", utcNow.AddMinutes(4));
        cancelledByCaregiver.MarkRefunded($"test-refund-{cancelledByCaregiver.Id.Value:N}", utcNow.AddMinutes(5));

        // 6. DeclinedByCaregiver after payment → refunded.
        // DeclineByCaregiver is only valid from PendingCaregiverApproval, so this
        // booking is paid but NOT accepted (the decline replaces the acceptance).
        Booking declined = NewBooking(family, owner, grandfather, medical, BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit, today.AddDays(6), new TimeOnly(14, 0), new TimeOnly(16, 0),
            utcNow, medicalHomeVisitFee);
        declined.RecordPaymentIntent(
            declined.Id.Value.ToString(),
            PaymentMethod.Card,
            utcNow);
        declined.MarkAsPaid(
            declined.Id.Value.ToString(),
            $"test-txn-{declined.Id.Value:N}",
            utcNow.AddMinutes(1));
        declined.DeclineByCaregiver("Seeded caregiver decline.", utcNow.AddMinutes(4));
        declined.MarkRefunded($"test-refund-{declined.Id.Value:N}", utcNow.AddMinutes(5));

        _familiesDbContext.Bookings.AddRange(
            completed, confirmed, pending, cancelledByFamily, cancelledByCaregiver, declined);
        await _familiesDbContext.SaveChangesAsync(cancellationToken);
    }

    private static Booking NewBooking(
        Family family,
        User owner,
        Elderly elderly,
        Caregiver caregiver,
        BookingCaregiverType caregiverType,
        BookingShiftType shiftType,
        DateOnly bookingDate,
        TimeOnly startTime,
        TimeOnly endTime,
        DateTime utcNow,
        decimal baseFee)
    {
        return Booking.Create(
            family.Id,
            owner.Id,
            elderly.Id,
            caregiver.Id,
            caregiverType,
            shiftType,
            bookingDate,
            startTime,
            endTime,
            "14 شارع الكورنيش، الإسكندرية (بيانات اختبار)",
            specialInstructions: null,
            BookingPriceSnapshot.Calculate(baseFee, 15m),
            acceptanceDeadlineUtc: utcNow.AddHours(24),
            currentDate: DateOnly.FromDateTime(utcNow),
            createdOnUtc: utcNow);
    }

    private static void PayAndAccept(Booking booking, DateTime utcNow)
    {
        booking.RecordPaymentIntent(
            booking.Id.Value.ToString(),
            PaymentMethod.Card,
            utcNow);
        booking.MarkAsPaid(
            booking.Id.Value.ToString(),
            $"test-txn-{booking.Id.Value:N}",
            utcNow.AddMinutes(1));
        booking.AcceptByCaregiver(utcNow.AddMinutes(2));
    }

    private async Task<User> EnsureUserAsync(
        string arabicFullName,
        string englishFullName,
        string email,
        string phoneNumber,
        AccountType accountType,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        string normalizedEmail = Email.Create(email).Value;

        List<User> users =
            await _identityDbContext.Users.ToListAsync(cancellationToken);

        User? user = users.FirstOrDefault(
            u => u.Email != null && u.Email.Value == normalizedEmail);

        if (user is not null)
        {
            return user;
        }

        user = User.Create(
            FullName.Create(arabicFullName),
            FullName.Create(englishFullName),
            Email.Create(email),
            PhoneNumber.Create(phoneNumber));

        user.AddAccount(accountType);
        user.SetInitialPasswordHash(
            _passwordHasher.Hash(_options.Password),
            utcNow);
        user.VerifyEmail(utcNow);
        user.VerifyPhone(utcNow);
        user.Activate(utcNow);

        _identityDbContext.Users.Add(user);
        await _identityDbContext.SaveChangesAsync(cancellationToken);

        return user;
    }

    private async Task<User> EnsureElderlyUserAsync(
        string arabicFullName,
        string englishFullName,
        string phoneNumber,
        Gender gender,
        DateOnly dateOfBirth,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        string normalizedPhoneNumber = PhoneNumber.Create(phoneNumber).Value;

        List<User> users =
            await _identityDbContext.Users.ToListAsync(cancellationToken);

        User? user = users.FirstOrDefault(
            u => u.PhoneNumber.Value == normalizedPhoneNumber);

        if (user is not null)
        {
            return user;
        }

        // CreateElderly already sets UserStatus.Active, PhoneVerified = true,
        // DateOfBirth, Gender and the Elderly account — nothing else needed.
        user = User.CreateElderly(
            FullName.Create(arabicFullName),
            FullName.Create(englishFullName),
            PhoneNumber.Create(phoneNumber),
            gender,
            dateOfBirth,
            utcNow);

        _identityDbContext.Users.Add(user);
        await _identityDbContext.SaveChangesAsync(cancellationToken);

        return user;
    }

    private async Task<T> EnsureLookupAsync<T>(
        CaregiversDbContext dbContext,
        DbSet<T> set,
        System.Linq.Expressions.Expression<Func<T, bool>> predicate,
        Func<T> factory,
        CancellationToken cancellationToken)
        where T : class
    {
        T? existing = await set.FirstOrDefaultAsync(predicate, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        T created = factory();
        set.Add(created);
        await dbContext.SaveChangesAsync(cancellationToken);

        return created;
    }
}
