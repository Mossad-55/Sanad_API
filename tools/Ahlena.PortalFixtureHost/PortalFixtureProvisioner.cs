using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ahlena.API.Seeding;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.CareHomes.Domain.Bookings;
using Ahlena.Modules.CareHomes.Domain.Facilities;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;
using Ahlena.Modules.Caregivers.Domain.Caregivers;
using Ahlena.Modules.Caregivers.Domain.Caregivers.Lookups;
using Ahlena.Modules.Caregivers.Infrastructure.Persistence;
using Ahlena.Modules.Families.Application.Bookings;
using Ahlena.Modules.Families.Application.Subscriptions;
using Ahlena.Modules.Families.Domain.Bookings;
using Ahlena.Modules.Families.Domain.Elderlies;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Domain.Subscriptions;
using Ahlena.Modules.Families.Infrastructure.Persistence;
using Ahlena.Modules.Finance.Application;
using Ahlena.Modules.Finance.Domain;
using Ahlena.Modules.Finance.Infrastructure;
using Ahlena.Modules.Identity.Application.Abstractions.Security;
using Ahlena.Modules.Identity.Domain.Users;
using Ahlena.Modules.Identity.Infrastructure.Persistence;

namespace Ahlena.PortalFixtureHost;

/// <summary>
/// Stable ids of the W5 bounded fixture groups. They double as the
/// <c>--provision-groups</c> values, the manifest keys and the guide's scenario
/// map, so the wrapper, the tests and the documentation cannot drift apart.
/// </summary>
public static class PortalFixtureGroups
{
    public const string CaregiverRefundAmbiguity = "caregiver-refund-ambiguity";
    public const string CaregiverRefundFailure = "caregiver-refund-failure";
    public const string CaregiverCompletedBooking = "caregiver-completed-booking";
    public const string CareHomePaidAwaitingDecision = "carehome-paid-awaiting-decision";
    public const string CareHomeRefundAmbiguity = "carehome-refund-ambiguity";
    public const string CareHomeRefundFailure = "carehome-refund-failure";
    public const string CareHomeEvidencedExternalRefund = "carehome-evidenced-external-refund";
    public const string CareHomeBookingExtension = "carehome-booking-extension";
    public const string CareHomeCheckoutDispute = "carehome-checkout-dispute";
    public const string SubscriptionAndDuplicateClaim = "subscription-and-duplicate-claim";
    public const string ComplaintReceived = "complaint-received";
    public const string ComplaintInvestigating = "complaint-investigating";

    public static readonly IReadOnlyList<string> All =
    [
        CaregiverRefundAmbiguity,
        CaregiverRefundFailure,
        CaregiverCompletedBooking,
        CareHomePaidAwaitingDecision,
        CareHomeRefundAmbiguity,
        CareHomeRefundFailure,
        CareHomeEvidencedExternalRefund,
        CareHomeBookingExtension,
        CareHomeCheckoutDispute,
        SubscriptionAndDuplicateClaim,
        ComplaintReceived,
        ComplaintInvestigating
    ];
}

public sealed record PortalFixtureProvisionOptions(
    string RunTag,
    IReadOnlyList<string> Groups,
    DateTime InstantUtc,
    string SyntheticPassword,
    string WorkRoot);

/// <summary>One fixture-owned record captured in the run manifest.</summary>
public sealed record PortalFixtureRecord(
    string Kind,
    string Id,
    string State);

/// <summary>One requested group's outcome. Credential-free by construction.</summary>
public sealed record PortalFixtureGroupResult(
    string Group,
    bool Provisioned,
    string Detail,
    IReadOnlyList<PortalFixtureRecord> Records);

public sealed record PortalFixtureProvisionReport(
    bool Passed,
    string ManifestPath,
    string RunTag,
    DateTime InstantUtc,
    string FamilyLoginEmail,
    IReadOnlyList<PortalFixtureGroupResult> Groups,
    IReadOnlyList<string> UnresolvedGroups);

/// <summary>
/// Bounded, fixture-owned domain provisioner. It writes only records it can
/// positively re-read afterwards, tags every narrative field with the run tag
/// so ownership is machine-checkable, and reuses whatever a previous run left
/// untouched instead of duplicating it. It never applies a migration, never
/// resets a password on an existing identity and never contacts a provider.
/// <para>
/// Group states that need a provider movement are built at the domain level
/// (claim/ambiguity/refusal markers) — no gateway call is issued at all — and
/// every such record is labelled synthetic. A group that cannot be provisioned
/// from the confirmed domain surface is reported unresolved rather than faked.
/// </para>
/// </summary>
public static class PortalFixtureProvisioner
{
    public static async Task<PortalFixtureProvisionReport> ProvisionAsync(
        IServiceProvider services,
        PortalFixtureProvisionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var run = new FixtureRun(services, options, cancellationToken);
        return await run.ExecuteAsync();
    }

    private sealed class FixtureRun(
        IServiceProvider services,
        PortalFixtureProvisionOptions options,
        CancellationToken cancellationToken)
    {
        private const string MarkerPrefix = "sanad-portal-fixture";
        private const string Currency = "EGP";

        private readonly IdentityDbContext _identity = services.GetRequiredService<IdentityDbContext>();
        private readonly FamiliesDbContext _families = services.GetRequiredService<FamiliesDbContext>();
        private readonly CaregiversDbContext _caregivers = services.GetRequiredService<CaregiversDbContext>();
        private readonly CareHomesDbContext _careHomes = services.GetRequiredService<CareHomesDbContext>();
        private readonly FinanceDbContext _finance = services.GetRequiredService<FinanceDbContext>();
        private readonly IPasswordHasher _hasher = services.GetRequiredService<IPasswordHasher>();
        private readonly ISender _sender = services.GetRequiredService<ISender>();
        private readonly TestPlatformChargeRuleSeeder _chargeRules =
            services.GetRequiredService<TestPlatformChargeRuleSeeder>();

        private readonly DateTime _now = EnsureUtc(options.InstantUtc);
        private readonly DateOnly _today = DateOnly.FromDateTime(EnsureUtc(options.InstantUtc));
        private readonly string _runTag = SanitizeTag(options.RunTag);
        private readonly string _baseMarker = $"{MarkerPrefix}:{SanitizeTag(options.RunTag)}";

        private User _owner = null!;
        private User _caregiverUser = null!;
        private User _careHomeOwnerUser = null!;
        private User _supportUser = null!;
        private User _superAdminUser = null!;
        private Family _family = null!;
        private Elderly _elderly = null!;
        private Caregiver _caregiver = null!;
        private CareHomeFacility _facility = null!;
        private CareHomeRoomType _roomType = null!;
        private CareHomeRoom _room = null!;
        private CareHomeBed _bed = null!;
        private SubscriptionPlanVersion _premiumPlan = null!;
        private FamilySubscription _subscription = null!;
        private SubscriptionPaymentAttempt _attempt = null!;

        // The subscription-shutdown refund runs on its OWN run-owned subscription
        // and capture ("slot") so it never competes with the duplicate-charge
        // claim for one capture's refund budget (shared-capture competition is
        // exactly what the Finance budget guard refuses).
        private User _shutdownOwner = null!;
        private FamilySubscription _shutdownSubscription = null!;
        private SubscriptionPaymentAttempt _shutdownAttempt = null!;
        private bool _shutdownSlotReady;

        /// <summary>The persisted outcome of the fixture's service-shutdown refund.</summary>
        private sealed record ShutdownRefund(
            Guid RefundCaseId,
            long AwardMinorUnits,
            SubscriptionRefundReason Reason,
            string EntryType);

        public async Task<PortalFixtureProvisionReport> ExecuteAsync()
        {
            IReadOnlyList<string> requested =
                options.Groups.Count == 0 ? PortalFixtureGroups.All : options.Groups;

            var results = new List<PortalFixtureGroupResult>();
            var unresolved = new List<string>();

            // The base fixture is shared by every group. A failure here is
            // fail-closed for the whole run: nothing is served.
            try
            {
                await EnsureBaseFixtureAsync();
            }
            catch (Exception exception)
            {
                foreach (string group in requested)
                {
                    unresolved.Add(group);
                    results.Add(new PortalFixtureGroupResult(
                        group,
                        Provisioned: false,
                        Detail: $"base fixture unavailable: {Safe(exception)}",
                        Records: []));
                }

                return WriteReport(results, unresolved);
            }

            foreach (string group in requested)
            {
                PortalFixtureGroupResult result;
                try
                {
                    result = group switch
                    {
                        PortalFixtureGroups.CaregiverRefundAmbiguity => await CaregiverRefundAmbiguityAsync(),
                        PortalFixtureGroups.CaregiverRefundFailure => await CaregiverRefundFailureAsync(),
                        PortalFixtureGroups.CaregiverCompletedBooking => await CaregiverCompletedBookingAsync(),
                        PortalFixtureGroups.CareHomePaidAwaitingDecision => await CareHomePaidAwaitingDecisionAsync(),
                        PortalFixtureGroups.CareHomeRefundAmbiguity => await CareHomeRefundAmbiguityAsync(),
                        PortalFixtureGroups.CareHomeRefundFailure => await CareHomeRefundFailureAsync(),
                        PortalFixtureGroups.CareHomeEvidencedExternalRefund => await CareHomeEvidencedExternalRefundAsync(),
                        PortalFixtureGroups.CareHomeBookingExtension => await CareHomeBookingExtensionAsync(),
                        PortalFixtureGroups.CareHomeCheckoutDispute => await CareHomeCheckoutDisputeAsync(),
                        PortalFixtureGroups.SubscriptionAndDuplicateClaim => await SubscriptionAndDuplicateClaimAsync(),
                        PortalFixtureGroups.ComplaintReceived => await ComplaintAsync(investigate: false),
                        PortalFixtureGroups.ComplaintInvestigating => await ComplaintAsync(investigate: true),
                        _ => new PortalFixtureGroupResult(
                            group,
                            Provisioned: false,
                            Detail: "unknown group id",
                            Records: [])
                    };
                }
                catch (Exception exception)
                {
                    result = new PortalFixtureGroupResult(
                        group,
                        Provisioned: false,
                        Detail: Safe(exception),
                        Records: []);
                }

                results.Add(result);
                if (!result.Provisioned)
                {
                    unresolved.Add(group);
                }
            }

            return WriteReport(results, unresolved);
        }

        // ------------------------------------------------------------------
        // Base fixture
        // ------------------------------------------------------------------

        private async Task EnsureBaseFixtureAsync()
        {
            _owner = await EnsureUserAsync(
                "family.owner", "مالك العيلة التجريبي", "W5 Fixture Family Owner", AccountType.Family);
            _caregiverUser = await EnsureUserAsync(
                "caregiver", "الكيرجيفر الطبي التجريبي", "W5 Fixture Medical Caregiver",
                AccountType.MedicalCaregiver);
            _careHomeOwnerUser = await EnsureUserAsync(
                "carehome.owner", "صاحب دار الرعاية التجريبي", "W5 Fixture Care Home Owner",
                AccountType.CareHomeOwner);
            _supportUser = await EnsureUserAsync(
                "support", "دعم العيلة التجريبي", "W5 Fixture Support Admin", AccountType.SupportAdmin);
            _superAdminUser = await EnsureUserAsync(
                "superadmin", "المشرف العام التجريبي", "W5 Fixture Super Admin", AccountType.SuperAdmin);
            User elderlyUser = await EnsureElderlyUserAsync();

            await EnsureFamilyAsync(elderlyUser);
            await EnsureCaregiverAsync();
            await EnsureFacilityAsync();
            await EnsureSubscriptionAsync();
            await _chargeRules.SeedAsync(_now, cancellationToken);
        }

        /// <summary>
        /// Load-or-create a fixture identity for one run-owned role. The email is
        /// deterministic per (run tag, role); the phone is derived from the same
        /// pair, never a shared constant, so two runs and two roles cannot
        /// collide. A pre-existing row for this email is reused ONLY after its
        /// stored ownership marker and account type are verified: a collision
        /// with an unrelated identity is refused outright — the fixture never
        /// adopts a foreign user, resets its password or changes its roles.
        /// Reuse never touches password or account state, because a manually
        /// progressed identity must survive a rerun.
        /// </summary>
        private async Task<User> EnsureUserAsync(
            string role, string arabicName, string englishName, AccountType accountType)
        {
            string email = FixtureEmail(role);
            string marker = OwnershipMarker(role);
            Email emailValue = Email.Create(email);

            User? existing = await _identity.Users
                .FirstOrDefaultAsync(user => user.Email == emailValue, cancellationToken);

            if (existing is not null)
            {
                RequireFixtureOwnership(existing, marker, accountType, email);
                return existing;
            }

            User user = User.Create(
                FullName.Create(arabicName),
                FullName.Create(TaggedName(englishName, marker)),
                Email.Create(email),
                PhoneNumber.Create(DeterministicPhone(role)));
            user.AddAccount(accountType);
            user.SetInitialPasswordHash(_hasher.Hash(options.SyntheticPassword), _now);
            user.VerifyEmail(_now);
            user.VerifyPhone(_now);
            user.Activate(_now);

            _identity.Users.Add(user);
            await _identity.SaveChangesAsync(cancellationToken);
            return user;
        }

        /// <summary>
        /// Load-or-create the elderly login identity. It has no email, so the
        /// deterministic per-run phone is the ownership key; a pre-existing row
        /// for that phone is reused only after its marker and Elderly account are
        /// verified, and a collision is refused rather than adopted.
        /// </summary>
        private async Task<User> EnsureElderlyUserAsync()
        {
            const string role = "elderly";
            string marker = OwnershipMarker(role);
            PhoneNumber phoneValue = PhoneNumber.Create(DeterministicPhone(role));

            User? existing = await _identity.Users
                .FirstOrDefaultAsync(user => user.PhoneNumber == phoneValue, cancellationToken);

            if (existing is not null)
            {
                RequireFixtureOwnership(existing, marker, AccountType.Elderly, phoneValue.Value);
                return existing;
            }

            User elderly = User.CreateElderly(
                FullName.Create("الجد التجريبي"),
                FullName.Create(TaggedName("W5 Fixture Grandfather", marker)),
                PhoneNumber.Create(DeterministicPhone(role)),
                Gender.Male,
                new DateOnly(1950, 1, 15),
                _now);
            elderly.SetInitialPasswordHash(_hasher.Hash(options.SyntheticPassword), _now);

            _identity.Users.Add(elderly);
            await _identity.SaveChangesAsync(cancellationToken);
            return elderly;
        }

        private string FixtureEmail(string role) => $"{_runTag}.{role}@test.sanad.local";

        private string OwnershipMarker(string role) => $"[{_runTag}:{role}]";

        private static string TaggedName(string englishName, string marker) => $"{englishName} {marker}";

        private static void RequireFixtureOwnership(
            User user, string marker, AccountType accountType, string identifier)
        {
            string englishName = user.EnglishFullName?.Value ?? string.Empty;
            bool tagged = englishName.Contains(marker, StringComparison.Ordinal);
            bool hasAccount = user.Accounts.Any(account => account.AccountType == accountType);

            if (!tagged || !hasAccount)
            {
                throw new InvalidOperationException(
                    $"fixture identity collision: '{identifier}' already exists but is not this run's fixture identity " +
                    $"(ownership marker present={tagged}, account type {accountType} present={hasAccount}); " +
                    "refusing to adopt, reset or re-role an unrelated identity.");
            }
        }

        /// <summary>
        /// A deterministic per-(run tag, role) E.164 phone. The 7-digit base is
        /// drawn from the tag only and is offset away from the all-zero range the
        /// production test seed uses; the 1-digit role ordinal makes two roles of
        /// the same run impossible to collide.
        /// </summary>
        private string DeterministicPhone(string role)
        {
            ulong hash = StableHash($"{_runTag}:{role}");
            long baseValue = 1_000_000 + (long)(hash % 9_000_000UL);
            int ordinal = RoleOrdinal(role);
            long digits = (baseValue * 10) + ordinal;
            return "+2010" + digits.ToString("D8", CultureInfo.InvariantCulture);
        }

        private static int RoleOrdinal(string role) => role switch
        {
            "family.owner" => 1,
            "caregiver" => 2,
            "carehome.owner" => 3,
            "support" => 4,
            "superadmin" => 5,
            "elderly" => 6,
            "shutdown.owner" => 7,
            _ => 8
        };

        private async Task EnsureFamilyAsync(User elderlyUser)
        {
            Family? family = await _families.Families
                .FirstOrDefaultAsync(item => item.OwnerUserId == _owner.Id, cancellationToken);

            if (family is null)
            {
                family = Family.Create(_owner.Id, $"W5 Fixture Family {_runTag}");
                _families.Families.Add(family);
                await _families.SaveChangesAsync(cancellationToken);
            }

            _family = family;

            Elderly? elderly = await _families.Elderlies.FirstOrDefaultAsync(
                item => item.FamilyId == family.Id && item.IdentityUserId == elderlyUser.Id, cancellationToken);

            if (elderly is null)
            {
                elderly = Elderly.Create(
                    _owner.Id,
                    elderlyUser.Id,
                    family.Id,
                    FamilyRelationshipType.Grandfather,
                    FullName.Create("الجد التجريبي"),
                    FullName.Create("W5 Fixture Grandfather"),
                    Gender.Male,
                    new DateOnly(1950, 1, 15),
                    _today);
                _families.Elderlies.Add(elderly);
                await _families.SaveChangesAsync(cancellationToken);
            }

            _elderly = elderly;
        }

        private async Task EnsureCaregiverAsync()
        {
            Caregiver? caregiver = await _caregivers.Caregivers
                .AsSplitQuery()
                .FirstOrDefaultAsync(item => item.UserId == _caregiverUser.Id, cancellationToken);

            if (caregiver is not null)
            {
                _caregiver = caregiver;
                return;
            }

            Governorate governorate = await EnsureLookupAsync(
                _caregivers.Governorates, g => g.EnglishName == "Alexandria",
                () => Governorate.Create("الإسكندرية", "Alexandria"));
            City city = await EnsureLookupAsync(
                _caregivers.Cities, c => c.EnglishName == "Smouha" && c.GovernorateId == governorate.Id,
                () => City.Create(governorate.Id, "سموحة", "Smouha"));
            Area area = await EnsureLookupAsync(
                _caregivers.Areas, a => a.EnglishName == "Smouha Area" && a.CityId == city.Id,
                () => Area.Create(city.Id, "سموحة", "Smouha Area"));
            Language language = await EnsureLookupAsync(
                _caregivers.Languages, l => l.EnglishName == "Arabic",
                () => Language.Create("ar", "العربية", "Arabic"));
            Service service = await EnsureLookupAsync(
                _caregivers.Services, s => s.EnglishName == "Test Medical Care",
                () => Service.Create("رعاية طبية تجريبية", "Test Medical Care", "test-data/icons/medical.png", CaregiverType.Medical, true));
            Specialization specialization = await EnsureLookupAsync(
                _caregivers.Specializations, s => s.EnglishName == "Test Geriatric Care",
                () => Specialization.Create("رعاية مسنين تجريبية", "Test Geriatric Care", true, CaregiverType.Medical));
            ProfessionalTitle professionalTitle = await EnsureLookupAsync(
                _caregivers.ProfessionalTitles, t => t.EnglishName == "Test Nurse",
                () => ProfessionalTitle.Create("ممرض تجريبي", "Test Nurse", true));
            AcademicDegree academicDegree = await EnsureLookupAsync(
                _caregivers.AcademicDegrees, d => d.EnglishName == "Test BSc Nursing",
                () => AcademicDegree.Create("بكالوريوس تمريض تجريبي", "Test BSc Nursing", true));

            Caregiver created = Caregiver.Create(_caregiverUser.Id, CaregiverType.Medical);
            created.SelectService(service);
            created.SelectLanguage(language);
            created.SelectArea(area);
            created.UpdateMedicalProfile(
                professionalTitle, 8, specialization, academicDegree,
                "W5 Fixture Clinic", "Bounded synthetic local fixture caregiver.", _now);
            created.UpdateMedicalPricing(500m, 900m, 1300m, 1800m);
            created.AddMedicalHomeVisitWindow(DayOfWeek.Saturday, new TimeOnly(8, 0), new TimeOnly(22, 0));
            created.AddMedicalHomeVisitWindow(DayOfWeek.Wednesday, new TimeOnly(8, 0), new TimeOnly(22, 0));
            created.AddMedicalShift(DayOfWeek.Monday, MedicalShiftType.EightHourMorning);
            created.AddCertificate(
                CaregiverCertificateType.PracticeLicense,
                "test-data/certificates/practice-license.pdf",
                _today.AddYears(1),
                _today);
            created.AddCertificate(
                CaregiverCertificateType.GraduationCertificate,
                "test-data/certificates/graduation.pdf",
                null,
                _today);
            created.SubmitForReview(_now, _today);
            foreach (CaregiverCertificate certificate in created.Certificates)
            {
                created.VerifyCertificate(certificate.Id);
            }

            created.Approve(_now, _today);
            created.BecomeAvailable(_today);

            _caregivers.Caregivers.Add(created);
            await _caregivers.SaveChangesAsync(cancellationToken);
            _caregiver = created;
        }

        private async Task EnsureFacilityAsync()
        {
            CareHomeFacility? facility = await _careHomes.Facilities
                .FirstOrDefaultAsync(item => item.OwnerUserId == _careHomeOwnerUser.Id, cancellationToken);

            if (facility is null)
            {
                facility = CareHomeFacility.CreateDraft(_careHomeOwnerUser.Id, _now);
                _careHomes.Facilities.Add(facility);
                await _careHomes.SaveChangesAsync(cancellationToken);

                facility.SaveDraft(_careHomeOwnerUser.Id, facility.Version, FacilityProfile(), _now);
                UserId reviewer = _superAdminUser.Id;
                foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
                {
                    facility.UploadDocument(
                        _careHomeOwnerUser.Id, type,
                        $"private/portal-fixture-{_runTag}-{type}.pdf", "application/pdf", 10, null, _now);
                }

                facility.Submit(_careHomeOwnerUser.Id, facility.Version, _now);
                foreach (CareHomeDocument document in facility.Documents.ToArray())
                {
                    facility.VerifyDocument(reviewer, facility.Version, document.Id, null, true, _now);
                }

                facility.Review(
                    reviewer, facility.Version, CareHomeReviewAction.Approved, null, _now, _today);
                await _careHomes.SaveChangesAsync(cancellationToken);
            }

            _facility = facility;

            CareHomeRoomType? roomType = await _careHomes.RoomTypes
                .FirstOrDefaultAsync(item => item.FacilityId == facility.Id, cancellationToken);

            if (roomType is null)
            {
                roomType = CareHomeRoomType.Create(
                    facility.Id, "غرفة مشتركة", "Shared room", 1000m, CareHomeAllocationMode.Shared, _now);
                CareHomeRoom room = CareHomeRoom.Create(facility.Id, roomType.Id, "101", _now);
                CareHomeBed bed = CareHomeBed.Create(facility.Id, room.Id, "A", _now);
                _careHomes.RoomTypes.Add(roomType);
                _careHomes.Rooms.Add(room);
                _careHomes.Beds.Add(bed);
                await _careHomes.SaveChangesAsync(cancellationToken);
                _roomType = roomType;
                _room = room;
                _bed = bed;
                return;
            }

            _roomType = roomType;
            _room = await _careHomes.Rooms.FirstAsync(item => item.RoomTypeId == roomType.Id, cancellationToken);
            _bed = await _careHomes.Beds.FirstAsync(item => item.RoomId == _room.Id, cancellationToken);
        }

        private async Task EnsureSubscriptionAsync()
        {
            SubscriptionPlanVersion free = await EnsurePlanVersionAsync(
                SubscriptionPlan.FreeKey, SubscriptionPlan.Free, isPublished: true, isAvailableForNewSales: true);
            _ = free;
            _premiumPlan = await EnsurePlanVersionAsync(
                SubscriptionPlan.PremiumKey, SubscriptionPlan.Premium, isPublished: true, isAvailableForNewSales: false);

            FamilySubscription? subscription = await _families.FamilySubscriptions.FirstOrDefaultAsync(
                item => item.FamilyId == _family.Id && item.IsCurrent, cancellationToken);

            if (subscription is null)
            {
                subscription = FamilySubscription.Create(_family.Id, _premiumPlan, _now.AddDays(-5));
                subscription.SetCurrentPeriodSettlement(_premiumPlan.Price, taxRatePercentage: 0m);
                _families.FamilySubscriptions.Add(subscription);
                await _families.SaveChangesAsync(cancellationToken);
            }
            else
            {
                // Reuse is only safe when the stored snapshot still identifies
                // THIS fixture's plan. The plan terms are immutable storage, so a
                // mismatch means the current subscription belongs to something
                // else: refuse rather than attach the claim/refund to it.
                RequireFixtureString(
                    SubscriptionPlan.PremiumKey, subscription.PlanKey, "current family subscription plan key");
            }

            _subscription = subscription;

            SubscriptionPaymentAttempt? attempt = await _families.SubscriptionPaymentAttempts.FirstOrDefaultAsync(
                item => item.FamilyId == _family.Id
                    && item.SubscriptionId == subscription.Id
                    && item.PlanKey == SubscriptionPlan.PremiumKey
                    && item.Status == SubscriptionPaymentAttemptStatus.Succeeded,
                cancellationToken);

            if (attempt is null)
            {
                attempt = SubscriptionPaymentAttempt.Create(
                    _family.Id,
                    _premiumPlan,
                    basePrice: _premiumPlan.Price,
                    discountPercentage: 0m,
                    discountAmount: 0m,
                    taxableAmount: _premiumPlan.Price,
                    taxRatePercentage: 0m,
                    taxAmount: 0m,
                    totalPayable: _premiumPlan.Price,
                    couponCode: null,
                    method: SubscriptionPaymentMethod.Card,
                    createdOnUtc: _now.AddDays(-5));
                attempt.RecordPaymobOrder($"{MarkerPrefix}-{_runTag}-subscription-order");
                attempt.TryMarkSucceeded($"{MarkerPrefix}-{_runTag}-subscription-txn", _now.AddDays(-5).AddMinutes(1));
                attempt.LinkSubscription(subscription.Id);
                attempt.CapturePlatformCharge(feeRatePercentage: 1.00m, feeAmount: 0m, ruleVersion: 1);
                _families.SubscriptionPaymentAttempts.Add(attempt);
                await _families.SaveChangesAsync(cancellationToken);
            }

            _attempt = attempt;
        }

        private async Task<SubscriptionPlanVersion> EnsurePlanVersionAsync(
            string key, SubscriptionPlan plan, bool isPublished, bool isAvailableForNewSales)
        {
            SubscriptionPlanVersion? existing = await _families.SubscriptionPlanVersions
                .Include(item => item.Benefits)
                .AsSplitQuery()
                .FirstOrDefaultAsync(item => item.Key == key && item.Version == plan.Version, cancellationToken);

            if (existing is not null)
            {
                return existing;
            }

            existing = SubscriptionPlanVersion.Create(
                plan,
                isPublished,
                isAvailableForNewSales,
                createdOnUtc: _now,
                publishedOnUtc: isPublished ? _now : null);
            _families.SubscriptionPlanVersions.Add(existing);
            await _families.SaveChangesAsync(cancellationToken);
            return existing;
        }

        // ------------------------------------------------------------------
        // Caregiver (Families booking) groups
        // ------------------------------------------------------------------

        private async Task<PortalFixtureGroupResult> CaregiverRefundAmbiguityAsync()
        {
            Booking booking = await EnsurePaidFamiliesBookingAsync(
                PortalFixtureGroups.CaregiverRefundAmbiguity, confirmed: true);

            if (!booking.IsRefundOutcomeAmbiguous && !booking.IsRefundAttemptInFlight)
            {
                long award = Math.Min(booking.RefundableCeilingMinorUnits(), 100L);
                Require(award > 0, "the ambiguity booking carries no refundable amount to mark as ambiguous.");
                booking.ClaimRefundAttempt(Guid.NewGuid(), award, _now.AddMinutes(-20));
                booking.MarkRefundOutcomeAmbiguous(
                    $"{MarkerPrefix}-{_runTag}-synthetic-ambiguous-ref",
                    "Synthetic local ambiguity marker: no provider was contacted.",
                    _now.AddMinutes(-19),
                    award);
                await _families.SaveChangesAsync(cancellationToken);
            }

            Booking readBack = await _families.Bookings.AsNoTracking()
                .FirstAsync(item => item.Id == booking.Id, cancellationToken);
            BookingRefundState state = BookingRefundStates.Resolve(readBack);
            Require(
                state == BookingRefundState.Pending,
                $"the caregiver ambiguity fixture read back as {state}, not Pending; an unconfirmed refund would not render as uncertain.");

            return Group(
                PortalFixtureGroups.CaregiverRefundAmbiguity,
                $"caregiver refund ambiguity marker raised; persisted refund state={state}",
                [Record("FamiliesBooking", booking.Id.Value, Describe(readBack))]);
        }

        private async Task<PortalFixtureGroupResult> CaregiverRefundFailureAsync()
        {
            Booking booking = await EnsurePaidFamiliesBookingAsync(
                PortalFixtureGroups.CaregiverRefundFailure, confirmed: true);

            if (booking.Status == BookingStatus.Confirmed)
            {
                // A definitive provider refusal is portal-visible as a FAILED refund
                // only once the booking is in a closed-without-refund status: that is
                // what BookingRefundStates.Resolve requires, and the retained claim id
                // and award stay as the durable retry evidence. The shared driver runs
                // the exact production sequence, so the offline self-check exercises it
                // against the real domain too.
                PortalFixtureDomainChecks.DriveConfirmedToFailedRefund(booking, _now);
                await _families.SaveChangesAsync(cancellationToken);
            }

            Booking readBack = await _families.Bookings.AsNoTracking()
                .FirstAsync(item => item.Id == booking.Id, cancellationToken);
            BookingRefundState state = BookingRefundStates.Resolve(readBack);
            Require(
                state == BookingRefundState.Failed,
                $"the caregiver refund-failure fixture read back as {state}, not Failed; the portal would not offer the retry.");
            Require(
                BookingRefundStates.ClosedWithoutRefundStatus.Contains(readBack.Status),
                $"the caregiver refund-failure fixture ended in {readBack.Status}, which is not a closed-without-refund status.");

            return Group(
                PortalFixtureGroups.CaregiverRefundFailure,
                $"caregiver refund failed (persisted status={readBack.Status}, refund state={state}, retryable=True)",
                [Record("FamiliesBooking", booking.Id.Value, Describe(readBack))]);
        }

        private async Task<PortalFixtureGroupResult> CaregiverCompletedBookingAsync()
        {
            Booking booking = await EnsurePaidFamiliesBookingAsync(
                PortalFixtureGroups.CaregiverCompletedBooking, confirmed: true);

            if (booking.Status == BookingStatus.Confirmed)
            {
                // Coherent chronology: confirmed -> started -> completed, every step
                // after the one it depends on. The shared driver is also what the
                // offline self-check calls, so a chronology regression fails it.
                PortalFixtureDomainChecks.DriveVisitToCompleted(booking, _now);
                await _families.SaveChangesAsync(cancellationToken);
            }

            string reference = $"{MarkerPrefix}-{_runTag}-payout";
            CaregiverPayout? payout = await _caregivers.Payouts.FirstOrDefaultAsync(
                item => item.TransferReference == reference, cancellationToken);

            if (payout is null)
            {
                payout = CaregiverPayout.Record(
                    booking.Id,
                    _caregiver.Id,
                    grossAmount: 500m,
                    platformFeeAmount: 0m,
                    currency: Currency,
                    policyVersion: 1,
                    chargeRuleVersion: 1,
                    bankCode: "CIB",
                    maskedIban: "****1234",
                    transferReference: reference,
                    evidence: $"{MarkerPrefix}-{_runTag}-synthetic-transfer-evidence",
                    reason: "Bounded synthetic local fixture payout (no real transfer).",
                    recordedBy: _supportUser.Id,
                    recordedOnUtc: _now.AddMinutes(1));
                _caregivers.Payouts.Add(payout);
                await _caregivers.SaveChangesAsync(cancellationToken);
            }

            Booking readBack = await _families.Bookings.AsNoTracking()
                .FirstAsync(item => item.Id == booking.Id, cancellationToken);
            CaregiverPayout payoutRead = await _caregivers.Payouts.AsNoTracking()
                .FirstAsync(item => item.TransferReference == reference, cancellationToken);

            PortalFixtureDomainChecks.RequireCompletedChronology(readBack);

            return Group(
                PortalFixtureGroups.CaregiverCompletedBooking,
                $"completed paid caregiver booking (confirmed={readBack.ConfirmedOnUtc:o}, started={readBack.StartedOnUtc:o}, completed={readBack.CompletedOnUtc:o}) with a persisted synthetic payout ({payoutRead.Status})",
                [
                    Record("FamiliesBooking", booking.Id.Value, Describe(readBack)),
                    Record("CaregiverPayout", payoutRead.Id.Value, payoutRead.Status.ToString())
                ]);
        }

        private async Task<Booking> EnsurePaidFamiliesBookingAsync(string group, bool confirmed)
        {
            string marker = $"{_baseMarker}:{group}";
            Booking? existing = await _families.Bookings
                .FirstOrDefaultAsync(item => item.SpecialInstructions == marker, cancellationToken);

            if (existing is not null)
            {
                return existing;
            }

            Booking booking = Booking.Create(
                _family.Id,
                _owner.Id,
                _elderly.Id,
                _caregiver.Id,
                BookingCaregiverType.Medical,
                BookingShiftType.HomeVisit,
                _today.AddDays((int)(StableNumber(group) % 5) + 1),
                new TimeOnly(10, 0),
                new TimeOnly(12, 0),
                "14 Synthetic Fixture Street, Alexandria (local test data)",
                marker,
                BookingPriceSnapshot.Calculate(500m, 15m),
                acceptanceDeadlineUtc: _now.AddHours(24),
                currentDate: _today,
                createdOnUtc: _now);

            string orderId = $"{MarkerPrefix}-{_runTag}-{group}";

            if (confirmed)
            {
                PortalFixtureDomainChecks.DrivePendingToConfirmed(
                    booking, orderId, $"{MarkerPrefix}-{_runTag}-txn", _now);
            }
            else
            {
                booking.RecordPaymentIntent(orderId, PaymentMethod.Card, _now.AddMinutes(-4));
                booking.MarkAsPaid(orderId, $"{MarkerPrefix}-{_runTag}-txn", _now.AddMinutes(-3));
            }

            _families.Bookings.Add(booking);
            await _families.SaveChangesAsync(cancellationToken);
            return booking;
        }

        // ------------------------------------------------------------------
        // CareHome groups
        // ------------------------------------------------------------------

        private async Task<PortalFixtureGroupResult> CareHomePaidAwaitingDecisionAsync()
        {
            // Two distinct run-owned slots awaiting the facility decision — one the
            // operator can accept and one they can reject — plus a persisted rejected
            // example so the RefundPending read path is exercised without consuming
            // either live decision slot.
            CareHomeBooking acceptSlot = await EnsureCareHomeBookingAsync(
                $"{PortalFixtureGroups.CareHomePaidAwaitingDecision}:accept", accept: false);
            CareHomeBooking rejectSlot = await EnsureCareHomeBookingAsync(
                $"{PortalFixtureGroups.CareHomePaidAwaitingDecision}:reject", accept: false);
            CareHomeBooking rejectedExample = await EnsureRejectedCareHomeBookingAsync(
                $"{PortalFixtureGroups.CareHomePaidAwaitingDecision}:rejected");

            CareHomeBooking acceptRead = await _careHomes.Bookings.AsNoTracking()
                .FirstAsync(item => item.Id == acceptSlot.Id, cancellationToken);
            CareHomeBooking rejectRead = await _careHomes.Bookings.AsNoTracking()
                .FirstAsync(item => item.Id == rejectSlot.Id, cancellationToken);
            CareHomeBooking rejectedRead = await _careHomes.Bookings.AsNoTracking()
                .FirstAsync(item => item.Id == rejectedExample.Id, cancellationToken);

            Require(
                acceptRead.Status == CareHomeBookingStatus.PaidAwaitingDecision
                    && acceptRead.PaymentStatus == CareHomeBookingPaymentStatus.Paid
                    && rejectRead.Status == CareHomeBookingStatus.PaidAwaitingDecision
                    && rejectRead.PaymentStatus == CareHomeBookingPaymentStatus.Paid,
                $"the facility-decision slots read back as {acceptRead.Status}/{acceptRead.PaymentStatus} and {rejectRead.Status}/{rejectRead.PaymentStatus}, not paid and awaiting a decision.");
            Require(
                rejectedRead.Status == CareHomeBookingStatus.RefundPending
                    && rejectedRead.RefundStatus == CareHomeRefundStatus.Pending,
                $"the persisted rejected example read back as {rejectedRead.Status}/{rejectedRead.RefundStatus}, not RefundPending/Pending.");

            return Group(
                PortalFixtureGroups.CareHomePaidAwaitingDecision,
                "two distinct paid slots awaiting the facility decision (accept and reject) plus a persisted rejected example in RefundPending",
                [
                    Record("CareHomeBookingAcceptSlot", acceptRead.Id.ToString(), Describe(acceptRead)),
                    Record("CareHomeBookingRejectSlot", rejectRead.Id.ToString(), Describe(rejectRead)),
                    Record("CareHomeBookingRejectedExample", rejectedRead.Id.ToString(), Describe(rejectedRead))
                ]);
        }

        private async Task<PortalFixtureGroupResult> CareHomeRefundAmbiguityAsync()
        {
            CareHomeBooking booking = await EnsureCareHomeRefundPendingAsync(
                PortalFixtureGroups.CareHomeRefundAmbiguity);

            if (!booking.IsRefundOutcomeAmbiguous && booking.RefundClaimedOnUtc is null)
            {
                if (booking.TryClaimRefund(_now.AddMinutes(-20)))
                {
                    booking.MarkRefundOutcomeAmbiguous(
                        "Synthetic local ambiguity marker: no provider was contacted.",
                        _now.AddMinutes(-19));
                    await _careHomes.SaveChangesAsync(cancellationToken);
                }
            }

            CareHomeBooking readBack = await _careHomes.Bookings.AsNoTracking()
                .FirstAsync(item => item.Id == booking.Id, cancellationToken);

            return Group(
                PortalFixtureGroups.CareHomeRefundAmbiguity,
                $"care home refund ambiguity marker raised (ambiguous={readBack.IsRefundOutcomeAmbiguous})",
                [Record("CareHomeBooking", booking.Id.ToString(), Describe(readBack))]);
        }

        private async Task<PortalFixtureGroupResult> CareHomeRefundFailureAsync()
        {
            CareHomeBooking booking = await EnsureCareHomeRefundPendingAsync(
                PortalFixtureGroups.CareHomeRefundFailure);

            if (booking.RefundStatus == CareHomeRefundStatus.Pending && booking.RefundClaimedOnUtc is null)
            {
                if (booking.TryClaimRefund(_now.AddMinutes(-20)))
                {
                    booking.MarkRefundFailed(
                        "Synthetic local refusal: no provider was contacted.",
                        _now.AddMinutes(-19));
                    await _careHomes.SaveChangesAsync(cancellationToken);
                }
            }

            CareHomeBooking readBack = await _careHomes.Bookings.AsNoTracking()
                .FirstAsync(item => item.Id == booking.Id, cancellationToken);

            return Group(
                PortalFixtureGroups.CareHomeRefundFailure,
                $"care home refund marked failed after a synthetic refusal (status={readBack.RefundStatus})",
                [Record("CareHomeBooking", booking.Id.ToString(), Describe(readBack))]);
        }

        private async Task<PortalFixtureGroupResult> CareHomeEvidencedExternalRefundAsync()
        {
            CareHomeBooking booking = await EnsureCareHomeRefundPendingAsync(
                PortalFixtureGroups.CareHomeEvidencedExternalRefund);

            if (booking.RefundStatus == CareHomeRefundStatus.Pending && booking.RefundClaimedOnUtc is null)
            {
                if (booking.TryClaimRefund(_now.AddMinutes(-20)))
                {
                    string reference = $"{MarkerPrefix}-{_runTag}-evidenced-refund";
                    booking.MarkRefundInitiated(reference, _now.AddMinutes(-19));
                    booking.MarkRefundCompleted(reference, _now.AddMinutes(-18));
                    await _careHomes.SaveChangesAsync(cancellationToken);
                }
            }

            CareHomeBooking readBack = await _careHomes.Bookings.AsNoTracking()
                .FirstAsync(item => item.Id == booking.Id, cancellationToken);

            return Group(
                PortalFixtureGroups.CareHomeEvidencedExternalRefund,
                $"care home refund completed from synthetic external evidence (status={readBack.RefundStatus})",
                [Record("CareHomeBooking", booking.Id.ToString(), Describe(readBack))]);
        }

        private async Task<PortalFixtureGroupResult> CareHomeBookingExtensionAsync()
        {
            CareHomeBooking current = await EnsureCareHomeBookingAsync(
                PortalFixtureGroups.CareHomeBookingExtension, accept: true);

            // CreateExtension copies the parent segment's care notes, so the
            // ownership marker cannot distinguish the extension. The public
            // ExtensionOfBookingId link is the stable idempotency key instead.
            CareHomeBooking? extension = await _careHomes.Bookings
                .FirstOrDefaultAsync(item => item.ExtensionOfBookingId == current.Id, cancellationToken);

            if (extension is null)
            {
                extension = CareHomeBooking.CreateExtension(
                    current, _owner.Id, baseAmount: 1000m, feeAmount: 100m, taxAmount: 50m, ruleVersion: 4, _now);
                _careHomes.Bookings.Add(extension);
                await _careHomes.SaveChangesAsync(cancellationToken);
            }

            CareHomeBooking readBack = await _careHomes.Bookings.AsNoTracking()
                .FirstAsync(item => item.Id == extension.Id, cancellationToken);

            return Group(
                PortalFixtureGroups.CareHomeBookingExtension,
                "care home booking extension created off an accepted paid segment",
                [
                    Record("CareHomeBookingCurrent", current.Id.ToString(), Describe(current)),
                    Record("CareHomeBookingExtension", extension.Id.ToString(), Describe(readBack))
                ]);
        }

        private async Task<PortalFixtureGroupResult> CareHomeCheckoutDisputeAsync()
        {
            string marker = $"{_baseMarker}:{PortalFixtureGroups.CareHomeCheckoutDispute}";
            CareHomeBooking? existing = await _careHomes.Bookings
                .FirstOrDefaultAsync(item => item.CareNeedsNotes == marker, cancellationToken);

            CareHomeBooking booking = existing ?? await EnsureCareHomeBookingWithMarkerAsync(
                PortalFixtureGroups.CareHomeCheckoutDispute, accept: true);

            if (existing is null)
            {
                booking.AssignPhysicalResource(_room.Id, _bed.Id, _now.AddMinutes(-20));
                booking.RecordCheckIn(_careHomeOwnerUser.Id, _now.AddMinutes(-19));
                await _careHomes.SaveChangesAsync(cancellationToken);
            }

            if (booking.ActualCheckOutOnUtc is null)
            {
                booking.RecordCheckOut(_careHomeOwnerUser.Id, _now.AddMinutes(-17));
                await _careHomes.SaveChangesAsync(cancellationToken);
            }

            CareHomeCheckInDispute? dispute = await _careHomes.CheckInDisputes
                .FirstOrDefaultAsync(item => item.BookingId == booking.Id, cancellationToken);

            if (dispute is null)
            {
                dispute = CareHomeCheckInDispute.Open(
                    booking.Id,
                    booking.FacilityId,
                    checkoutOnUtc: booking.ActualCheckOutOnUtc,
                    _owner.Id,
                    _now.AddMinutes(-16),
                    "Synthetic local checkout dispute: no family confirmation recorded.");
                _careHomes.CheckInDisputes.Add(dispute);
                await _careHomes.SaveChangesAsync(cancellationToken);
            }

            CareHomeBooking readBack = await _careHomes.Bookings.AsNoTracking()
                .FirstAsync(item => item.Id == booking.Id, cancellationToken);
            CareHomeCheckInDispute disputeRead = await _careHomes.CheckInDisputes.AsNoTracking()
                .FirstAsync(item => item.Id == dispute.Id, cancellationToken);

            return Group(
                PortalFixtureGroups.CareHomeCheckoutDispute,
                $"checkout recorded with no family confirmation and an open dispute ({disputeRead.Status})",
                [
                    Record("CareHomeBooking", booking.Id.ToString(), Describe(readBack)),
                    Record("CareHomeCheckInDispute", disputeRead.Id.ToString(), disputeRead.Status.ToString())
                ]);
        }

        private async Task<CareHomeBooking> EnsureCareHomeRefundPendingAsync(string group)
        {
            CareHomeBooking booking = await EnsureCareHomeBookingAsync(group, accept: true);

            if (booking.Status == CareHomeBookingStatus.Accepted
                && booking.PaymentStatus == CareHomeBookingPaymentStatus.Paid)
            {
                booking.CancelByFacility(
                    "Synthetic local fixture cancellation (no provider movement).", _now.AddMinutes(-20));
                await _careHomes.SaveChangesAsync(cancellationToken);
            }

            return booking;
        }

        private async Task<CareHomeBooking> EnsureCareHomeBookingAsync(string group, bool accept)
            => await EnsureCareHomeBookingWithMarkerAsync(group, accept);

        private async Task<CareHomeBooking> EnsureCareHomeBookingWithMarkerAsync(string group, bool accept)
        {
            string marker = $"{_baseMarker}:{group}";
            CareHomeBooking? existing = await _careHomes.Bookings
                .FirstOrDefaultAsync(item => item.CareNeedsNotes == marker, cancellationToken);

            if (existing is not null)
            {
                return existing;
            }

            CareHomeBooking booking = CareHomeBooking.Create(
                _facility.Id,
                _owner.Id,
                _family.Id,
                _elderly.Id,
                _roomType.Id,
                _today.AddDays(2),
                baseAmount: 1000m,
                feeAmount: 100m,
                taxAmount: 50m,
                ruleVersion: 4,
                arabicName: "الجد التجريبي",
                englishName: "W5 Fixture Grandfather",
                age: 75,
                medicalJson: null,
                contactName: "W5 Fixture Family Owner",
                contactPhone: "+201000000001",
                relationship: "Grandfather",
                careNotes: marker,
                utcNow: _now);

            booking.MarkPaid(StableNumber(marker), _now.AddMinutes(-30));
            if (accept)
            {
                booking.Accept(_now.AddMinutes(-29));
            }

            _careHomes.Bookings.Add(booking);
            await _careHomes.SaveChangesAsync(cancellationToken);
            return booking;
        }

        private async Task<CareHomeBooking> EnsureRejectedCareHomeBookingAsync(string group)
        {
            CareHomeBooking booking = await EnsureCareHomeBookingAsync(group, accept: false);

            if (booking.Status == CareHomeBookingStatus.PaidAwaitingDecision)
            {
                booking.Reject(
                    "Synthetic local fixture rejection (no provider movement).", _now.AddMinutes(-29));
                await _careHomes.SaveChangesAsync(cancellationToken);
            }

            return booking;
        }

        // ------------------------------------------------------------------
        // Subscription / duplicate-claim and complaint groups
        // ------------------------------------------------------------------

        private async Task<PortalFixtureGroupResult> SubscriptionAndDuplicateClaimAsync()
        {
            DuplicateChargeClaim claim = await EnsureDuplicateClaimAsync();
            ShutdownRefund shutdown = await EnsureShutdownRefundAsync();

            return Group(
                PortalFixtureGroups.SubscriptionAndDuplicateClaim,
                $"a current paid subscription capture with a submitted duplicate-charge claim ({claim.State}) "
                    + "awaiting Support review, plus a persisted successful service-shutdown refund "
                    + $"(award {shutdown.AwardMinorUnits} minor units, ledger {shutdown.EntryType}) on its own "
                    + "run-owned capture so the two never compete for one refund budget",
                [
                    Record("FamilySubscription", _subscription.Id.ToString(), _subscription.PlanKey),
                    Record("SubscriptionPaymentAttempt", _attempt.Id.ToString(), "Succeeded (duplicate-claim capture)"),
                    Record("DuplicateChargeClaim", claim.Id.ToString(), claim.State.ToString()),
                    Record("FamilySubscriptionShutdown", _shutdownSubscription.Id.ToString(), _shutdownSubscription.PlanKey),
                    Record("SubscriptionPaymentAttemptShutdown", _shutdownAttempt.Id.ToString(), "Succeeded (shutdown capture)"),
                    Record("SubscriptionRefundRequest", shutdown.RefundCaseId.ToString(), shutdown.Reason.ToString()),
                    Record("RefundLedgerEntry", shutdown.RefundCaseId.ToString(), $"{shutdown.EntryType} {shutdown.AwardMinorUnits}")
                ]);
        }

        /// <summary>
        /// The submitted duplicate-charge claim and the genuine refund case it
        /// owns. A rerun reuses the stored claim only after proving its refund
        /// case is still the one this fixture capture derives; a foreign claim
        /// on the same capture is refused. Nothing moved money or claimed
        /// budget, so the claim serializes as Submitted (ready for Support).
        /// </summary>
        private async Task<DuplicateChargeClaim> EnsureDuplicateClaimAsync()
        {
            Guid expectedRefundCaseId = SubscriptionRefundCaseIds.ForDuplicateCharge(_attempt.Id);

            DuplicateChargeClaim? existing = await _finance.DuplicateChargeClaims.AsNoTracking()
                .FirstOrDefaultAsync(item => item.PaymentAttemptId == _attempt.Id, cancellationToken);

            if (existing is not null)
            {
                Require(
                    existing.RefundCaseId == expectedRefundCaseId,
                    $"the stored duplicate-charge claim's refund case {existing.RefundCaseId} is not this fixture "
                        + $"capture's derived case {expectedRefundCaseId}; refusing to reuse a foreign claim.");
                return existing;
            }

            var command = new SubmitDuplicateChargeClaimCommand(
                _owner.Id,
                _attempt.Id,
                IdempotencyKey: $"{MarkerPrefix}-{_runTag}-duplicate-claim",
                EvidenceReference: $"{MarkerPrefix}-{_runTag}-synthetic-evidence",
                CustomerReportedOnUtc: _now.AddMinutes(-30));

            Result<FamilyDuplicateChargeClaimView> result = await _sender.Send(command, cancellationToken);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"duplicate-charge submission refused: {result.Error.Code}");
            }

            DuplicateChargeClaim claim = await _finance.DuplicateChargeClaims.AsNoTracking()
                .FirstAsync(item => item.Id == result.Value.ClaimId, cancellationToken);
            Require(
                claim.State == DuplicateChargeClaimState.Submitted,
                $"the duplicate-charge claim read back as {claim.State}, not Submitted and awaiting Support review.");
            Require(
                claim.RefundCaseId == expectedRefundCaseId,
                "the submitted duplicate-charge claim did not persist its derived refund case.");
            return claim;
        }

        /// <summary>
        /// A successful paid subscription <em>service-shutdown</em> refund, with
        /// the budget claim, the append-only ledger entry and the idempotency
        /// request all read back. It runs on its own run-owned family,
        /// subscription and capture so it never contends with the duplicate
        /// claim for one capture's refund budget.
        /// </summary>
        private async Task<ShutdownRefund> EnsureShutdownRefundAsync()
        {
            await EnsureShutdownSubscriptionSlotAsync();

            string key = $"{MarkerPrefix}-{_runTag}-shutdown-refund";
            SubscriptionRefundRequest? existing = await _finance.SubscriptionRefundRequests.AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.SubscriptionId == _shutdownSubscription.Id && item.IdempotencyKey == key,
                    cancellationToken);

            Guid refundCaseId;
            long awardMinorUnits;
            if (existing is not null)
            {
                refundCaseId = existing.RefundCaseId;
                awardMinorUnits = existing.RequestedAmountMinorUnits;
            }
            else
            {
                var command = new RefundSubscriptionServiceShutdownCommand(
                    _shutdownSubscription.Id,
                    _shutdownOwner.Id,
                    ShutdownOnUtc: _now,
                    IdempotencyKey: key,
                    EvidenceReference: $"{MarkerPrefix}-{_runTag}-synthetic-shutdown-evidence");

                Result<SubscriptionRefundResponse> result = await _sender.Send(command, cancellationToken);
                if (!result.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"the service-shutdown refund was refused: {result.Error.Code}");
                }

                refundCaseId = result.Value.RefundCaseId;
                awardMinorUnits = result.Value.AwardedMinorUnits;
            }

            Require(
                awardMinorUnits > 0,
                $"the service-shutdown refund computed a {awardMinorUnits} award; a successful paid-subscription "
                    + "shutdown refund requires a positive prorated movement.");

            SubscriptionRefundRequest request = await _finance.SubscriptionRefundRequests.AsNoTracking()
                .FirstAsync(item => item.RefundCaseId == refundCaseId, cancellationToken);
            RefundLedgerEntry? entry = await _finance.RefundLedgerEntries.AsNoTracking()
                .FirstOrDefaultAsync(item => item.RefundCaseId == refundCaseId, cancellationToken);
            SubscriptionRefundBudget? budget = await _finance.SubscriptionRefundBudgets.AsNoTracking()
                .FirstOrDefaultAsync(item => item.PaymentAttemptId == _shutdownAttempt.Id, cancellationToken);

            Require(entry is not null, "the service-shutdown refund ledger entry did not persist.");
            Require(
                entry!.Amount.MinorUnits == awardMinorUnits,
                $"the persisted shutdown ledger entry is {entry.Amount.MinorUnits} minor units, not the awarded {awardMinorUnits}.");
            Require(
                budget is not null && budget.ClaimedMinorUnits == awardMinorUnits,
                $"the shutdown capture budget claim is "
                    + $"{(budget is null ? "<none>" : budget.ClaimedMinorUnits.ToString(CultureInfo.InvariantCulture))}, "
                    + $"not the awarded {awardMinorUnits}.");

            return new ShutdownRefund(
                request.RefundCaseId,
                request.RequestedAmountMinorUnits,
                request.Reason,
                entry.EntryType.ToString());
        }

        private async Task EnsureShutdownSubscriptionSlotAsync()
        {
            if (_shutdownSlotReady)
            {
                return;
            }

            _shutdownOwner = await EnsureUserAsync(
                "shutdown.owner", "مالك العيلة الثاني التجريبي", "W5 Fixture Shutdown Owner",
                AccountType.Family);

            Family family = await EnsureFixtureFamilyAsync(
                _shutdownOwner, $"W5 Fixture Shutdown Family {_runTag}");

            SubscriptionPlanVersion premiumPlan = await EnsurePlanVersionAsync(
                SubscriptionPlan.PremiumKey, SubscriptionPlan.Premium,
                isPublished: true, isAvailableForNewSales: false);

            FamilySubscription? subscription = await _families.FamilySubscriptions.FirstOrDefaultAsync(
                item => item.FamilyId == family.Id && item.IsCurrent, cancellationToken);

            if (subscription is null)
            {
                subscription = FamilySubscription.Create(family.Id, premiumPlan, _now.AddDays(-10));
                subscription.SetCurrentPeriodSettlement(premiumPlan.Price, taxRatePercentage: 0m);
                _families.FamilySubscriptions.Add(subscription);
                await _families.SaveChangesAsync(cancellationToken);
            }
            else
            {
                RequireFixtureString(
                    SubscriptionPlan.PremiumKey, subscription.PlanKey, "shutdown subscription plan key");
            }

            _shutdownSubscription = subscription;

            SubscriptionPaymentAttempt? attempt = await _families.SubscriptionPaymentAttempts.FirstOrDefaultAsync(
                item => item.FamilyId == family.Id
                    && item.SubscriptionId == subscription.Id
                    && item.Status == SubscriptionPaymentAttemptStatus.Succeeded,
                cancellationToken);

            if (attempt is null)
            {
                attempt = SubscriptionPaymentAttempt.Create(
                    family.Id,
                    premiumPlan,
                    basePrice: premiumPlan.Price,
                    discountPercentage: 0m,
                    discountAmount: 0m,
                    taxableAmount: premiumPlan.Price,
                    taxRatePercentage: 0m,
                    taxAmount: 0m,
                    totalPayable: premiumPlan.Price,
                    couponCode: null,
                    method: SubscriptionPaymentMethod.Card,
                    createdOnUtc: _now.AddDays(-10));
                attempt.RecordPaymobOrder($"{MarkerPrefix}-{_runTag}-shutdown-subscription-order");
                attempt.TryMarkSucceeded(
                    $"{MarkerPrefix}-{_runTag}-shutdown-subscription-txn", _now.AddDays(-10).AddMinutes(1));
                attempt.LinkSubscription(subscription.Id);
                attempt.CapturePlatformCharge(feeRatePercentage: 1.00m, feeAmount: 0m, ruleVersion: 1);
                _families.SubscriptionPaymentAttempts.Add(attempt);
                await _families.SaveChangesAsync(cancellationToken);
            }

            _shutdownAttempt = attempt;
            _shutdownSlotReady = true;
        }

        /// <summary>
        /// Load-or-create a family owned by a fixture-verified user. Ownership is
        /// transitive: the owner id is the lookup key and the owner identity has
        /// already passed its own marker/account check, so the family is
        /// fixture-owned and can never be an unrelated row.
        /// </summary>
        private async Task<Family> EnsureFixtureFamilyAsync(User owner, string name)
        {
            Family? family = await _families.Families
                .FirstOrDefaultAsync(item => item.OwnerUserId == owner.Id, cancellationToken);

            if (family is null)
            {
                family = Family.Create(owner.Id, name);
                _families.Families.Add(family);
                await _families.SaveChangesAsync(cancellationToken);
            }

            return family;
        }

        private async Task<PortalFixtureGroupResult> ComplaintAsync(bool investigate)
        {
            string group = investigate
                ? PortalFixtureGroups.ComplaintInvestigating
                : PortalFixtureGroups.ComplaintReceived;
            string marker = $"{_baseMarker}:{group}";

            // A complaint's refund case is a correlation key to a real ledger,
            // and it must be the fixture's OWN refund case — never a random
            // Guid the schema merely happens to allow. The schema allows exactly
            // ONE review per refund case (ux_complaint_reviews_refund_case_id),
            // so the two complaint halves link to two DISTINCT fixture-owned
            // cases: the received complaint to the duplicate-charge claim's
            // case, the investigating complaint to the service-shutdown
            // refund's ledger case. Two reviews on one case would collide.
            Guid refundCaseId;
            if (investigate)
            {
                refundCaseId = (await EnsureShutdownRefundAsync()).RefundCaseId;
            }
            else
            {
                refundCaseId = (await EnsureDuplicateClaimAsync()).RefundCaseId;
            }

            ComplaintReview? review = await _finance.ComplaintReviews
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.RefundCaseId == refundCaseId, cancellationToken);

            if (review is null)
            {
                review = ComplaintReview.Open(
                    refundCaseId: refundCaseId,
                    familyId: _family.Id.Value,
                    reportingParty: ComplaintReportingParty.Family,
                    reportedByUserId: _owner.Id.Value,
                    reportedOnUtc: _now.AddMinutes(-90),
                    visitedOnUtc: null,
                    isSafetyConcern: false,
                    description: marker);
                _finance.ComplaintReviews.Add(review);
                await _finance.SaveChangesAsync(cancellationToken);
            }

            if (investigate && review.State == ComplaintReviewState.Received)
            {
                review.BeginInvestigation(
                    _superAdminUser.Id.Value,
                    _now.AddMinutes(-60),
                    $"{MarkerPrefix}-{_runTag}-synthetic-investigation-evidence");
                await _finance.SaveChangesAsync(cancellationToken);
            }

            ComplaintReview readBack = await _finance.ComplaintReviews.AsNoTracking()
                .FirstAsync(item => item.Id == review.Id, cancellationToken);

            if (investigate)
            {
                // Drive to Investigating when still Received, but never pull a
                // manually advanced case back: report the honest current state.
                Require(
                    readBack.State is ComplaintReviewState.Investigating
                        or ComplaintReviewState.Resolved
                        or ComplaintReviewState.Refused,
                    $"the investigated complaint read back as {readBack.State}, which is not at or past Investigating.");
            }

            return Group(
                group,
                $"complaint review in persisted state {readBack.State} linked to the fixture's own refund case",
                [Record("ComplaintReview", readBack.Id.ToString(), readBack.State.ToString())]);
        }

        // ------------------------------------------------------------------
        // Shared helpers
        // ------------------------------------------------------------------

        private async Task<T> EnsureLookupAsync<T>(
            DbSet<T> set,
            Expression<Func<T, bool>> predicate,
            Func<T> factory)
            where T : class
        {
            T? existing = await set.FirstOrDefaultAsync(predicate, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            T created = factory();
            set.Add(created);
            await _caregivers.SaveChangesAsync(cancellationToken);
            return created;
        }

        private static CareHomeProfileDraft FacilityProfile() => new(
            "دار الأمل التجريبية",
            "W5 Fixture Hope House",
            "رعاية وإقامة آمنة (بيانات اختبارية)",
            "Safe local fixture care and accommodation",
            "Fixture Contact",
            "+201000000000",
            null,
            "Cairo",
            "Nasr City",
            "District 1",
            "Synthetic fixture street address",
            "الشروط التجريبية",
            "Synthetic fixture admission conditions",
            [],
            []);

        private PortalFixtureGroupResult Group(
            string group, string detail, IReadOnlyList<PortalFixtureRecord> records) =>
            new(group, Provisioned: true, Detail: detail, Records: records);

        /// <summary>
        /// Assert a read-back/domain invariant. A failure throws, so the group is
        /// reported unresolved instead of being served with a claimed state that
        /// was never verified.
        /// </summary>
        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>
        /// Assert a stored immutable ownership string still identifies THIS
        /// run's fixture. Used before reusing a pre-existing aggregate: a
        /// mismatch means the row belongs to something else and must be refused,
        /// never adopted.
        /// </summary>
        private static void RequireFixtureString(string expected, string? actual, string what)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"fixture ownership check failed for {what}: expected '{expected}' but found "
                        + $"'{actual ?? "<null>"}'; refusing to reuse an unrelated record.");
            }
        }

        private static PortalFixtureRecord Record(string kind, object id, string state) =>
            new(kind, Convert.ToString(id, CultureInfo.InvariantCulture) ?? string.Empty, state);

        private static string Describe(Booking booking) =>
            $"{booking.Status} ambiguous={booking.IsRefundOutcomeAmbiguous} inFlight={booking.IsRefundAttemptInFlight}";

        private static string Describe(CareHomeBooking booking) =>
            $"{booking.Status}/{booking.PaymentStatus} refund={booking.RefundStatus} ambiguous={booking.IsRefundOutcomeAmbiguous}";

        private PortalFixtureProvisionReport WriteReport(
            IReadOnlyList<PortalFixtureGroupResult> groups, IReadOnlyList<string> unresolved)
        {
            Directory.CreateDirectory(options.WorkRoot);
            string manifestPath = Path.Combine(
                options.WorkRoot, $"portal-fixture-manifest-{_runTag}.json");

            var report = new PortalFixtureProvisionReport(
                Passed: unresolved.Count == 0,
                ManifestPath: manifestPath,
                RunTag: _runTag,
                InstantUtc: _now,
                FamilyLoginEmail: $"{_runTag}.family.owner@test.sanad.local",
                Groups: groups,
                UnresolvedGroups: unresolved);

            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

            return report;
        }

        private static string SanitizeTag(string value)
        {
            var buffer = new System.Text.StringBuilder();
            foreach (char character in value ?? string.Empty)
            {
                if (char.IsLetterOrDigit(character))
                {
                    buffer.Append(char.ToLowerInvariant(character));
                }
            }

            string tag = buffer.Length == 0 ? "w5p" : buffer.ToString();
            return tag.Length > 24 ? tag[..24] : tag;
        }

        private static long StableNumber(string value) =>
            (long)(StableHash(value) & 0x7FFFFFFFFFFFFFFFUL);

        /// <summary>
        /// FNV-1a 64-bit: a stable synthetic id that does not depend on
        /// run-to-run hash seeding.
        /// </summary>
        private static ulong StableHash(string value)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                foreach (char character in value)
                {
                    hash ^= character;
                    hash *= 1099511628211UL;
                }

                return hash;
            }
        }

        private static DateTime EnsureUtc(DateTime value) =>
            value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

        private static string Safe(Exception exception)
        {
            string message = (exception.Message ?? string.Empty)
                .Replace('\r', ' ').Replace('\n', ' ').Trim();
            return message.Length > 200 ? message[..200] + "..." : message;
        }
    }
}

/// <summary>
/// The exact domain sequences the provisioner uses to drive a fixture into a
/// requested money state, factored out so the side-effect-free host self-check
/// (<c>--mode selfcheck</c>) executes the SAME code against the real domain and
/// the offline suite can prove the sequences are coherent without a database.
/// <para>
/// It builds only in-memory aggregates: no I/O, no SQL, no listener and no
/// provider call. The provisioner persists each returned aggregate through its
/// own <c>DbContext</c>; the self-check never persists anything.
/// </para>
/// </summary>
public static class PortalFixtureDomainChecks
{
    /// <summary>The result of the in-memory self-check: the checks that ran.</summary>
    public sealed record PortalFixtureDomainCheckReport(bool Passed, IReadOnlyList<string> Checks);

    public static PortalFixtureDomainCheckReport Run(DateTime instantUtc)
    {
        DateTime now = instantUtc.Kind == DateTimeKind.Utc
            ? instantUtc
            : DateTime.SpecifyKind(instantUtc, DateTimeKind.Utc);
        DateOnly today = DateOnly.FromDateTime(now);
        var checks = new List<string>();

        // 1. Families: pending -> paid -> confirmed, then a coherent visit.
        Booking completed = NewBooking(today, now, "completed");
        DrivePendingToConfirmed(completed, "selfcheck-completed-order", "selfcheck-completed-txn", now);
        DriveVisitToCompleted(completed, now);
        RequireCompletedChronology(completed);
        checks.Add("families.booking.completed");

        // 2. Families: a definitive refusal on a closed booking reads Failed.
        Booking failed = NewBooking(today, now, "failed");
        DrivePendingToConfirmed(failed, "selfcheck-failed-order", "selfcheck-failed-txn", now);
        DriveConfirmedToFailedRefund(failed, now);
        Require(
            BookingRefundStates.Resolve(failed) == BookingRefundState.Failed,
            $"the refund-failure sequence read back as {BookingRefundStates.Resolve(failed)}, not Failed.");
        checks.Add("families.refund.failed");

        // 3. Families: an unconfirmed attempt reads Pending, never Failed.
        Booking ambiguous = NewBooking(today, now, "ambiguous");
        DrivePendingToConfirmed(ambiguous, "selfcheck-ambiguous-order", "selfcheck-ambiguous-txn", now);
        long award = Math.Min(ambiguous.RefundableCeilingMinorUnits(), 100L);
        Require(award > 0, "the ambiguity sequence booking carries no refundable amount.");
        ambiguous.ClaimRefundAttempt(Guid.NewGuid(), award, now.AddMinutes(-1));
        ambiguous.MarkRefundOutcomeAmbiguous(
            "selfcheck-ambiguous-ref", "Synthetic self-check ambiguity; no provider was contacted.", now, award);
        Require(
            BookingRefundStates.Resolve(ambiguous) == BookingRefundState.Pending,
            $"an unconfirmed refund attempt read back as {BookingRefundStates.Resolve(ambiguous)}, not Pending.");
        checks.Add("families.refund.ambiguous");

        // 4. CareHomes: paid awaiting decision -> rejected -> RefundPending.
        CareHomeBooking careHome = NewCareHomeBooking(today, now);
        careHome.MarkPaid(90_000_001L, now);
        Require(
            careHome.Status == CareHomeBookingStatus.PaidAwaitingDecision,
            $"the care-home paid sequence read back as {careHome.Status}, not PaidAwaitingDecision.");
        careHome.Reject("Synthetic self-check rejection; no provider was contacted.", now.AddMinutes(1));
        Require(
            careHome.Status == CareHomeBookingStatus.RefundPending
                && careHome.RefundStatus == CareHomeRefundStatus.Pending,
            $"the care-home rejection read back as {careHome.Status}/{careHome.RefundStatus}, not RefundPending/Pending.");
        checks.Add("carehome.reject.refundpending");

        return new PortalFixtureDomainCheckReport(Passed: true, Checks: checks);
    }

    /// <summary>
    /// Paid then confirmed, in chronological order: a payment intent, the
    /// settlement, then the caretaker acceptance. The acceptance instant never
    /// precedes the confirmation it depends on.
    /// </summary>
    public static void DrivePendingToConfirmed(
        Booking booking, string orderId, string transactionId, DateTime utcNow)
    {
        booking.RecordPaymentIntent(orderId, PaymentMethod.Card, utcNow.AddMinutes(-4));
        booking.MarkAsPaid(orderId, transactionId, utcNow.AddMinutes(-3));
        booking.AcceptByCaregiver(utcNow.AddMinutes(-2));
        Require(
            booking.Status == BookingStatus.Confirmed,
            $"pending->confirmed ended in {booking.Status}, not Confirmed.");
        Require(booking.ConfirmedOnUtc is not null, "pending->confirmed recorded no confirmation instant.");
    }

    /// <summary>Confirmation, then start, then completion — each after its prerequisite.</summary>
    public static void DriveVisitToCompleted(Booking booking, DateTime utcNow)
    {
        Require(
            booking.Status == BookingStatus.Confirmed,
            $"a visit can only be driven from Confirmed, not {booking.Status}.");
        booking.StartVisit(utcNow.AddMinutes(-1));
        booking.CompleteVisit("Synthetic completed local fixture visit.", utcNow);
    }

    /// <summary>
    /// Confirmed -> a claimed-then-released refund attempt (the definitive
    /// refusal shape) -> a closed-without-refund status, which is exactly what
    /// <see cref="BookingRefundStates.Resolve(Booking)"/> needs to render
    /// <see cref="BookingRefundState.Failed"/>. The claim id and award are
    /// retained by the release as the durable retry evidence.
    /// </summary>
    public static void DriveConfirmedToFailedRefund(Booking booking, DateTime utcNow)
    {
        Require(
            booking.Status == BookingStatus.Confirmed,
            $"a definitive refund refusal needs a Confirmed booking, not {booking.Status}.");
        long award = Math.Min(booking.RefundableCeilingMinorUnits(), 100L);
        Require(award > 0, "the refund-failure booking carries no refundable amount.");

        booking.ClaimRefundAttempt(Guid.NewGuid(), award, utcNow.AddMinutes(-1));
        booking.ReleaseRefundAttempt(utcNow);
        booking.CancelByCaregiver(
            "Synthetic local fixture refund refusal (no provider movement).", utcNow);
    }

    /// <summary>Confirmation, start and completion are all present and monotonic.</summary>
    public static void RequireCompletedChronology(Booking booking)
    {
        Require(
            booking.Status == BookingStatus.Completed,
            $"the completed booking read back as {booking.Status}, not Completed.");
        DateTime confirmed = booking.ConfirmedOnUtc ?? default;
        DateTime started = booking.StartedOnUtc ?? default;
        DateTime completed = booking.CompletedOnUtc ?? default;
        Require(
            booking.ConfirmedOnUtc is not null
                && booking.StartedOnUtc is not null
                && booking.CompletedOnUtc is not null,
            "the completed booking is missing a confirmation, start or completion instant.");

        Require(
            confirmed <= started && started <= completed,
            $"the completed booking chronology is not monotonic: confirmed={confirmed:o}, "
                + $"started={started:o}, completed={completed:o}.");
    }

    private static Booking NewBooking(DateOnly today, DateTime now, string tag) =>
        Booking.Create(
            FamilyId.New(),
            UserId.New(),
            ElderlyId.New(),
            CaregiverId.New(),
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            today.AddDays(1),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "14 Synthetic Selfcheck Street (in-memory only)",
            $"sanad-portal-fixture:selfcheck:{tag}",
            BookingPriceSnapshot.Calculate(500m, 15m),
            acceptanceDeadlineUtc: now.AddHours(24),
            currentDate: today,
            createdOnUtc: now);

    private static CareHomeBooking NewCareHomeBooking(DateOnly today, DateTime now) =>
        CareHomeBooking.Create(
            CareHomeId.New(),
            UserId.New(),
            FamilyId.New(),
            ElderlyId.New(),
            roomTypeId: Guid.NewGuid(),
            startDate: today.AddDays(2),
            baseAmount: 1000m,
            feeAmount: 100m,
            taxAmount: 50m,
            ruleVersion: 4,
            arabicName: "الجد التجريبي",
            englishName: "W5 Selfcheck",
            age: 75,
            medicalJson: null,
            contactName: "W5 Selfcheck",
            contactPhone: null,
            relationship: "Grandfather",
            careNotes: "sanad-portal-fixture:selfcheck:carehome",
            utcNow: now);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
