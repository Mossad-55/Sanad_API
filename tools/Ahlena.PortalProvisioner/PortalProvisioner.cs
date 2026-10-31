using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
using Ahlena.Modules.Families.Domain.CareRecords;
using Ahlena.Modules.Families.Infrastructure.Persistence;
using Ahlena.Modules.Finance.Application;
using Ahlena.Modules.Finance.Domain;
using Ahlena.Modules.Finance.Infrastructure;
using Ahlena.Modules.Identity.Application.Abstractions.Security;
using Ahlena.Modules.Identity.Domain.Users;
using Ahlena.Modules.Identity.Infrastructure.Persistence;

namespace Ahlena.PortalProvisioner;

/// <summary>
/// Stable ids of the S3a bounded data-only fixture groups for the rehearsal
/// portal report. They double as the <c>--groups</c> values, the manifest keys
/// and the classification table, so the tool, the tests and the brief cannot
/// drift apart. The four seam groups (<see cref="SeamGroups"/>) — the two
/// refund-Failed states and the two duplicate-claim execution states — are
/// provisionable ONLY behind the operator's <c>--seam-deployed</c>
/// declaration that the deployed rehearsal revision runs the
/// <c>FixtureRefundSeam</c> allowlist for their allowlisted transaction ids.
/// </summary>
public static class PortalProvisionGroups
{
    public const string CaregiverRefundAmbiguity = "caregiver-refund-ambiguity";
    public const string CaregiverCompletedBooking = "caregiver-completed-booking";
    public const string CaregiverConfirmedBooking = "caregiver-confirmed-booking";
    public const string CareHomePaidAwaitingDecision = "carehome-paid-awaiting-decision";
    public const string CareHomeRefundAmbiguity = "carehome-refund-ambiguity";
    public const string CareHomeEvidencedExternalRefund = "carehome-evidenced-external-refund";
    public const string CareHomeFacilityDraft = "carehome-facility-draft";
    public const string CareHomeBookingExtension = "carehome-booking-extension";
    public const string CareHomeCheckoutDispute = "carehome-checkout-dispute";
    public const string SubscriptionAndDuplicateClaim = "subscription-and-duplicate-claim";
    public const string FamiliesCareRecords = "families-care-records";
    public const string FamilyPrivateAccess = "family-private-access";
    public const string ComplaintReceived = "complaint-received";
    public const string ComplaintInvestigating = "complaint-investigating";

    // The four seam groups. Their rows are legitimate only because the
    // deployed revision's FixtureRefundSeam intercepts the refund call for
    // their allowlisted transaction ids and returns the scripted outcome —
    // an accidental retry/execute can then never reach the provider.
    public const string CaregiverRefundFailed = "caregiver-refund-failed";
    public const string CareHomeRefundFailed = "carehome-refund-failed";
    public const string DuplicateClaimApprovedAwaitingExecution = "duplicate-claim-approved-awaiting-execution";
    public const string DuplicateClaimAmbiguous = "duplicate-claim-ambiguous";

    /// <summary>Every group the provisioner is allowed to create without the seam declaration, data-only.</summary>
    public static readonly IReadOnlyList<string> DataOnlyGroups =
    [
        CareHomeFacilityDraft,
        CareHomePaidAwaitingDecision,
        CareHomeRefundAmbiguity,
        CareHomeEvidencedExternalRefund,
        CareHomeBookingExtension,
        CareHomeCheckoutDispute,
        CaregiverConfirmedBooking,
        CaregiverCompletedBooking,
        CaregiverRefundAmbiguity,
        SubscriptionAndDuplicateClaim,
        FamiliesCareRecords,
        FamilyPrivateAccess,
        ComplaintReceived,
        ComplaintInvestigating
    ];

    /// <summary>
    /// Seam-gated groups: every row they create carries a provider-call armed
    /// money state (Failed refund, awaiting-execution or ambiguous claim),
    /// which is provisionable ONLY when the operator declares with
    /// <c>--seam-deployed</c> that the deployed revision runs the
    /// <c>FixtureRefundSeam</c> allowlist covering these groups' allowlisted
    /// transaction ids. Without the declaration the provisioner refuses them.
    /// </summary>
    public static readonly IReadOnlyList<string> SeamGroups =
    [
        CaregiverRefundFailed,
        CareHomeRefundFailed,
        DuplicateClaimApprovedAwaitingExecution,
        DuplicateClaimAmbiguous
    ];
}

public sealed record PortalProvisionOptions(
    string RunTag,
    IReadOnlyList<string> Groups,
    DateTime InstantUtc,
    string SyntheticPassword,
    string WorkRoot,
    bool SeamDeployed = false);

/// <summary>One fixture-owned record captured in the run manifest.</summary>
public sealed record PortalProvisionRecord(
    string Kind,
    string Id,
    string State);

/// <summary>One requested group's outcome. Credential-free by construction.</summary>
public sealed record PortalProvisionGroupResult(
    string Group,
    bool Provisioned,
    string Detail,
    IReadOnlyList<PortalProvisionRecord> Records);

public sealed record PortalProvisionReport(
    bool Passed,
    string ManifestPath,
    string RunTag,
    DateTime InstantUtc,
    string FamilyLoginEmail,
    IReadOnlyList<PortalProvisionGroupResult> Groups,
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
public static class PortalProvisioner
{
    public static async Task<PortalProvisionReport> ProvisionAsync(
        IServiceProvider services,
        PortalProvisionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var run = new FixtureRun(services, options, cancellationToken);
        return await run.ExecuteAsync();
    }

    private sealed class FixtureRun(
        IServiceProvider services,
        PortalProvisionOptions options,
        CancellationToken cancellationToken)
    {
        private const string MarkerPrefix = "spf";
        private const string Currency = "EGP";

        private readonly IdentityDbContext _identity = services.GetRequiredService<IdentityDbContext>();
        private readonly FamiliesDbContext _families = services.GetRequiredService<FamiliesDbContext>();
        private readonly CaregiversDbContext _caregivers = services.GetRequiredService<CaregiversDbContext>();
        private readonly CareHomesDbContext _careHomes = services.GetRequiredService<CareHomesDbContext>();
        private readonly FinanceDbContext _finance = services.GetRequiredService<FinanceDbContext>();
        private readonly IPasswordHasher _hasher = services.GetRequiredService<IPasswordHasher>();

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

        // The two seam duplicate-claim groups run on their OWN run-owned
        // capture slots (same isolation reasoning as the shutdown slot: one
        // claim per capture, never a shared refund budget).
        private readonly Dictionary<string, (User Owner, SubscriptionPaymentAttempt Attempt)> _seamClaimSlots =
            new(StringComparer.Ordinal);

        /// <summary>The persisted outcome of the fixture's service-shutdown refund.</summary>
        private sealed record ShutdownRefund(
            Guid RefundCaseId,
            long AwardMinorUnits,
            SubscriptionRefundReason Reason,
            string EntryType);

        public async Task<PortalProvisionReport> ExecuteAsync()
        {
            // Fail-closed group gate: every requested group must be in the
            // approved set. Unknown groups are refused outright; seam groups
            // are refused unless the operator explicitly declared
            // --seam-deployed, proving the deployed revision runs the
            // FixtureRefundSeam allowlist for the groups' transaction ids.
            List<string> refused = new();
            foreach (string group in options.Groups)
            {
                if (PortalProvisionGroups.SeamGroups.Contains(group))
                {
                    if (!options.SeamDeployed)
                    {
                        refused.Add(
                            $"{group} (seam-gated: it is only buildable with --seam-deployed, the operator's declaration "
                                + "that the deployed rehearsal revision runs the FixtureRefundSeam allowlist; without the "
                                + "seam an accidental retry/execute on its rows would reach the provider)");
                    }
                }
                else if (!PortalProvisionGroups.DataOnlyGroups.Contains(group))
                {
                    refused.Add($"{group} (unknown group id)");
                }
            }

            if (refused.Count > 0)
            {
                throw new InvalidOperationException(
                    "provisioning refused before any write; refused groups: " + string.Join("; ", refused));
            }

            IReadOnlyList<string> requested = options.Groups;

            var results = new List<PortalProvisionGroupResult>();
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
                    results.Add(new PortalProvisionGroupResult(
                        group,
                        Provisioned: false,
                        Detail: $"base fixture unavailable: {Safe(exception)}",
                        Records: []));
                }

                return WriteReport(results, unresolved);
            }

            foreach (string group in requested)
            {
                PortalProvisionGroupResult result;
                try
                {
                    result = group switch
                    {
                        PortalProvisionGroups.CaregiverRefundAmbiguity => await CaregiverRefundAmbiguityAsync(),
                        PortalProvisionGroups.CaregiverConfirmedBooking => await CaregiverConfirmedBookingAsync(),
                        PortalProvisionGroups.CaregiverCompletedBooking => await CaregiverCompletedBookingAsync(),
                        PortalProvisionGroups.CareHomePaidAwaitingDecision => await CareHomePaidAwaitingDecisionAsync(),
                        PortalProvisionGroups.CareHomeRefundAmbiguity => await CareHomeRefundAmbiguityAsync(),
                        PortalProvisionGroups.CareHomeEvidencedExternalRefund => await CareHomeEvidencedExternalRefundAsync(),
                        PortalProvisionGroups.CareHomeFacilityDraft => await CareHomeFacilityDraftAsync(),
                        PortalProvisionGroups.CareHomeBookingExtension => await CareHomeBookingExtensionAsync(),
                        PortalProvisionGroups.CareHomeCheckoutDispute => await CareHomeCheckoutDisputeAsync(),
                        PortalProvisionGroups.SubscriptionAndDuplicateClaim => await SubscriptionAndDuplicateClaimAsync(),
                        PortalProvisionGroups.FamiliesCareRecords => await FamiliesCareRecordsAsync(),
                        PortalProvisionGroups.FamilyPrivateAccess => await FamilyPrivateAccessAsync(),
                        PortalProvisionGroups.ComplaintReceived => await ComplaintAsync(investigate: false),
                        PortalProvisionGroups.ComplaintInvestigating => await ComplaintAsync(investigate: true),
                        PortalProvisionGroups.CareHomeRefundFailed => await CareHomeRefundFailedAsync(),
                        PortalProvisionGroups.CaregiverRefundFailed => await CaregiverRefundFailedAsync(),
                        PortalProvisionGroups.DuplicateClaimApprovedAwaitingExecution => await SeamClaimAsync(approved: true),
                        PortalProvisionGroups.DuplicateClaimAmbiguous => await SeamClaimAsync(approved: false),
                        _ => new PortalProvisionGroupResult(
                            group,
                            Provisioned: false,
                            Detail: "unknown group id",
                            Records: [])
                    };
                }
                catch (Exception exception)
                {
                    result = new PortalProvisionGroupResult(
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

                // RUN-WIDE HARD INVARIANT: after EVERY group, re-prove that no
                // fixture-owned money row is provider-call armed. The scan is
                // marker-scoped to this run, evaluated over the persisted rows
                // (not the write facades), and any violation fails the run and
                // stops it before any further group.
                List<string> invariantViolations = await AssertRefundInertnessAsync();
                if (invariantViolations.Count > 0)
                {
                    foreach (string violation in invariantViolations)
                    {
                        unresolved.Add($"refund-inertness-invariant: {violation}");
                    }

                    foreach (string remaining in requested.Skip(results.Count))
                    {
                        unresolved.Add(
                            $"{remaining} (not attempted: the run was stopped by the refund-inertness invariant)");
                    }

                    break;
                }
            }

            return WriteReport(results, unresolved);
        }

        /// <summary>
        /// Reads back every fixture-owned money row (marker-scoped) and hands
        /// the materialized snapshots to <see cref="PortalProvisionInvariants.Evaluate"/>.
        /// A query failure is itself a violation: the run cannot prove
        /// inertness, so it fails closed.
        /// </summary>
        private async Task<List<string>> AssertRefundInertnessAsync()
        {
            try
            {
                List<CareHomeBooking> careHomeBookings = await _careHomes.Bookings.AsNoTracking()
                    .Where(item => item.CareNeedsNotes != null && item.CareNeedsNotes!.StartsWith(_baseMarker))
                    .ToListAsync(cancellationToken);

                List<Booking> familyBookings = await _families.Bookings.AsNoTracking()
                    .Where(item => item.SpecialInstructions != null && item.SpecialInstructions!.StartsWith(_baseMarker))
                    .ToListAsync(cancellationToken);

                List<Guid> attemptIds = [];
                if (_attempt is not null)
                {
                    attemptIds.Add(_attempt.Id);
                }

                if (_shutdownAttempt is not null)
                {
                    attemptIds.Add(_shutdownAttempt.Id);
                }

                foreach ((User _, SubscriptionPaymentAttempt seamAttempt) in _seamClaimSlots.Values)
                {
                    attemptIds.Add(seamAttempt.Id);
                }

                List<FixtureClaimSnapshot> claims = attemptIds.Count == 0
                    ? []
                    : await _finance.DuplicateChargeClaims.AsNoTracking()
                        .Where(item => attemptIds.Contains(item.PaymentAttemptId))
                        .Select(item => new FixtureClaimSnapshot(
                            item.Id, item.State, item.ExecutionState, item.IdempotencyKey))
                        .ToListAsync(cancellationToken);

                // Exemptions exist ONLY under the operator's --seam-deployed
                // declaration, and only ever name the four seam groups' exact
                // fixture markers/keys. PortalProvisionInvariants treats the
                // sets as exact-match whitelists, so a normal group's row can
                // never be exempted.
                SeamRefundExemptions? exemptions = options.SeamDeployed
                    ? new(
                        CareHomeBookingMarkers:
                        [
                            $"{_baseMarker}:{PortalProvisionGroups.CareHomeRefundFailed}"
                        ],
                        FamiliesBookingMarkers:
                        [
                            $"{_baseMarker}:{PortalProvisionGroups.CaregiverRefundFailed}"
                        ],
                        ClaimIdempotencyKeys:
                        [
                            PortalProvisionIdentity.SeamClaimApprovedIdempotencyKey(_runTag),
                            PortalProvisionIdentity.SeamClaimAmbiguousIdempotencyKey(_runTag)
                        ])
                    : null;

                return [.. PortalProvisionInvariants.Evaluate(
                    careHomeBookings,
                    familyBookings,
                    claims,
                    exemptions)];
            }
            catch (Exception exception)
            {
                return
                [
                    "the inertness scan could not be evaluated (the run cannot prove that no fixture row is "
                        + $"provider-call armed): {exception.Message.Replace('\r', ' ').Replace('\n', ' ')}"
                ];
            }
        }

        // ------------------------------------------------------------------
        // Base fixture
        // ------------------------------------------------------------------

        private async Task EnsureBaseFixtureAsync()
        {
            _owner = await EnsureUserAsync(
                "family.owner", "مالك العيلة التجريبي", "Portal Fixture Family Owner", AccountType.Family);
            _caregiverUser = await EnsureUserAsync(
                "caregiver", "الكيرجيفر الطبي التجريبي", "Portal Fixture Medical Caregiver",
                AccountType.MedicalCaregiver);
            _careHomeOwnerUser = await EnsureUserAsync(
                "carehome.owner", "صاحب دار الرعاية التجريبي", "Portal Fixture Care Home Owner",
                AccountType.CareHomeOwner);
            _supportUser = await EnsureUserAsync(
                "support", "دعم العيلة التجريبي", "Portal Fixture Support Admin", AccountType.SupportAdmin);
            _superAdminUser = await EnsureUserAsync(
                "superadmin", "المشرف العام التجريبي", "Portal Fixture Super Admin", AccountType.SuperAdmin);
            User elderlyUser = await EnsureElderlyUserAsync();

            await EnsureFamilyAsync(elderlyUser);
            await EnsureCaregiverAsync();
            await EnsureFacilityAsync();
            await EnsureSubscriptionAsync();
            // No platform charge-rule seeding: every price/fee value the tool
            // writes below is passed explicitly to the domain, so the deployed
            // revision's lookup tables are never a fixture dependency.
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
                FullName.Create(TaggedName("Portal Fixture Grandfather", marker)),
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
            "carehome.owner2" => 9,
            _ => 8
        };

        private async Task EnsureFamilyAsync(User elderlyUser)
        {
            Family? family = await _families.Families
                .FirstOrDefaultAsync(item => item.OwnerUserId == _owner.Id, cancellationToken);

            if (family is null)
            {
                family = Family.Create(_owner.Id, $"Portal Fixture Family {_runTag}");
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
                    FullName.Create("Portal Fixture Grandfather"),
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
                "Portal Fixture Clinic", "Bounded synthetic local fixture caregiver.", _now);
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

        private async Task<PortalProvisionGroupResult> CaregiverRefundAmbiguityAsync()
        {
            Booking booking = await EnsurePaidFamiliesBookingAsync(
                PortalProvisionGroups.CaregiverRefundAmbiguity, confirmed: true);

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
                PortalProvisionGroups.CaregiverRefundAmbiguity,
                $"caregiver refund ambiguity marker raised; persisted refund state={state}",
                [Record("FamiliesBooking", booking.Id.Value, Describe(readBack))]);
        }

        private async Task<PortalProvisionGroupResult> CaregiverCompletedBookingAsync()
        {
            Booking booking = await EnsurePaidFamiliesBookingAsync(
                PortalProvisionGroups.CaregiverCompletedBooking, confirmed: true);

            if (booking.Status == BookingStatus.Confirmed)
            {
                // Coherent chronology: confirmed -> started -> completed, every step
                // after the one it depends on. The shared driver is also what the
                // offline self-check calls, so a chronology regression fails it.
                PortalProvisionDomainChecks.DriveVisitToCompleted(booking, _now);
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

            PortalProvisionDomainChecks.RequireCompletedChronology(readBack);

            return Group(
                PortalProvisionGroups.CaregiverCompletedBooking,
                $"completed paid caregiver booking (confirmed={readBack.ConfirmedOnUtc:o}, started={readBack.StartedOnUtc:o}, completed={readBack.CompletedOnUtc:o}) with a persisted synthetic payout ({payoutRead.Status})",
                [
                    Record("FamiliesBooking", booking.Id.Value, Describe(readBack)),
                    Record("CaregiverPayout", payoutRead.Id.Value, payoutRead.Status.ToString())
                ]);
        }

        private async Task<Booking> EnsurePaidFamiliesBookingAsync(
            string group,
            bool confirmed,
            string? paymobTransactionId = null)
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
                PortalProvisionDomainChecks.DrivePendingToConfirmed(
                    booking, orderId, paymobTransactionId ?? $"{MarkerPrefix}-{_runTag}-txn", _now);
            }
            else
            {
                booking.RecordPaymentIntent(orderId, PaymentMethod.Card, _now.AddMinutes(-4));
                booking.MarkAsPaid(orderId, paymobTransactionId ?? $"{MarkerPrefix}-{_runTag}-txn", _now.AddMinutes(-3));
            }

            _families.Bookings.Add(booking);
            await _families.SaveChangesAsync(cancellationToken);
            return booking;
        }

        // ------------------------------------------------------------------
        // CareHome groups
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> CareHomePaidAwaitingDecisionAsync()
        {
            // Two distinct run-owned slots awaiting the facility decision — one the
            // operator can accept and one they can reject — plus a persisted rejected
            // example so the RefundPending read path is exercised without consuming
            // either live decision slot.
            CareHomeBooking acceptSlot = await EnsureCareHomeBookingAsync(
                $"{PortalProvisionGroups.CareHomePaidAwaitingDecision}:accept", accept: false);
            CareHomeBooking rejectSlot = await EnsureCareHomeBookingAsync(
                $"{PortalProvisionGroups.CareHomePaidAwaitingDecision}:reject", accept: false);
            CareHomeBooking rejectedExample = await EnsureRejectedCareHomeBookingAsync(
                $"{PortalProvisionGroups.CareHomePaidAwaitingDecision}:rejected");

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
                    && rejectedRead.RefundStatus == CareHomeRefundStatus.Pending
                    && rejectedRead.RefundClaimedOnUtc is not null
                    && !rejectedRead.IsRefundSendArmed
                    && !rejectedRead.IsRefundOutcomeAmbiguous,
                $"the persisted rejected example read back as {rejectedRead.Status}/{rejectedRead.RefundStatus} "
                    + $"(claimed={rejectedRead.RefundClaimedOnUtc is not null}, sendArmed={rejectedRead.IsRefundSendArmed}); "
                    + "expected RefundPending/Pending with the refund attempt claim-held and NEVER send-armed.");

            return Group(
                PortalProvisionGroups.CareHomePaidAwaitingDecision,
                "two distinct paid slots awaiting the facility decision (accept and reject) plus a persisted rejected "
                    + "example in RefundPending whose refund attempt is claim-held (not ambiguous, not completed, and "
                    + "never refund-send-armed: a refund retry refuses before any provider call)",
                [
                    Record("CareHomeBookingAcceptSlot", acceptRead.Id.ToString(), Describe(acceptRead)),
                    Record("CareHomeBookingRejectSlot", rejectRead.Id.ToString(), Describe(rejectRead)),
                    Record("CareHomeBookingRejectedExample", rejectedRead.Id.ToString(), Describe(rejectedRead))
                ]);
        }

        private async Task<PortalProvisionGroupResult> CareHomeRefundAmbiguityAsync()
        {
            CareHomeBooking booking = await EnsureCareHomeRefundPendingAsync(
                PortalProvisionGroups.CareHomeRefundAmbiguity);

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
                PortalProvisionGroups.CareHomeRefundAmbiguity,
                $"care home refund ambiguity marker raised (ambiguous={readBack.IsRefundOutcomeAmbiguous})",
                [Record("CareHomeBooking", booking.Id.ToString(), Describe(readBack))]);
        }

        private async Task<PortalProvisionGroupResult> CareHomeEvidencedExternalRefundAsync()
        {
            CareHomeBooking booking = await EnsureCareHomeRefundPendingAsync(
                PortalProvisionGroups.CareHomeEvidencedExternalRefund);

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
                PortalProvisionGroups.CareHomeEvidencedExternalRefund,
                $"care home refund completed from synthetic external evidence (status={readBack.RefundStatus})",
                [Record("CareHomeBooking", booking.Id.ToString(), Describe(readBack))]);
        }

        private async Task<PortalProvisionGroupResult> CareHomeBookingExtensionAsync()
        {
            CareHomeBooking current = await EnsureCareHomeBookingAsync(
                PortalProvisionGroups.CareHomeBookingExtension, accept: true);

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
                PortalProvisionGroups.CareHomeBookingExtension,
                "care home booking extension created off an accepted paid segment",
                [
                    Record("CareHomeBookingCurrent", current.Id.ToString(), Describe(current)),
                    Record("CareHomeBookingExtension", extension.Id.ToString(), Describe(readBack))
                ]);
        }

        private async Task<PortalProvisionGroupResult> CareHomeCheckoutDisputeAsync()
        {
            string marker = $"{_baseMarker}:{PortalProvisionGroups.CareHomeCheckoutDispute}";
            CareHomeBooking? existing = await _careHomes.Bookings
                .FirstOrDefaultAsync(item => item.CareNeedsNotes == marker, cancellationToken);

            CareHomeBooking booking = existing ?? await EnsureCareHomeBookingWithMarkerAsync(
                PortalProvisionGroups.CareHomeCheckoutDispute, accept: true);

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
                PortalProvisionGroups.CareHomeCheckoutDispute,
                $"checkout recorded with no family confirmation and an open dispute ({disputeRead.Status})",
                [
                    Record("CareHomeBooking", booking.Id.ToString(), Describe(readBack)),
                    Record("CareHomeCheckInDispute", disputeRead.Id.ToString(), disputeRead.Status.ToString())
                ]);
        }

        private async Task<PortalProvisionGroupResult> CareHomeFacilityDraftAsync()
        {
            // A SEPARATE fixture-owned care-home owner, deliberately never
            // promoted past Draft: the deployed revision shows the Draft
            // facility read path with zero booking money anywhere near it.
            User draftOwner = await EnsureUserAsync(
                "carehome.owner2", "صاحب دار الرعاية المسودة التجريبي",
                "Portal Fixture Draft Care Home Owner", AccountType.CareHomeOwner);

            CareHomeFacility? facility = await _careHomes.Facilities
                .FirstOrDefaultAsync(item => item.OwnerUserId == draftOwner.Id, cancellationToken);

            if (facility is null)
            {
                facility = CareHomeFacility.CreateDraft(draftOwner.Id, _now.AddMinutes(-10));
                _careHomes.Facilities.Add(facility);
                await _careHomes.SaveChangesAsync(cancellationToken);

                facility.SaveDraft(
                    draftOwner.Id, facility.Version, DraftFacilityProfile(), _now.AddMinutes(-9));
                await _careHomes.SaveChangesAsync(cancellationToken);
            }

            CareHomeFacility readBack = await _careHomes.Facilities.AsNoTracking()
                .FirstAsync(item => item.OwnerUserId == draftOwner.Id, cancellationToken);

            Require(
                readBack.Status == CareHomeStatus.Draft,
                $"the draft facility read back as {readBack.Status}, not Draft.");

            return Group(
                PortalProvisionGroups.CareHomeFacilityDraft,
                $"a separate fixture owner's care-home facility left in Draft (status={readBack.Status})",
                [Record("CareHomeFacility", readBack.Id.ToString(), readBack.Status.ToString())]);
        }

        private async Task<PortalProvisionGroupResult> CaregiverConfirmedBookingAsync()
        {
            Booking booking = await EnsurePaidFamiliesBookingAsync(
                PortalProvisionGroups.CaregiverConfirmedBooking, confirmed: true);

            Booking readBack = await _families.Bookings.AsNoTracking()
                .FirstAsync(item => item.Id == booking.Id, cancellationToken);

            Require(
                readBack.Status == BookingStatus.Confirmed,
                $"the caregiver confirmed-booking fixture read back as {readBack.Status}, not Confirmed.");

            return Group(
                PortalProvisionGroups.CaregiverConfirmedBooking,
                $"a paid caregiver booking in Confirmed awaiting the home visit (total payable {readBack.PriceSnapshot.TotalPayableAmount.ToString(CultureInfo.InvariantCulture)})",
                [Record("FamiliesBooking", readBack.Id.Value, Describe(readBack))]);
        }

        private async Task<PortalProvisionGroupResult> FamiliesCareRecordsAsync()
        {
            string baseMarker = $"{_baseMarker}:{PortalProvisionGroups.FamiliesCareRecords}";

            await EnsureCareRecordAsync(
                $"{baseMarker}:family", () => CareRecord.CreateForFamily(
                    _elderly.Id, _owner.Id, _family.Id,
                    "Synthetic Fixture Hospital", _today.AddDays(-2),
                    $"{baseMarker}:family", _now.AddMinutes(-50)));
            await EnsureCareRecordAsync(
                $"{baseMarker}:caregiver", () => CareRecord.CreateForCaregiver(
                    _elderly.Id, _caregiverUser.Id, _caregiver.Id,
                    "Synthetic Fixture Hospital", _today.AddDays(-1),
                    $"{baseMarker}:caregiver", _now.AddMinutes(-40)));
            await EnsureCareRecordAsync(
                $"{baseMarker}:carehome", () => CareRecord.CreateForCareHome(
                    _elderly.Id, _careHomeOwnerUser.Id, _facility.Id.Value,
                    "Synthetic Fixture Hospital", _today,
                    $"{baseMarker}:carehome", _now.AddMinutes(-30)));

            // Plain StartsWith translates to a parameterized LIKE on
            // PostgreSQL; markers carry no LIKE metacharacters, so the
            // prefix match is exact. (The StringComparison overload does not
            // translate — proven by this proof run.)
            List<CareRecord> records = await _families.CareRecords.AsNoTracking()
                .Where(item => item.Details.StartsWith(baseMarker))
                .ToListAsync(cancellationToken);

            // Only the FAMILY-source record carries AuthorFamilyId (the domain
            // factories null it for the caregiver and care-home sources); all
            // three must belong to the fixture's own dependent.
            Require(
                records.Count == 3
                    && records.Single(item => item.Source == CareRecordSource.Family).AuthorFamilyId == _family.Id
                    && records.All(item => item.ElderlyId == _elderly.Id)
                    && records.Select(item => item.Source).Distinct().Count() == 3,
                $"the care-records group read back {records.Count} marker rows; expected exactly the 3 fixture-owned source records.");

            return Group(
                PortalProvisionGroups.FamiliesCareRecords,
                "one dependent carrying three care records authored from all three locked sources (family, caregiver, care home)",
                [.. records.Select(item => Record(
                    $"CareRecord.{item.Source}", item.Id.ToString(), item.Source.ToString()))]);
        }

        private async Task EnsureCareRecordAsync(string marker, Func<CareRecord> factory)
        {
            bool exists = await _families.CareRecords
                .AnyAsync(item => item.Details == marker, cancellationToken);
            if (exists)
            {
                return;
            }

            CareRecord record = factory();
            _families.CareRecords.Add(record);
            await _families.SaveChangesAsync(cancellationToken);

            bool persisted = await _families.CareRecords
                .AnyAsync(item => item.Details == marker, cancellationToken);
            Require(persisted, $"the care record '{marker}' did not read back after save.");
        }

        private async Task<PortalProvisionGroupResult> FamilyPrivateAccessAsync()
        {
            // Private test access for the reviewer identities: read-only
            // verification that the run-owned login identities exist with the
            // expected account types and that the family holds a current
            // subscription slot. No new rows are written by this group.
            FamilySubscription? subscription = await _families.FamilySubscriptions.AsNoTracking()
                .FirstOrDefaultAsync(item => item.FamilyId == _family.Id && item.IsCurrent, cancellationToken);

            Require(
                subscription is not null,
                "the fixture family has no current subscription; private family test access would be incomplete.");

            return Group(
                PortalProvisionGroups.FamilyPrivateAccess,
                "reviewer login identities verified (family owner with a current subscription, caregiver, care-home owner, support, super admin); no writes",
                [
                    Record("LoginFamilyOwner", FixtureEmail("family.owner"), _family.Name),
                    Record("LoginCaregiver", FixtureEmail("caregiver"), "MedicalCaregiver"),
                    Record("LoginCareHomeOwner", FixtureEmail("carehome.owner"), OwnershipMarker("carehome.owner")),
                    Record("LoginSupport", FixtureEmail("support"), "SupportAdmin"),
                    Record("LoginSuperAdmin", FixtureEmail("superadmin"), "SuperAdmin"),
                    Record("SubscriptionCurrent", subscription!.Id.ToString(), subscription.PlanKey)
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
                englishName: "Portal Fixture Grandfather",
                age: 75,
                medicalJson: null,
                contactName: "Portal Fixture Family Owner",
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

            // REFUND-SEND-ARMED GUARD (review correction): a freshly rejected
            // booking sits in RefundPending/Pending with NO persisted claim —
            // which is EXACTLY the shape CareHomeBooking.IsRefundSendArmed is
            // true for, so POST /admin/care-homes/bookings/{id}/refund/retry
            // would claim-and-send it to the provider on the deployed revision.
            // The fixture therefore persists the refund-attempt claim
            // immediately (the same fence the ambiguity group uses): a claimed
            // attempt is never send-armed and the retry refuses BEFORE any
            // gateway call. The row still displays RefundPending/Pending; the
            // attempt is NOT marked ambiguous and NOT completed.
            if (booking.Status == CareHomeBookingStatus.RefundPending
                && booking.RefundClaimedOnUtc is null
                && !booking.IsRefundOutcomeAmbiguous)
            {
                Require(
                    booking.TryClaimRefund(_now.AddMinutes(-28)),
                    "the rejected example's refund attempt could not be claimed; an unclaimed Pending refund is "
                        + "refund-send-armed and this fixture is not allowed to store it.");
                await _careHomes.SaveChangesAsync(cancellationToken);
            }

            return booking;
        }

        // ------------------------------------------------------------------
        // Seam-gated groups (require the operator's --seam-deployed
        // declaration; see PortalProvisionGroups.SeamGroups)
        // ------------------------------------------------------------------

        /// <summary>
        /// Seam group: a care-home booking whose refund FAILED definitively.
        /// The row drives Reject → TryClaimRefund → MarkRefundFailed, exactly
        /// the sequence a real 4xx refund produces — which leaves the booking
        /// failed and CLAIM-RELEASED, i.e. refund-send-armed and retryable.
        /// It is landable ONLY because the deployed revision's
        /// FixtureRefundSeam intercepts the retry for this booking's own
        /// deterministic transaction id (<c>StableNumber</c> of the marker)
        /// and returns the scripted <c>Rejected</c> outcome. Nothing here
        /// calls a provider at provisioning time.
        /// </summary>
        private async Task<PortalProvisionGroupResult> CareHomeRefundFailedAsync()
        {
            string group = PortalProvisionGroups.CareHomeRefundFailed;
            string marker = $"{_baseMarker}:{group}";
            string allowlistedTransactionId = StableNumber(marker)
                .ToString(CultureInfo.InvariantCulture);

            CareHomeBooking booking = await EnsureCareHomeBookingWithMarkerAsync(group, accept: false);

            if (booking.Status == CareHomeBookingStatus.PaidAwaitingDecision)
            {
                booking.Reject(
                    "Synthetic local fixture rejection (no provider movement).", _now.AddMinutes(-29));
                await _careHomes.SaveChangesAsync(cancellationToken);
            }

            if (booking.Status == CareHomeBookingStatus.RefundPending
                && booking.RefundClaimedOnUtc is null
                && !booking.IsRefundOutcomeAmbiguous)
            {
                Require(
                    booking.TryClaimRefund(_now.AddMinutes(-28)),
                    "the seam care-home Failed fixture could not claim its refund attempt.");
                Require(
                    booking.PaymobTransactionId is not null,
                    "the seam care-home Failed fixture has no persisted provider transaction id; the seam "
                        + "allowlist could never match its refund retry.");
                booking.MarkRefundFailed("Synthetic scripted fixture refund rejection (seam; no provider call).",
                    _now.AddMinutes(-27));
                await _careHomes.SaveChangesAsync(cancellationToken);
            }

            CareHomeBooking readBack = await _careHomes.Bookings.AsNoTracking()
                .FirstAsync(item => item.Id == booking.Id, cancellationToken);
            Require(
                readBack.RefundStatus == CareHomeRefundStatus.Failed && !readBack.IsRefundOutcomeAmbiguous,
                $"the seam care-home Failed fixture read back as {readBack.Status}/{readBack.RefundStatus} "
                    + "(ambiguous=" + readBack.IsRefundOutcomeAmbiguous + "); expected the claim-released Failed shape.");
            Require(
                readBack.PaymobTransactionId?.ToString(CultureInfo.InvariantCulture) == allowlistedTransactionId,
                "the seam care-home Failed fixture's persisted transaction id no longer matches the deterministic "
                    + $"allowlist id {allowlistedTransactionId}.");

            return Group(
                group,
                "care-home refund FAILED (claim released, retryable-by-design) with the refund retry intercepted by the "
                    + $"deployed FixtureRefundSeam: allowlist FixtureRefundSeam:RefundOutcomes['{allowlistedTransactionId}']='Rejected'",
                [Record("CareHomeBooking", readBack.Id.ToString(), Describe(readBack))]);
        }

        /// <summary>
        /// Seam group: a caregiver (Families) booking whose refund FAILED
        /// definitively. Paid → Confirmed → CancelledByFamily with a seam
        /// allowlisted transaction id resolves to exactly the
        /// <c>BookingRefundState.Failed</c> shape whose admin refund-retry
        /// route passes every pre-gateway gate. It is landable ONLY because
        /// the deployed FixtureRefundSeam intercepts that retry for this
        /// booking's allowlisted transaction id. AdminBookingCommands maps a
        /// scripted <c>Rejected</c> outcome to ReleaseRefundAttempt → the
        /// 502/Failed surface, with no provider call.
        /// </summary>
        private async Task<PortalProvisionGroupResult> CaregiverRefundFailedAsync()
        {
            string group = PortalProvisionGroups.CaregiverRefundFailed;
            string allowlistedTransactionId = PortalProvisionIdentity.SeamCaregiverFailedTransactionId(_runTag);

            Booking booking = await EnsurePaidFamiliesBookingAsync(
                group,
                confirmed: true,
                paymobTransactionId: allowlistedTransactionId);

            if (booking.Status == BookingStatus.Confirmed)
            {
                booking.CancelByFamily(
                    "Synthetic local fixture cancellation (no provider movement).", _now.AddMinutes(-15));
                await _families.SaveChangesAsync(cancellationToken);
            }

            Booking readBack = await _families.Bookings.AsNoTracking()
                .FirstAsync(item => item.Id == booking.Id, cancellationToken);
            BookingRefundState state = BookingRefundStates.Resolve(readBack);
            Require(
                state == BookingRefundState.Failed,
                $"the seam caregiver Failed fixture read back as refund state {state}; expected Failed.");
            Require(
                readBack.PaymobTransactionId == allowlistedTransactionId,
                "the seam caregiver Failed fixture's persisted transaction id no longer matches the deterministic "
                    + $"allowlist id {allowlistedTransactionId}.");

            return Group(
                group,
                "caregiver refund FAILED (paid + closed, retryable-by-design) with the admin refund retry intercepted by "
                    + $"the deployed FixtureRefundSeam: allowlist FixtureRefundSeam:RefundOutcomes['{allowlistedTransactionId}']='Rejected'",
                [Record("FamiliesBooking", readBack.Id.Value, Describe(readBack))]);
        }

        /// <summary>
        /// The two seam duplicate-claim groups on TWO DEDICATED capture slots
        /// (one claim per capture — never a shared refund budget):
        /// <list type="bullet">
        /// <item><c>duplicate-claim-approved-awaiting-execution</c>: Approved
        /// + AwaitingExecution, never executed. An accidental Execute click is
        /// intercepted by the deployed seam for the slot's allowlisted charge
        /// transaction id ('Rejected' → 502 refusal recorded, no provider
        /// call).</item>
        /// <item><c>duplicate-claim-ambiguous</c>: Approved, then executed
        /// IN-PROCESS against a scripted gateway stub that returns the
        /// unconfirmable outcome (same shape as
        /// <c>Paymob.RefundOutcomeUnknown</c>) → the claim records Ambiguous
        /// with a Timeout ledger entry. Reconciliation retries of that charge
        /// are intercepted by the seam's 'Unknown' allowlist entry.</item>
        /// </list>
        /// Idempotent: a rerun reuses the stored claim by its idempotency key
        /// and never re-approves, re-executes or re-claims budget.
        /// </summary>
        private async Task<PortalProvisionGroupResult> SeamClaimAsync(bool approved)
        {
            string group = approved
                ? PortalProvisionGroups.DuplicateClaimApprovedAwaitingExecution
                : PortalProvisionGroups.DuplicateClaimAmbiguous;
            string role = approved ? "seam.claim.approved" : "seam.claim.ambiguous";
            string idempotencyKey = approved
                ? PortalProvisionIdentity.SeamClaimApprovedIdempotencyKey(_runTag)
                : PortalProvisionIdentity.SeamClaimAmbiguousIdempotencyKey(_runTag);
            string allowlistedChargeTransactionId = approved
                ? PortalProvisionIdentity.SeamClaimApprovedTransactionId(_runTag)
                : PortalProvisionIdentity.SeamClaimAmbiguousTransactionId(_runTag);
            string marker = $"{_baseMarker}:{group}";

            (User slotOwner, SubscriptionPaymentAttempt attempt) =
                await EnsureSeamClaimSlotAsync(role);

            DuplicateChargeClaim? existing = await _finance.DuplicateChargeClaims.AsNoTracking()
                .FirstOrDefaultAsync(item => item.IdempotencyKey == idempotencyKey, cancellationToken);
            DuplicateChargeClaim claim;
            if (existing is not null)
            {
                claim = existing;
            }
            else
            {
                var command = new SubmitDuplicateChargeClaimCommand(
                    slotOwner.Id,
                    attempt.Id,
                    IdempotencyKey: idempotencyKey,
                    EvidenceReference: $"{MarkerPrefix}-{_runTag}-synthetic-seam-evidence",
                    CustomerReportedOnUtc: _now.AddMinutes(-30));

                // Direct handler construction — the store's refund-gateway seam
                // is a REFUSING stub here, so submission itself can never reach
                // a provider; submission never executes.
                var duplicateClaimStore = new DuplicateChargeClaimStore(
                    _finance,
                    new SubscriptionRefundRecorder(_finance),
                    RefusingRefundGateway.Instance,
                    new ProvisionerClock(_now));
                Result<FamilyDuplicateChargeClaimView> submitted = await
                    new SubmitDuplicateChargeClaimCommandHandler(
                        _families, duplicateClaimStore, new ProvisionerClock(_now))
                        .Handle(command, cancellationToken);
                if (!submitted.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"the seam duplicate-charge submission was refused: {submitted.Error.Code}");
                }

                claim = await _finance.DuplicateChargeClaims.AsNoTracking()
                    .FirstAsync(item => item.Id == submitted.Value.ClaimId, cancellationToken);
            }

            Require(
                claim.State is DuplicateChargeClaimState.Submitted or DuplicateChargeClaimState.Approved,
                $"the seam duplicate-charge claim read back as {claim.State}; expected Submitted or already Approved.");
            Require(
                claim.PaymentAttemptId == attempt.Id,
                "the seam duplicate-charge claim points at a foreign capture; refusing to touch it.");

            if (claim.State != DuplicateChargeClaimState.Approved)
            {
                var approvalStore = new DuplicateChargeClaimStore(
                    _finance,
                    new SubscriptionRefundRecorder(_finance),
                    RefusingRefundGateway.Instance,
                    new ProvisionerClock(_now));
                Result<DuplicateChargeClaimAdminView> approvedResult = await approvalStore.ApproveAsync(
                    claim.Id,
                    _superAdminUser.Id.Value,
                    AccountType.SuperAdmin.ToString(),
                    _now,
                    verifiedBasis: "Synthetic fixture basis: the provider refusal is scripted by the refund seam.",
                    decisionNote: marker,
                    familyReason: null,
                    cancellationToken);
                if (!approvedResult.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"the seam duplicate-charge approval was refused: {approvedResult.Error.Code}");
                }

                claim = await _finance.DuplicateChargeClaims.AsNoTracking()
                    .FirstAsync(item => item.Id == claim.Id, cancellationToken);
            }

            // Rerun gate — the ONLY accepted claim shapes after the approve
            // step, resolved by the pure, unit-proved SeamClaimRerun gate
            // (never an inline ad-hoc Require). The ambiguous group's
            // prior-run end state (Approved/Ambiguous) is SKIPPED below —
            // never re-executed, never refused; the approved group tolerates
            // only its scripted hold state.
            SeamClaimRerunDisposition? disposition =
                SeamClaimRerun.Resolve(approved, claim.State, claim.ExecutionState);
            Require(
                disposition is not null,
                $"the seam duplicate-charge claim ({group}) read back as {claim.State}/{claim.ExecutionState} after approval; "
                    + (approved
                        ? "expected Approved/AwaitingExecution (the scripted never-executed hold state)."
                        : "expected Approved/AwaitingExecution to execute now, or the prior run's Approved/Ambiguous which is skipped (never re-executed)."));

            bool executedThisRun = disposition == SeamClaimRerunDisposition.ExecuteScriptedAmbiguous;
            if (executedThisRun)
            {
                // In-process scripted execution: the gateway stub returns the
                // exact unconfirmable shape the real client produces for a
                // lost refund response, so the claim records Ambiguous with a
                // Timeout ledger entry WITHOUT any provider call.
                var executionStore = new DuplicateChargeClaimStore(
                    _finance,
                    new SubscriptionRefundRecorder(_finance),
                    new ScriptedUnknownRefundGateway(allowlistedChargeTransactionId),
                    new ProvisionerClock(_now));
                Result<DuplicateChargeClaimAdminView> executed = await executionStore.ExecuteAsync(
                    claim.Id,
                    expectedExecutionAttemptId: null,
                    _superAdminUser.Id.Value,
                    AccountType.SuperAdmin.ToString(),
                    _now,
                    cancellationToken);
                if (!executed.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"the scripted ambiguous execution was refused: {executed.Error.Code}");
                }

                claim = await _finance.DuplicateChargeClaims.AsNoTracking()
                    .FirstAsync(item => item.Id == claim.Id, cancellationToken);
            }

            Require(
                SeamClaimRerun.Resolve(approved, claim.State, claim.ExecutionState)
                    == (approved
                        ? SeamClaimRerunDisposition.HoldAsApprovedAwaitingExecution
                        : SeamClaimRerunDisposition.SkipAlreadyExecutedByPriorRun),
                $"the seam duplicate-charge claim ({group}) read back as {claim.State}/{claim.ExecutionState}; "
                    + "expected the scripted state to hold.");

            string detail = approved
                ? "duplicate-charge claim Approved and AwaitingExecution (never executed); an accidental Execute is "
                    + "intercepted by the deployed FixtureRefundSeam: allowlist "
                    + $"FixtureRefundSeam:RefundOutcomes['{allowlistedChargeTransactionId}']='Rejected'"
                : executedThisRun
                    ? "duplicate-charge claim executed IN-PROCESS via a scripted unconfirmable gateway stub: State=Ambiguous, "
                        + "reconciliation-only; any reconciliation-driven refund for its charge transaction is intercepted "
                        + $"by the deployed FixtureRefundSeam: allowlist FixtureRefundSeam:RefundOutcomes['{allowlistedChargeTransactionId}']='Unknown'"
                    : "duplicate-charge claim already in its scripted end state Approved/Ambiguous from a prior run "
                        + "(execution SKIPPED on this run); reconciliation-only, refunds for its charge transaction "
                        + "intercepted by the deployed FixtureRefundSeam: allowlist "
                        + $"FixtureRefundSeam:RefundOutcomes['{allowlistedChargeTransactionId}']='Unknown'";

            return Group(
                group,
                detail,
                [
                    Record("FamilySubscription", attempt.SubscriptionId!.Value.ToString(), "dedicated seam capture slot"),
                    Record("SubscriptionPaymentAttempt", attempt.Id.ToString(), $"Succeeded ({allowlistedChargeTransactionId})"),
                    Record("DuplicateChargeClaim", claim.Id.ToString(), $"{claim.State}/{claim.ExecutionState}")
                ]);
        }

        /// <summary>
        /// A dedicated run-owned subscription capture slot for ONE seam
        /// duplicate-claim group (role-parameterized mirror of
        /// <see cref="EnsureShutdownSubscriptionSlotAsync"/>): its own owner
        /// login, family, premium subscription and a succeeded capture whose
        /// Paymob transaction id is the group's deterministic allowlist id.
        /// </summary>
        private async Task<(User Owner, SubscriptionPaymentAttempt Attempt)> EnsureSeamClaimSlotAsync(string role)
        {
            if (_seamClaimSlots.TryGetValue(role, out (User, SubscriptionPaymentAttempt) ready))
            {
                return ready;
            }

            bool approved = role == "seam.claim.approved";
            string allowlistedChargeTransactionId = approved
                ? PortalProvisionIdentity.SeamClaimApprovedTransactionId(_runTag)
                : PortalProvisionIdentity.SeamClaimAmbiguousTransactionId(_runTag);

            User owner = await EnsureUserAsync(
                role,
                approved ? "مالك العيلة المطالبة المزدوجة التجريبي" : "مالك العيلة المطالبة الملتبسة التجريبي",
                approved ? "Portal Fixture Approved Claim Owner" : "Portal Fixture Ambiguous Claim Owner",
                AccountType.Family);

            Family family = await EnsureFixtureFamilyAsync(
                owner, $"Portal Fixture Seam Claim Family {_runTag} ({role})");

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
                    SubscriptionPlan.PremiumKey, subscription.PlanKey, $"seam claim slot ({role}) plan key");
            }

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
                attempt.RecordPaymobOrder($"{MarkerPrefix}-{_runTag}-{role}-order");
                attempt.TryMarkSucceeded(allowlistedChargeTransactionId, _now.AddDays(-10).AddMinutes(1));
                attempt.LinkSubscription(subscription.Id);
                attempt.CapturePlatformCharge(feeRatePercentage: 1.00m, feeAmount: 0m, ruleVersion: 1);
                _families.SubscriptionPaymentAttempts.Add(attempt);
                await _families.SaveChangesAsync(cancellationToken);
            }
            else
            {
                Require(
                    attempt.PaymobTransactionId == allowlistedChargeTransactionId,
                    $"the seam claim slot ({role}) capture transaction id {attempt.PaymobTransactionId} does not "
                        + $"match the deterministic allowlist id {allowlistedChargeTransactionId}.");
            }

            _seamClaimSlots[role] = (owner, attempt);
            return (owner, attempt);
        }

        // ------------------------------------------------------------------
        // Subscription / duplicate-claim and complaint groups
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> SubscriptionAndDuplicateClaimAsync()
        {
            DuplicateChargeClaim claim = await EnsureDuplicateClaimAsync();
            ShutdownRefund shutdown = await EnsureShutdownRefundAsync();

            return Group(
                PortalProvisionGroups.SubscriptionAndDuplicateClaim,
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

            // Direct handler construction — no MediatR bus, no DI host. The
            // store's refund-gateway seam is a REFUSING stub, so even a bug in
            // this path could never reach a provider; submission never executes.
            var duplicateClaimStore = new DuplicateChargeClaimStore(
                _finance,
                new SubscriptionRefundRecorder(_finance),
                RefusingRefundGateway.Instance,
                new ProvisionerClock(_now));
            Result<FamilyDuplicateChargeClaimView> result = await
                new SubmitDuplicateChargeClaimCommandHandler(
                    _families, duplicateClaimStore, new ProvisionerClock(_now))
                    .Handle(command, cancellationToken);
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

                // Direct handler construction — the shutdown refund records through
            // Finance's SubscriptionRefundRecorder only (ledger + budget claim);
            // it has no gateway path at all, which is why this group is
            // data-only even though a refund is "executed".
            Result<SubscriptionRefundResponse> result = await
                new RefundSubscriptionServiceShutdownCommandHandler(
                    _families, new SubscriptionRefundRecorder(_finance))
                    .Handle(command, cancellationToken);
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
                "shutdown.owner", "مالك العيلة الثاني التجريبي", "Portal Fixture Shutdown Owner",
                AccountType.Family);

            Family family = await EnsureFixtureFamilyAsync(
                _shutdownOwner, $"Portal Fixture Shutdown Family {_runTag}");

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

        private async Task<PortalProvisionGroupResult> ComplaintAsync(bool investigate)
        {
            string group = investigate
                ? PortalProvisionGroups.ComplaintInvestigating
                : PortalProvisionGroups.ComplaintReceived;
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
            "Portal Fixture Hope House",
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

        private static CareHomeProfileDraft DraftFacilityProfile() => new(
            "دار المسودة التجريبية",
            "Portal Fixture Draft House",
            "مسودة بيانات اختبارية (لم تُرسل للمراجعة)",
            "Bounded synthetic draft profile, never submitted",
            "Draft Contact",
            "+201000000001",
            null,
            "Cairo",
            "Nasr City",
            "District 2",
            "Synthetic draft street address",
            "الشروط التجريبية (مسودة)",
            "Synthetic draft admission conditions",
            [],
            []);

        private PortalProvisionGroupResult Group(
            string group, string detail, IReadOnlyList<PortalProvisionRecord> records) =>
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

        private static PortalProvisionRecord Record(string kind, object id, string state) =>
            new(kind, Convert.ToString(id, CultureInfo.InvariantCulture) ?? string.Empty, state);

        private static string Describe(Booking booking) =>
            $"{booking.Status} ambiguous={booking.IsRefundOutcomeAmbiguous} inFlight={booking.IsRefundAttemptInFlight}";

        private static string Describe(CareHomeBooking booking) =>
            $"{booking.Status}/{booking.PaymentStatus} refund={booking.RefundStatus} ambiguous={booking.IsRefundOutcomeAmbiguous}";

        private PortalProvisionReport WriteReport(
            IReadOnlyList<PortalProvisionGroupResult> groups, IReadOnlyList<string> unresolved)
        {
            Directory.CreateDirectory(options.WorkRoot);
            string manifestPath = Path.Combine(
                options.WorkRoot, $"portal-provision-manifest-{_runTag}.json");

            var report = new PortalProvisionReport(
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
public static class PortalProvisionDomainChecks
{
    /// <summary>The result of the in-memory self-check: the checks that ran.</summary>
    public sealed record PortalProvisionDomainCheckReport(bool Passed, IReadOnlyList<string> Checks);

    public static PortalProvisionDomainCheckReport Run(DateTime instantUtc)
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

        return new PortalProvisionDomainCheckReport(Passed: true, Checks: checks);
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
            careNotes: "spf:selfcheck:carehome",
            utcNow: now);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}


/// <summary>
/// The run-wide refund-inertness invariant — the definitive structural proof
/// that NO provisioned fixture row can reach the payment provider on the
/// deployed revision. It is enforced after EVERY provisioned group over every
/// fixture-owned row (marker-scoped), and the run fails closed: any violation
/// stops the run, fails the report and prevents any further group.
/// <para>
/// The armed shapes refused here are exactly the persisted states the
/// admin retry routes send to the gateway on revision <c>4690b5b2</c>:
/// <list type="bullet">
/// <item>Care-home bookings for which <see cref="CareHomeBooking.IsRefundSendArmed"/>
/// is true (RefundPending + unclaimed Pending/Failed + no ambiguity marker —
/// the refund-retry route claims and sends these), including every
/// <c>RefundStatus == Failed</c> row (the bounded retry re-enters the gateway);</item>
/// <item>Families bookings whose persisted refund state resolves to
/// <see cref="BookingRefundState.Failed"/> — the admin refund-retry gates
/// (already-refunded / no-refund-due / ambiguous / in-flight) all pass for
/// exactly that shape and the next gate sends the award to the gateway;</item>
/// <item>Duplicate-charge claims that are <see cref="DuplicateChargeClaimState.Approved"/>
/// or carry ANY execution state (execution calls the refund gateway; awaiting
/// execution is one trigger away; a Failed attempt is retryable; ambiguous
/// degrades to retryable).</item>
/// </list>
/// </para>
/// <para>Replacement states that legitimately hold money rows WITHOUT arming
/// them — claim-held Pending (attempt claimed, no ambiguity), an ambiguity
/// marker folded over a claim, a submitted (never-approved) claim — pass;
/// those are the shapes the retry routes refuse pre-gateway.</para>
/// <para>SEAM EXCEPTION (S3c): when the operator's <c>--seam-deployed</c>
/// declaration is present, exactly the four seam groups' rows — matched by
/// their exact fixture markers / claim idempotency keys, never a prefix — are
/// excused, because the deployed revision's <c>FixtureRefundSeam</c> allowlist
/// intercepts every refund/execution call for those rows' allowlisted
/// transaction ids. Without the declaration the exemption sets are empty and
/// the invariant is unchanged.</para>
/// </summary>
public sealed record FixtureClaimSnapshot(
    Guid Id,
    DuplicateChargeClaimState State,
    DuplicateChargeExecutionState ExecutionState,
    string? IdempotencyKey = null);

/// <summary>
/// The ONLY rows the run-wide refund-inertness invariant may ever excuse, and
/// only while the operator's <c>--seam-deployed</c> declaration is present:
/// the four seam groups' exact fixture markers and claim idempotency keys.
/// Every member is an exact-match whitelist value (ordinal equality — never a
/// prefix or pattern), so a normal group's row can never be exempted: a row
/// whose marker is not EXACTLY one of these values is flagged regardless.
/// </summary>
public sealed record SeamRefundExemptions(
    IReadOnlyCollection<string> CareHomeBookingMarkers,
    IReadOnlyCollection<string> FamiliesBookingMarkers,
    IReadOnlyCollection<string> ClaimIdempotencyKeys)
{
    public static readonly SeamRefundExemptions None = new([], [], []);
}

/// <summary>
/// What the seam duplicate-claim provisioning may do with a readback claim.
/// </summary>
public enum SeamClaimRerunDisposition
{
    /// <summary>
    /// The approved group's ONLY scripted state: Approved/AwaitingExecution,
    /// held forever (never executed).
    /// </summary>
    HoldAsApprovedAwaitingExecution,

    /// <summary>
    /// The ambiguous group's fresh state: Approved/AwaitingExecution — the
    /// scripted unconfirmable execution runs NOW (idempotent: it produces the
    /// same Approved/Ambiguous + Timeout ledger shape).
    /// </summary>
    ExecuteScriptedAmbiguous,

    /// <summary>
    /// The ambiguous group AFTER a prior run: Approved/Ambiguous holds. The
    /// scripted execution is SKIPPED — never re-executed, never refused.
    /// </summary>
    SkipAlreadyExecutedByPriorRun,
}

/// <summary>
/// Pure gate for the ONLY duplicate-claim states the seam provisioning
/// accepts, so the rerun decision is an asserted, unit-proved decision and
/// not an inline Require. Null means the claim readback is OUTSIDE every
/// scripted state (foreign, drifted or corrupted): the provisioner must
/// refuse to touch it.
/// </summary>
public static class SeamClaimRerun
{
    public static SeamClaimRerunDisposition? Resolve(
        bool approved,
        DuplicateChargeClaimState state,
        DuplicateChargeExecutionState executionState)
    {
        if (state != DuplicateChargeClaimState.Approved)
        {
            return null;
        }

        return (approved, executionState) switch
        {
            (true, DuplicateChargeExecutionState.AwaitingExecution)
                => SeamClaimRerunDisposition.HoldAsApprovedAwaitingExecution,

            (false, DuplicateChargeExecutionState.AwaitingExecution)
                => SeamClaimRerunDisposition.ExecuteScriptedAmbiguous,

            // The prior run already executed this claim IN-PROCESS through the
            // scripted unconfirmable gateway; the rerun must skip, not
            // re-execute (which the store would refuse) and not fail.
            (false, DuplicateChargeExecutionState.Ambiguous)
                => SeamClaimRerunDisposition.SkipAlreadyExecutedByPriorRun,

            _ => null,
        };
    }
}

public static class PortalProvisionInvariants
{
    public static IReadOnlyList<string> Evaluate(
        IReadOnlyCollection<CareHomeBooking> careHomeBookings,
        IReadOnlyCollection<Booking> familyBookings,
        IReadOnlyCollection<FixtureClaimSnapshot> duplicateChargeClaims,
        SeamRefundExemptions? seamExemptions = null)
    {
        var violations = new List<string>();
        SeamRefundExemptions exemptions = seamExemptions ?? SeamRefundExemptions.None;

        foreach (CareHomeBooking booking in careHomeBookings)
        {
            // Exact-match exemption: only the seam care-home Failed group's
            // own marker is ever excusable, and only under --seam-deployed.
            bool seamExempt = exemptions.CareHomeBookingMarkers.Contains(
                booking.CareNeedsNotes ?? string.Empty,
                StringComparer.Ordinal);

            if (!seamExempt && booking.RefundStatus == CareHomeRefundStatus.Failed)
            {
                violations.Add(
                    $"care-home booking {booking.Id} is a Failed refund fixture row: a Failed refund is "
                        + "retryable and its retry reaches the provider on the deployed revision.");
            }

            // The armed check is order-independent of the Failed check and
            // keys on the exact computed view the retry route gates on.
            if (!seamExempt && booking.IsRefundSendArmed)
            {
                violations.Add(
                    $"care-home booking {booking.Id} is refund-send-armed "
                        + $"({booking.Status}/{booking.RefundStatus}, claimed={booking.RefundClaimedOnUtc is not null}): "
                        + "POST /admin/care-homes/bookings/{{id}}/refund/retry would claim-and-send it to the provider.");
            }
        }

        foreach (Booking booking in familyBookings)
        {
            // BookingRefundStates.Resolve(booking) folds an attempt-in-flight
            // or ambiguous marker into Pending (the retry refuses those), so
            // == Failed is exactly the persisted shape whose retry passes the
            // pre-gateway gates in AdminBookingCommands.
            BookingRefundState refundState = BookingRefundStates.Resolve(booking);
            if (refundState != BookingRefundState.Failed)
            {
                continue;
            }

            // Exact-match exemption: only the seam caregiver Failed group's
            // own marker is ever excusable, and only under --seam-deployed.
            if (exemptions.FamiliesBookingMarkers.Contains(
                    booking.SpecialInstructions ?? string.Empty,
                    StringComparer.Ordinal))
            {
                continue;
            }

            violations.Add(
                $"families booking {booking.Id} resolves to refund state Failed "
                    + $"({booking.Status}, paid={booking.PaidOnUtc is not null}): the admin refund-retry "
                    + "route would send its outstanding award to the provider.");
        }

        foreach (FixtureClaimSnapshot claim in duplicateChargeClaims)
        {
            // Exact-match exemption: only the two seam duplicate-claim
            // groups' OWN idempotency keys are ever excusable, and only under
            // --seam-deployed. A claim without a key can never match.
            bool seamExempt = !string.IsNullOrEmpty(claim.IdempotencyKey)
                && exemptions.ClaimIdempotencyKeys.Contains(claim.IdempotencyKey, StringComparer.Ordinal);

            if (!seamExempt && claim.State == DuplicateChargeClaimState.Approved)
            {
                violations.Add(
                    $"duplicate-charge claim {claim.Id} is Approved: execution reaches the refund gateway; "
                        + "a fixture claim must stay Submitted (awaiting Support review).");
            }

            if (!seamExempt && claim.ExecutionState is not DuplicateChargeExecutionState.None)
            {
                violations.Add(
                    $"duplicate-charge claim {claim.Id} carries execution state {claim.ExecutionState}: "
                        + "every non-None execution phase either calls the gateway (Executing), is one "
                        + "operator trigger away (AwaitingExecution), or degrades to a retryable send "
                        + "(Failed/Ambiguous); none may be provisioned.");
            }
        }

        return violations;
    }
}


/// <summary>
/// A fixture clock pinned to the operator-passed <c>--instant</c>. Handlers
/// that stamp rows from <see cref="IDateTimeProvider.UtcNow"/> (duplicate-claim
/// submission) inherit the fixture timeline instead of wall-clock time, so the
/// provisioned chronology is reproducible and monotonic per the brief.
/// </summary>
public sealed class ProvisionerClock(DateTime utcNow) : IDateTimeProvider
{
    public DateTime UtcNow { get; } =
        utcNow.Kind == DateTimeKind.Utc ? utcNow : DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
}

/// <summary>
/// The structural inertness seam: every refund the tool could theoretically
/// route through the Finance duplicate-claim store terminates here with an
/// exception instead of at a provider. The tool never references a provider
/// client; this stub is the last line of defense, asserted by a focused test.
/// </summary>
public sealed class RefusingRefundGateway : ISubscriptionRefundGateway
{
    public static readonly RefusingRefundGateway Instance = new();

    private RefusingRefundGateway()
    {
    }

    public Task<SubscriptionRefundGatewayPreparation> PrepareAsync(
        Guid paymentAttemptId,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "portal provisioner inertness guard: no refund preparation may be staged through the provisioning tool (attempt "
                + paymentAttemptId + ").");

    public Task<SubscriptionRefundGatewayResult> RefundAsync(
        string chargeTransactionId,
        string currency,
        long awardMinorUnits,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "portal provisioner inertness guard: no refund may be sent through the provisioning tool (charge "
                + chargeTransactionId + ", " + awardMinorUnits + " minor units).");
}

/// <summary>
/// The seam duplicate-claim "ambiguous" group's IN-PROCESS execution gateway:
/// it returns the exact unconfirmable shape the real client produces for a
/// lost refund response (<c>Paymob.RefundOutcomeUnknown</c>), so
/// <see cref="DuplicateChargeClaimStore.ExecuteAsync"/> records the claim as
/// Ambiguous with a Timeout ledger entry WITHOUT any provider call. Only ever
/// used inside the provisioning tool process; the deployed app keeps its real
/// gateway, whose provider call for this charge transaction id is intercepted
/// by the FixtureRefundSeam allowlist.
/// </summary>
public sealed class ScriptedUnknownRefundGateway(string chargeTransactionId) : ISubscriptionRefundGateway
{
    public Task<SubscriptionRefundGatewayPreparation> PrepareAsync(
        Guid paymentAttemptId,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            SubscriptionRefundGatewayPreparation.WithChargeTransaction(
                ScriptedUnknownRefundGateway.CheckCharge(chargeTransactionId)));

    public Task<SubscriptionRefundGatewayResult> RefundAsync(
        string preparedChargeTransactionId,
        string currency,
        long awardMinorUnits,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            SubscriptionRefundGatewayResult.UnknownBecauseReferenceMissing("Paymob.RefundOutcomeUnknown"));

    private static string CheckCharge(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                "the scripted-unknown seam gateway was constructed without a charge transaction id")
            : value;
}

/// <summary>
/// The demo duplicate-claim "Failed" group's IN-PROCESS execution gateway: it
/// returns the exact definitive-refusal shape the real client produces
/// (<c>Paymob.RefundRejected</c>), so
/// <see cref="DuplicateChargeClaimStore.ExecuteAsync"/> records the claim's
/// execution as Failed WITHOUT any provider call.
/// <para>
/// This stub exists because <see cref="RefusingRefundGateway"/> cannot produce
/// that persisted outcome: its <c>PrepareAsync</c> throws, and
/// <c>PrepareAsync</c> is called OUTSIDE <c>ExecuteAsync</c>'s outcome-mapping
/// try/catch, so it can never be classified into a Failed claim. The deployed
/// app keeps its real gateway, whose provider call for this charge transaction
/// id is intercepted by the FixtureRefundSeam allowlist.
/// </para>
/// </summary>
public sealed class ScriptedRejectingRefundGateway(string chargeTransactionId) : ISubscriptionRefundGateway
{
    public Task<SubscriptionRefundGatewayPreparation> PrepareAsync(
        Guid paymentAttemptId,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            SubscriptionRefundGatewayPreparation.WithChargeTransaction(
                ScriptedRejectingRefundGateway.CheckCharge(chargeTransactionId)));

    public Task<SubscriptionRefundGatewayResult> RefundAsync(
        string preparedChargeTransactionId,
        string currency,
        long awardMinorUnits,
        CancellationToken cancellationToken) =>
        Task.FromResult(new SubscriptionRefundGatewayResult(
            SubscriptionRefundGatewayOutcome.Rejected,
            null,
            "Paymob.RefundRejected"));

    private static string CheckCharge(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                "the scripted-rejecting gateway was constructed without a charge transaction id")
            : value;
}
