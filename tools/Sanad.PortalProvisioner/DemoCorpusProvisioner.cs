using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Infrastructure.Storage;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.Community.Domain.Comments;
using Sanad.Modules.Community.Domain.Uploads;
using Sanad.Modules.Community.Domain.Interactions;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.Modules.Community.Application.Uploads;
using Sanad.Modules.Community.Infrastructure.Persistence;
using Sanad.Modules.Cms.Domain.Help;
using Sanad.Modules.Cms.Domain.Landing;
using Sanad.Modules.Cms.Domain.Legal;
using Sanad.Modules.Cms.Domain.Library;
using Sanad.Modules.Cms.Domain.Wellness;
using Sanad.Modules.Cms.Infrastructure.Persistence;
using Sanad.Modules.Identity.Domain.Support;
using Sanad.Modules.Notifications.Domain.Notifications;
using Sanad.Modules.Notifications.Infrastructure.Persistence;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Chat.Domain.Attachments;
using Sanad.Modules.Chat.Domain.Blocks;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Domain.Messages;
using Sanad.Modules.Chat.Domain.Outbox;
using Sanad.Modules.Chat.Domain.Reports;
using Sanad.Modules.Chat.Infrastructure.Persistence;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Application.Subscriptions;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;
using Sanad.Modules.Finance.Infrastructure;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;

namespace Sanad.PortalProvisioner;

/// <summary>Options for a demo-corpus run (D1 nonce dry run, D2 local, D3 rehearsal).</summary>
public sealed record DemoCorpusOptions(
    string RunTag,
    IReadOnlyList<string> Groups,
    DateTime InstantUtc,
    string SyntheticPassword,
    string WorkRoot,
    string StorageRoot,
    string MediaDirectory,
    string TargetKind,
    bool SeamDeployed,
    bool LocalSanadDbOverride);

/// <summary>
/// The demo-corpus provisioner. Separate from the S3a report-fixture
/// provisioner on purpose: the <c>spf</c> namespace, its groups and its
/// money-state handlers stay byte-identical, and a demo run can never expand
/// the fixture tables.
/// <para>
/// Every group is additive: it loads by marker first, creates only what is
/// missing, reuses existing singletons (plan versions, lookup rows, site
/// settings) and reports a skip instead of overwriting. A second run over the
/// same database therefore creates no row and changes no identity.
/// </para>
/// <para>
/// Media follows the user-upload workflow exactly: the bytes are written by
/// <c>LocalDiskFileStorage</c> into the target's configured upload root, and
/// the row stores the key that writer returned. A media key is only used after
/// the writer produced it, and the run fails if the stored file cannot be
/// opened again through the same abstraction.
/// </para>
/// </summary>
public static class DemoCorpusProvisioner
{
    public static async Task<PortalProvisionReport> ProvisionAsync(
        IServiceProvider services,
        DemoCorpusOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var run = new DemoRun(services, options, cancellationToken);
        return await run.ExecuteAsync();
    }

    private sealed class DemoRun(
        IServiceProvider services,
        DemoCorpusOptions options,
        CancellationToken cancellationToken)
    {
        private readonly DateTime _now = AsUtc(options.InstantUtc);
        private readonly DateOnly _today = DateOnly.FromDateTime(AsUtc(options.InstantUtc));
        private readonly string _runTag = DemoCorpusIdentity.SanitizeTag(options.RunTag);
        private readonly string _marker = DemoCorpusIdentity.BaseMarker(options.RunTag);

        private readonly IdentityDbContext _identity =
            services.GetRequiredService<IdentityDbContext>();
        private readonly FamiliesDbContext _families =
            services.GetRequiredService<FamiliesDbContext>();
        private readonly FinanceDbContext _finance =
            services.GetRequiredService<FinanceDbContext>();
        private readonly CaregiversDbContext _caregivers =
            services.GetRequiredService<CaregiversDbContext>();
        private readonly CareHomesDbContext _careHomes =
            services.GetRequiredService<CareHomesDbContext>();
        private readonly CommunityDbContext _community =
            services.GetRequiredService<CommunityDbContext>();
        private readonly CmsDbContext _cms =
            services.GetRequiredService<CmsDbContext>();
        private readonly NotificationsDbContext _notifications =
            services.GetRequiredService<NotificationsDbContext>();
        private readonly ChatDbContext _chat =
            services.GetRequiredService<ChatDbContext>();
        private readonly DemoCorpusMedia _media = new(options.StorageRoot, options.MediaDirectory);

        private readonly Dictionary<string, User> _accounts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Family> _familyByOwnerRole = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Sanad.Modules.Families.Domain.Elderlies.Elderly> _dependents =
            new(StringComparer.Ordinal);

        private SubscriptionPlanVersion _freePlan = null!;
        private SubscriptionPlanVersion _premiumPlan = null!;
        private PlatformChargeRule? _activeChargeRule;

        /// <summary>Per-run cache of the on-demand capture slots the money groups own.</summary>
        private readonly Dictionary<string, (FamilySubscription Subscription, SubscriptionPaymentAttempt Attempt)>
            _captureSlots = new(StringComparer.Ordinal);

        public async Task<PortalProvisionReport> ExecuteAsync()
        {
            List<string> refused = [];
            foreach (string group in options.Groups)
            {
                if (!DemoCorpusGroups.IsDemoGroup(group))
                {
                    refused.Add($"{group} (not a demo-corpus group)");
                }
                else if (DemoCorpusGroups.SeamGated.Contains(group, StringComparer.Ordinal)
                    && !options.SeamDeployed)
                {
                    refused.Add(
                        $"{group} (seam-gated: it persists a provider-call-armed row and needs --seam-deployed "
                            + "backed by the deployed revision's FixtureRefundSeam allowlist)");
                }
            }

            if (refused.Count > 0)
            {
                throw new InvalidOperationException(
                    "demo provisioning refused before any write; refused groups: "
                        + string.Join("; ", refused));
            }

            var results = new List<PortalProvisionGroupResult>();
            var unresolved = new List<string>();

            foreach (string group in options.Groups)
            {
                PortalProvisionGroupResult result;
                try
                {
                    result = group switch
                    {
                        DemoCorpusGroups.IdentityAccounts => await IdentityAccountsAsync(),
                        DemoCorpusGroups.FamiliesPeople => await FamiliesPeopleAsync(),
                        DemoCorpusGroups.FamiliesSubscriptions => await FamiliesSubscriptionsAsync(),
                        DemoCorpusGroups.CaregiversProfiles => await CaregiversProfilesAsync(),
                        DemoCorpusGroups.CaregiverBookings => await CaregiverBookingsAsync(),
                        DemoCorpusGroups.CaregiverRatings => await CaregiverRatingsAsync(),
                        DemoCorpusGroups.CareHomeFacilities => await CareHomeFacilitiesAsync(),
                        DemoCorpusGroups.CareHomeInventory => await CareHomeInventoryAsync(),
                        DemoCorpusGroups.CareHomeBookings => await CareHomeBookingsAsync(),
                        DemoCorpusGroups.CareHomeVisits => await CareHomeVisitsAsync(),
                        DemoCorpusGroups.Chat => await ChatAsync(),
                        DemoCorpusGroups.Community => await CommunityAsync(),
                        DemoCorpusGroups.CmsLibrary => await CmsLibraryAsync(),
                        DemoCorpusGroups.CmsWellness => await CmsWellnessAsync(),
                        DemoCorpusGroups.CmsLegal => await CmsLegalAsync(),
                        DemoCorpusGroups.CmsSupport => await CmsSupportAsync(),
                        DemoCorpusGroups.CmsFaq => await CmsFaqAsync(),
                        DemoCorpusGroups.CmsLanding => await CmsLandingAsync(),
                        DemoCorpusGroups.FinancePlatform => await FinancePlatformAsync(),
                        DemoCorpusGroups.FinanceClaims => await FinanceClaimsAsync(),
                        DemoCorpusGroups.FinanceComplaints => await FinanceComplaintsAsync(),
                        DemoCorpusGroups.Notifications => await NotificationsAsync(),
                        _ => new PortalProvisionGroupResult(
                            group,
                            Provisioned: false,
                            Detail: "demo group not implemented in this slice; it is reported unresolved rather "
                                + "than partially seeded.",
                            Records: [])
                    };
                }
                catch (Exception exception)
                {
                    // A group that failed between Add and SaveChanges would leave
                    // uncommitted entities tracked in the shared per-module
                    // contexts; discarding them keeps every group independent so a
                    // failure is reported as its own cause instead of resurfacing
                    // as a duplicate-key error inside a later, unrelated group.
                    DiscardPendingWrites();
                    result = new PortalProvisionGroupResult(
                        group,
                        Provisioned: false,
                        Detail: Describe(exception),
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
        // identity-accounts
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> IdentityAccountsAsync()
        {
            var records = new List<PortalProvisionRecord>();

            (string Role, string Arabic, string English, AccountType Type, bool Suspended, bool Elderly)[] plan =
            [
                ("family.owner", "مالك العيلة التجريبي", "Demo Family Owner", AccountType.Family, false, false),
                ("family.member", "فرد العيلة التجريبي", "Demo Family Member", AccountType.Family, false, false),
                ("family.owner2", "مالك العيلة التاني التجريبي", "Demo Second Family Owner", AccountType.Family, false, false),
                ("elderly.1", "الجد التجريبي", "Demo Grandfather", AccountType.Elderly, false, true),
                ("elderly.2", "الجدة التجريبية", "Demo Grandmother", AccountType.Elderly, false, true),
                ("caregiver.1", "الكيرجيفر الأول التجريبي", "Demo Caregiver One", AccountType.MedicalCaregiver, false, false),
                ("caregiver.2", "الكيرجيفر التاني التجريبي", "Demo Caregiver Two", AccountType.MedicalCaregiver, false, false),
                ("caregiver.3", "المرافق الأول التجريبي", "Demo Companion One", AccountType.CompanionCaregiver, false, false),
                ("caregiver.4", "المرافق التاني التجريبي", "Demo Companion Two", AccountType.CompanionCaregiver, false, false),
                ("carehome.owner1", "صاحب دار الرعاية الأول التجريبي", "Demo Care Home Owner One", AccountType.CareHomeOwner, false, false),
                ("carehome.owner2", "صاحب دار الرعاية التاني التجريبي", "Demo Care Home Owner Two", AccountType.CareHomeOwner, false, false),
                ("support", "دعم سند التجريبي", "Demo Support Admin", AccountType.SupportAdmin, false, false),
                ("superadmin", "المشرف العام التجريبي", "Demo Super Admin", AccountType.SuperAdmin, false, false),
                ("content.admin", "مشرف المحتوى التجريبي", "Demo Content Admin", AccountType.ContentAdmin, false, false),
                ("finance.admin", "مشرف المالية التجريبي", "Demo Finance Admin", AccountType.FinanceAdmin, false, false),
                ("suspended.family", "حساب عيلة موقوف تجريبي", "Demo Suspended Family", AccountType.Family, true, false)
            ];

            foreach ((string role, string arabic, string english, AccountType type, bool suspended, bool elderly) in plan)
            {
                (User user, bool created) = await EnsureAccountAsync(
                    role, arabic, english, type, elderlyAccount: elderly, suspend: suspended);
                _accounts[role] = user;
                records.Add(new PortalProvisionRecord(
                    $"User:{role}",
                    user.Id.ToString(),
                    created ? "created" : "reused"));
            }

            return new PortalProvisionGroupResult(
                DemoCorpusGroups.IdentityAccounts,
                Provisioned: true,
                Detail: $"{records.Count} demo accounts ({(records.Count(record => record.State == "created"))} created this run)",
                Records: records);
        }

        private async Task<(User User, bool Created)> EnsureAccountAsync(
            string role,
            string arabicName,
            string englishName,
            AccountType accountType,
            bool elderlyAccount,
            bool suspend)
        {
            string marker = DemoCorpusIdentity.OwnershipMarker(_runTag, role);
            PhoneNumber phone = PhoneNumber.Create(DemoCorpusIdentity.Phone(_runTag, role));
            string email = DemoCorpusIdentity.Email(_runTag, role);

            User? existing = elderlyAccount
                ? await _identity.Users.FirstOrDefaultAsync(
                    user => user.PhoneNumber == phone, cancellationToken)
                : await _identity.Users.FirstOrDefaultAsync(
                    user => user.Email == Email.Create(email), cancellationToken);

            if (existing is not null)
            {
                RequireOwnership(existing, marker, accountType, elderlyAccount ? phone.Value : email);
                return (existing, false);
            }

            User user;
            if (elderlyAccount)
            {
                user = User.CreateElderly(
                    FullName.Create(arabicName),
                    FullName.Create(Tag(englishName, marker)),
                    phone,
                    role.EndsWith('2') ? Gender.Female : Gender.Male,
                    role.EndsWith('2') ? new DateOnly(1952, 6, 20) : new DateOnly(1948, 3, 11),
                    _now);
            }
            else
            {
                user = User.Create(
                    FullName.Create(arabicName),
                    FullName.Create(Tag(englishName, marker)),
                    Email.Create(email),
                    phone);
                user.AddAccount(accountType);
                user.SetInitialPasswordHash(
                    services.GetRequiredService<Sanad.Modules.Identity.Application.Abstractions.Security.IPasswordHasher>()
                        .Hash(options.SyntheticPassword),
                    _now);
                user.VerifyEmail(_now);
                user.VerifyPhone(_now);
                user.Activate(_now);
            }

            if (suspend)
            {
                user.Suspend("demo corpus suspended account", _now);
            }

            _identity.Users.Add(user);
            await _identity.SaveChangesAsync(cancellationToken);
            return (user, true);
        }

        // ------------------------------------------------------------------
        // families-people
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> FamiliesPeopleAsync()
        {
            await EnsureAccountsLoadedAsync();
            var records = new List<PortalProvisionRecord>();

            Family first = await EnsureFamilyAsync("family.owner", "عيلة سند التجريبية الأولى");
            Family second = await EnsureFamilyAsync("family.owner2", "عيلة سند التجريبية التانية");
            records.Add(new PortalProvisionRecord("Family:first", first.Id.ToString(), "ready"));
            records.Add(new PortalProvisionRecord("Family:second", second.Id.ToString(), "ready"));

            await EnsureFamilyMemberAsync(first, _accounts["family.member"], FamilyRole.Editor);
            await EnsureFamilyMemberAsync(first, _accounts["family.owner2"], FamilyRole.Viewer);

            (string Role, string OwnerRole, Family Family, FamilyRelationshipType Relationship, string Arabic, string English, Gender Gender, DateOnly DateOfBirth)[] dependents =
            [
                ("elderly.1", "family.owner", first, FamilyRelationshipType.Grandfather, "الجد التجريبي", "Demo Grandfather", Gender.Male, new DateOnly(1948, 3, 11)),
                ("elderly.2", "family.owner", first, FamilyRelationshipType.Grandmother, "الجدة التجريبية", "Demo Grandmother", Gender.Female, new DateOnly(1952, 6, 20)),
                ("dependent.3", "family.owner2", second, FamilyRelationshipType.Father, "والد العيلة التانية التجريبي", "Demo Second Family Father", Gender.Male, new DateOnly(1945, 1, 9)),
                ("dependent.4", "family.owner2", second, FamilyRelationshipType.Mother, "والدة العيلة التانية التجريبية", "Demo Second Family Mother", Gender.Female, new DateOnly(1947, 11, 2))
            ];

            foreach ((string role, string ownerRole, Family family, FamilyRelationshipType relationship, string arabic, string english, Gender gender, DateOnly dob) in dependents)
            {
                User identity = role switch
                {
                    "elderly.1" => _accounts["elderly.1"],
                    "elderly.2" => _accounts["elderly.2"],
                    _ => (await EnsureAccountAsync(role, arabic, english, AccountType.Elderly, elderlyAccount: true, suspend: false)).User
                };

                var dependent = await EnsureDependentAsync(
                    family, _accounts[ownerRole], identity, relationship, arabic, english, gender, dob);
                _dependents[role] = dependent;
                records.Add(new PortalProvisionRecord($"Elderly:{role}", dependent.Id.ToString(), "ready"));
            }

            await EnsureGrantAsync(_dependents["elderly.1"], _accounts["family.owner"], _accounts["family.member"],
                MedicalAccessGrantType.Full, canView: true, canEdit: true);
            await EnsureGrantAsync(_dependents["elderly.1"], _accounts["family.owner"], _accounts["caregiver.1"],
                MedicalAccessGrantType.Limited, canView: true, canEdit: false);
            await EnsureGrantAsync(_dependents["dependent.3"], _accounts["family.owner2"], _accounts["family.owner2"],
                MedicalAccessGrantType.Full, canView: true, canEdit: true);
            records.Add(new PortalProvisionRecord("MedicalAccessGrant", "3 grants", "ready"));

            return new PortalProvisionGroupResult(
                DemoCorpusGroups.FamiliesPeople,
                Provisioned: true,
                Detail: "2 families, 4 dependents and 3 medical access grants",
                Records: records);
        }

        private async Task EnsureAccountsLoadedAsync()
        {
            if (_accounts.Count > 0)
            {
                return;
            }

            await IdentityAccountsAsync();
        }

        private async Task<Family> EnsureFamilyAsync(string ownerRole, string name)
        {
            if (_familyByOwnerRole.TryGetValue(ownerRole, out Family? cached))
            {
                return cached;
            }

            UserId ownerId = _accounts[ownerRole].Id;
            Family? family = await _families.Families
                .FirstOrDefaultAsync(item => item.OwnerUserId == ownerId, cancellationToken);

            if (family is null)
            {
                family = Family.Create(ownerId, $"{name} [{_marker}]");
                _families.Families.Add(family);
                await _families.SaveChangesAsync(cancellationToken);
            }

            _familyByOwnerRole[ownerRole] = family;
            return family;
        }

        private async Task EnsureFamilyMemberAsync(Family family, User member, FamilyRole role)
        {
            if (family.Members.Any(item => item.Id == member.Id))
            {
                return;
            }

            family.AddMember(FamilyMember.Create(
                member.Id,
                family.OwnerUserId,
                FamilyRelationshipType.Other,
                role));
            await _families.SaveChangesAsync(cancellationToken);
        }

        private async Task<Sanad.Modules.Families.Domain.Elderlies.Elderly> EnsureDependentAsync(
            Family family,
            User grantedBy,
            User identity,
            FamilyRelationshipType relationship,
            string arabicName,
            string englishName,
            Gender gender,
            DateOnly dateOfBirth)
        {
            var existing = await _families.Elderlies.FirstOrDefaultAsync(
                item => item.FamilyId == family.Id && item.IdentityUserId == identity.Id,
                cancellationToken);

            if (existing is not null)
            {
                return existing;
            }

            var dependent = Sanad.Modules.Families.Domain.Elderlies.Elderly.Create(
                grantedBy.Id,
                identity.Id,
                family.Id,
                relationship,
                FullName.Create(arabicName),
                FullName.Create(Tag(englishName, DemoCorpusIdentity.OwnershipMarker(_runTag, "dependent"))),
                gender,
                dateOfBirth,
                _today);
            _families.Elderlies.Add(dependent);
            await _families.SaveChangesAsync(cancellationToken);
            return dependent;
        }

        private async Task EnsureGrantAsync(
            Sanad.Modules.Families.Domain.Elderlies.Elderly dependent,
            User grantedBy,
            User grantee,
            MedicalAccessGrantType grantType,
            bool canView,
            bool canEdit)
        {
            if (grantedBy.Id == grantee.Id)
            {
                return;
            }

            var existing = await _families.MedicalAccessGrants.FirstOrDefaultAsync(
                item => item.DependentId == dependent.Id
                    && item.GranteeUserId == grantee.Id
                    && item.RevokedOnUtc == null,
                cancellationToken);

            if (existing is not null)
            {
                return;
            }

            _families.MedicalAccessGrants.Add(
                Sanad.Modules.Families.Domain.Elderlies.MedicalAccessGrant.Create(
                    dependent.Id,
                    grantedBy.Id,
                    grantee.Id,
                    grantType,
                    canView,
                    canEdit,
                    canShareWithOthers: false,
                    _now,
                    expiresOnUtc: null,
                    notes: $"demo grant [{_marker}]"));
            await _families.SaveChangesAsync(cancellationToken);
        }

        // ------------------------------------------------------------------
        // families-subscriptions
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> FamiliesSubscriptionsAsync()
        {
            await EnsureAccountsLoadedAsync();
            await EnsureDependentFixtureAsync();

            _freePlan = await EnsurePlanVersionAsync(
                SubscriptionPlan.FreeKey, SubscriptionPlan.Free, isPublished: true, isAvailableForNewSales: true);
            _premiumPlan = await EnsurePlanVersionAsync(
                SubscriptionPlan.PremiumKey, SubscriptionPlan.Premium, isPublished: true, isAvailableForNewSales: false);

            Family first = _familyByOwnerRole["family.owner"];
            Family second = _familyByOwnerRole["family.owner2"];

            var records = new List<PortalProvisionRecord>();

            // Family 1: one current premium subscription (with a pending
            // downgrade to free) plus two historical rows (expired, cancelled).
            FamilySubscription current = await EnsureSubscriptionAsync(
                first, _premiumPlan, startOffsetDays: -20, slot: "current-premium");
            if (current.PendingDowngrade is null)
            {
                current.ReplacePendingDowngrade(_freePlan);
                await _families.SaveChangesAsync(cancellationToken);
            }

            records.Add(new PortalProvisionRecord("Subscription:current-premium", current.Id.ToString(), "ready"));

            FamilySubscription expired = await EnsureSubscriptionAsync(
                first, _premiumPlan, startOffsetDays: -120, slot: "historical-expired", current: false);
            records.Add(new PortalProvisionRecord("Subscription:historical-expired", expired.Id.ToString(), "ready"));

            FamilySubscription cancelled = await EnsureSubscriptionAsync(
                first, _premiumPlan, startOffsetDays: -200, slot: "historical-cancelled",
                current: false, cancelRenewalOnUtc: _now.AddDays(-190));
            records.Add(new PortalProvisionRecord("Subscription:historical-cancelled", cancelled.Id.ToString(), "ready"));

            // Family 2: the free plan current, plus two historical captures.
            FamilySubscription free = await EnsureSubscriptionAsync(
                second, _freePlan, startOffsetDays: -15, slot: "free-current");
            records.Add(new PortalProvisionRecord("Subscription:free-current", free.Id.ToString(), "ready"));

            FamilySubscription secondExpired = await EnsureSubscriptionAsync(
                second, _premiumPlan, startOffsetDays: -300, slot: "second-expired", current: false);
            records.Add(new PortalProvisionRecord("Subscription:second-expired", secondExpired.Id.ToString(), "ready"));

            FamilySubscription familyFree = await EnsureSubscriptionAsync(
                second, _freePlan, startOffsetDays: -420, slot: "second-free-history", current: false);
            records.Add(new PortalProvisionRecord("Subscription:second-free-history", familyFree.Id.ToString(), "ready"));

            return new PortalProvisionGroupResult(
                DemoCorpusGroups.FamiliesSubscriptions,
                Provisioned: true,
                Detail: "6 subscriptions: current premium (downgrade pending), 2 expired, 1 cancelled, free current, 1 free history",
                Records: records);
        }

        private async Task EnsureDependentFixtureAsync()
        {
            if (_familyByOwnerRole.ContainsKey("family.owner")
                && _familyByOwnerRole.ContainsKey("family.owner2"))
            {
                return;
            }

            await FamiliesPeopleAsync();
        }

        private async Task<FamilySubscription> EnsureSubscriptionAsync(
            Family family,
            SubscriptionPlanVersion plan,
            int startOffsetDays,
            string slot,
            bool current = true,
            DateTime? cancelRenewalOnUtc = null)
        {
            DateTime created = _now.AddDays(startOffsetDays);
            string marker = DemoCorpusIdentity.RecordKey(_runTag, DemoCorpusGroups.FamiliesSubscriptions, slot);

            FamilySubscription? existing = await _families.FamilySubscriptions
                .FirstOrDefaultAsync(
                    item => item.FamilyId == family.Id
                        && item.PlanKey == plan.Key
                        && item.PlanVersion == plan.Version
                        && item.CreatedOnUtc == created,
                    cancellationToken);

            if (existing is not null)
            {
                return existing;
            }

            FamilySubscription subscription = FamilySubscription.Create(family.Id, plan, created);
            subscription.SetCurrentPeriodSettlement(plan.Price, taxRatePercentage: 0m);

            // A family may hold exactly one current subscription: the partial
            // unique index ux_family_subscriptions_current is enforced on every
            // save. The renewal-cancellation transition is only legal while
            // IsCurrent is true, so it is applied to the freshly created
            // instance and the row is retired BEFORE it is ever inserted: the
            // INSERT already carries is_current = false, so a historical row can
            // never collide with the family's live subscription and a replay can
            // add it even while the live row is present.
            if (cancelRenewalOnUtc is not null && subscription.AutoRenewEnabled)
            {
                subscription.CancelRenewal(cancelRenewalOnUtc);
            }

            if (!current)
            {
                subscription.MarkNotCurrent();
            }

            _families.FamilySubscriptions.Add(subscription);
            await _families.SaveChangesAsync(cancellationToken);
            _ = marker;
            return subscription;
        }

        private async Task<SubscriptionPlanVersion> EnsurePlanVersionAsync(
            string key,
            SubscriptionPlan plan,
            bool isPublished,
            bool isAvailableForNewSales)
        {
            SubscriptionPlanVersion? existing = await _families.SubscriptionPlanVersions
                .Include(item => item.Benefits)
                .AsSplitQuery()
                .FirstOrDefaultAsync(
                    item => item.Key == key && item.Version == plan.Version,
                    cancellationToken);

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
        // caregivers-profiles
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> CaregiversProfilesAsync()
        {
            await EnsureAccountsLoadedAsync();
            var lookups = await EnsureCaregiverLookupsAsync();
            var records = new List<PortalProvisionRecord>();

            (string Role, CaregiverType Type, string State, string Arabic, string English)[] plan =
            [
                ("caregiver.1", CaregiverType.Medical, "approved-geriatric", "الكيرجيفر الطبي الأول التجريبي", "Demo Geriatric Caregiver"),
                ("caregiver.2", CaregiverType.Medical, "approved-unavailable", "الكيرجيفر الطبي التاني التجريبي", "Demo Nursing Caregiver"),
                ("caregiver.3", CaregiverType.Companion, "approved-companion", "المرافق التجريبي", "Demo Companion Caregiver"),
                ("caregiver.4", CaregiverType.Companion, "pending-review", "المرافق التجريبي بانتظار المراجعة", "Demo Pending Companion"),
                ("caregiver.5", CaregiverType.Medical, "rejected", "الكيرجيفر الطبي المرفوض التجريبي", "Demo Rejected Caregiver"),
                ("caregiver.6", CaregiverType.Medical, "suspended", "الكيرجيفر الطبي الموقوف التجريبي", "Demo Suspended Caregiver")
            ];

            foreach ((string role, CaregiverType type, string state, string arabic, string english) in plan)
            {
                AccountType accountType = type == CaregiverType.Medical
                    ? AccountType.MedicalCaregiver
                    : AccountType.CompanionCaregiver;
                (User user, _) = await EnsureAccountAsync(
                    role, arabic, english, accountType, elderlyAccount: false, suspend: false);

                Caregiver caregiver = await EnsureCaregiverAsync(user, type, state, arabic, english, lookups);
                records.Add(new PortalProvisionRecord(
                    $"Caregiver:{role}",
                    caregiver.Id.ToString(),
                    caregiver.Status.ToString()));
            }

            return new PortalProvisionGroupResult(
                DemoCorpusGroups.CaregiversProfiles,
                Provisioned: true,
                Detail: "6 caregiver profiles: 3 approved (2 medical, 1 companion), 1 pending review, 1 rejected, 1 suspended",
                Records: records);
        }

        private sealed record CaregiverLookups(
            Area Area,
            Language Language,
            Service MedicalService,
            Service CompanionService,
            Specialization Geriatric,
            Specialization Nursing,
            Specialization Companion,
            ProfessionalTitle Title,
            AcademicDegree Degree);

        private async Task<CaregiverLookups> EnsureCaregiverLookupsAsync()
        {
            Governorate governorate = await EnsureLookupAsync(
                _caregivers.Governorates, g => g.EnglishName == "Cairo",
                () => Governorate.Create("القاهرة", "Cairo"));
            City city = await EnsureLookupAsync(
                _caregivers.Cities, c => c.EnglishName == "Nasr City" && c.GovernorateId == governorate.Id,
                () => City.Create(governorate.Id, "مدينة نصر", "Nasr City"));
            Area area = await EnsureLookupAsync(
                _caregivers.Areas, a => a.EnglishName == "Nasr City Area" && a.CityId == city.Id,
                () => Area.Create(city.Id, "حي مدينة نصر", "Nasr City Area"));
            Language language = await EnsureLookupAsync(
                _caregivers.Languages, l => l.EnglishName == "Arabic",
                () => Language.Create("ar", "العربية", "Arabic"));
            Service medical = await EnsureLookupAsync(
                _caregivers.Services, s => s.EnglishName == "Home Medical Care",
                () => Service.Create("رعاية طبية منزلية", "Home Medical Care", "test-data/icons/medical.png", CaregiverType.Medical, true));
            Service companion = await EnsureLookupAsync(
                _caregivers.Services, s => s.EnglishName == "Companion Care",
                () => Service.Create("رعاية مرافق لكبار السن", "Companion Care", "test-data/icons/companion.png", CaregiverType.Companion, true));
            Specialization geriatric = await EnsureLookupAsync(
                _caregivers.Specializations, s => s.EnglishName == "Geriatric Care",
                () => Specialization.Create("رعاية كبار السن", "Geriatric Care", true, CaregiverType.Medical));
            Specialization nursing = await EnsureLookupAsync(
                _caregivers.Specializations, s => s.EnglishName == "Nursing Care",
                () => Specialization.Create("تمريض منزلي", "Nursing Care", true, CaregiverType.Medical));
            Specialization companionSpecialization = await EnsureLookupAsync(
                _caregivers.Specializations, s => s.EnglishName == "Elderly Companionship",
                () => Specialization.Create("مرافقة كبار السن", "Elderly Companionship", true, CaregiverType.Companion));
            ProfessionalTitle title = await EnsureLookupAsync(
                _caregivers.ProfessionalTitles, t => t.EnglishName == "Senior Nurse",
                () => ProfessionalTitle.Create("ممرض أول", "Senior Nurse", true));
            AcademicDegree degree = await EnsureLookupAsync(
                _caregivers.AcademicDegrees, d => d.EnglishName == "BSc Nursing",
                () => BachelorDegree());

            return new CaregiverLookups(
                area, language, medical, companion, geriatric, nursing, companionSpecialization, title, degree);
        }

        private static AcademicDegree BachelorDegree() =>
            AcademicDegree.Create("بكالوريوس تمريض", "BSc Nursing", true);

        private async Task<Caregiver> EnsureCaregiverAsync(
            User user,
            CaregiverType type,
            string state,
            string arabicName,
            string englishName,
            CaregiverLookups lookups)
        {
            Caregiver? existing = await _caregivers.Caregivers
                .AsSplitQuery()
                .FirstOrDefaultAsync(item => item.UserId == user.Id, cancellationToken);

            if (existing is not null)
            {
                return existing;
            }

            Caregiver caregiver = Caregiver.Create(user.Id, type);
            caregiver.SelectService(type == CaregiverType.Medical ? lookups.MedicalService : lookups.CompanionService);
            caregiver.SelectLanguage(lookups.Language);
            caregiver.SelectArea(lookups.Area);

            if (type == CaregiverType.Medical)
            {
                caregiver.UpdateMedicalProfile(
                    lookups.Title,
                    yearsOfExperience: state == "rejected" ? 1 : 7,
                    state == "approved-geriatric" ? lookups.Geriatric : lookups.Nursing,
                    lookups.Degree,
                    "Demo Clinic",
                    $"SANAD DEMO profile [{_marker}]",
                    _now);
                caregiver.UpdateMedicalPricing(450m, 800m, 1200m, 1600m);

                // Every Medical caregiver must declare at least one Medical
                // availability entry before it can submit OR be approved
                // (ValidateSubmissionReadiness / ValidateActivationReadiness), so
                // the weekly schedule is attached to every Medical role. The
                // home-visit window must sit on a day with NO shift: the domain
                // refuses a day that combines Home Visit windows with a Medical
                // shift.
                caregiver.AddMedicalShift(DayOfWeek.Sunday, MedicalShiftType.EightHourMorning);
                caregiver.AddMedicalShift(DayOfWeek.Monday, MedicalShiftType.EightHourMorning);
                caregiver.AddMedicalShift(DayOfWeek.Tuesday, MedicalShiftType.TwelveHourDay);
                caregiver.AddMedicalHomeVisitWindow(DayOfWeek.Thursday, new TimeOnly(9, 0), new TimeOnly(17, 0));

                // Professional certificates exist only on Medical caregivers.
                caregiver.AddCertificate(
                    CaregiverCertificateType.PracticeLicense,
                    $"private/caregiver-documents/demo-{_runTag}-practice-license.pdf",
                    _today.AddYears(1),
                    _today);
                caregiver.AddCertificate(
                    CaregiverCertificateType.GraduationCertificate,
                    $"private/caregiver-documents/demo-{_runTag}-graduation.pdf",
                    null,
                    _today);
            }
            else
            {
                caregiver.UpdateCompanionProfile(
                    yearsOfExperience: 5,
                    lookups.Companion,
                    $"SANAD DEMO companion profile [{_marker}]",
                    _now);
                caregiver.UpdateCompanionPricing(300m, 500m, 900m);
                // An EightHourDay window must span exactly 8 hours.
                caregiver.AddCompanionAvailabilityWindow(CompanionBookingType.EightHourDay, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(17, 0));
                caregiver.AddCompanionAvailabilityWindow(CompanionBookingType.EightHourDay, DayOfWeek.Wednesday, new TimeOnly(9, 0), new TimeOnly(17, 0));
            }

            caregiver.SubmitForReview(_now, _today);

            switch (state)
            {
                case "pending-review":
                    break;
                case "rejected":
                    caregiver.RejectApplication("SANAD DEMO rejected application", _now);
                    break;
                default:
                    foreach (CaregiverCertificate certificate in caregiver.Certificates)
                    {
                        caregiver.VerifyCertificate(certificate.Id);
                    }

                    caregiver.Approve(_now, _today);
                    if (state == "suspended")
                    {
                        caregiver.Suspend("SANAD DEMO suspension", _now);
                    }
                    else if (state == "approved-unavailable")
                    {
                        // Approved but deliberately NOT marked available: the
                        // availability endpoint reports empty intervals whenever
                        // Availability != Available, which is exactly this state.
                    }
                    else
                    {
                        caregiver.BecomeAvailable(_today);
                    }

                    break;
            }

            _caregivers.Caregivers.Add(caregiver);
            await _caregivers.SaveChangesAsync(cancellationToken);
            return caregiver;
        }

        // ------------------------------------------------------------------
        // care-home-facilities
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> CareHomeFacilitiesAsync()
        {
            await EnsureAccountsLoadedAsync();
            User reviewer = _accounts["superadmin"];
            var records = new List<PortalProvisionRecord>();

            (string Role, string State, string Arabic, string English)[] plan =
            [
                ("carehome.owner1", "approved", "دار رعاية سند المعتمدة التجريبية", "Demo Approved Care Home"),
                ("carehome.owner2", "pending", "دار رعاية سند بانتظار المراجعة التجريبية", "Demo Pending Care Home"),
                ("carehome.owner3", "draft", "دار رعاية سند المسودة التجريبية", "Demo Draft Care Home"),
                ("carehome.owner4", "corrections", "دار رعاية سند تحت التصحيح التجريبية", "Demo Corrections Care Home"),
                ("carehome.owner5", "suspended", "دار رعاية سند الموقوفة التجريبية", "Demo Suspended Care Home")
            ];

            foreach ((string role, string state, string arabic, string english) in plan)
            {
                (User owner, _) = await EnsureAccountAsync(
                    role, arabic, english, AccountType.CareHomeOwner, elderlyAccount: false, suspend: false);

                CareHomeFacility facility = await EnsureFacilityAsync(owner, reviewer, state, arabic, english);
                records.Add(new PortalProvisionRecord(
                    $"CareHomeFacility:{role}",
                    facility.Id.ToString(),
                    facility.Status.ToString()));
            }

            return new PortalProvisionGroupResult(
                DemoCorpusGroups.CareHomeFacilities,
                Provisioned: true,
                Detail: "5 facilities (one per owner, as the portal assumes): approved, pending review, draft, corrections requested, suspended",
                Records: records);
        }

        private async Task<CareHomeFacility> EnsureFacilityAsync(
            User owner,
            User reviewer,
            string state,
            string arabicName,
            string englishName)
        {
            CareHomeFacility? existing = await _careHomes.Facilities
                .Include(item => item.Revisions)
                .Include(item => item.Documents)
                .FirstOrDefaultAsync(item => item.OwnerUserId == owner.Id, cancellationToken);

            if (existing is not null)
            {
                return existing;
            }

            CareHomeFacility facility = CareHomeFacility.CreateDraft(owner.Id, _now);
            _careHomes.Facilities.Add(facility);
            await _careHomes.SaveChangesAsync(cancellationToken);

            facility.SaveDraft(owner.Id, facility.Version, FacilityProfile(arabicName, englishName), _now);
            await _careHomes.SaveChangesAsync(cancellationToken);

            if (state == "draft")
            {
                return facility;
            }

            // The real upload path for facility media: the private writer, the
            // care-home-media folder, jpeg only - then the domain row stores the
            // key the writer returned.
            CareHomeProfileRevision revision = facility.PrepareProfileMediaRevision(owner.Id, facility.Version, _now);
            string coverKey = await _media.ImagePrivateKeyAsync("care-home-media", maxBytes: 5_242_880);
            _careHomes.ProfileMedia.Add(CareHomeProfileMedia.Create(
                revision.Id,
                CareHomeProfileMediaKind.Cover,
                0,
                coverKey,
                "image/jpeg",
                _media.ImageLength,
                _now));
            facility.RecordProfileMediaChange(owner.Id, facility.Version, _now);
            await _careHomes.SaveChangesAsync(cancellationToken);

            revision = facility.PrepareProfileMediaRevision(owner.Id, facility.Version, _now);
            string galleryKey = await _media.ImagePrivateKeyAsync("care-home-media", maxBytes: 5_242_880);
            _careHomes.ProfileMedia.Add(CareHomeProfileMedia.Create(
                revision.Id,
                CareHomeProfileMediaKind.Gallery,
                0,
                galleryKey,
                "image/jpeg",
                _media.ImageLength,
                _now));
            facility.RecordProfileMediaChange(owner.Id, facility.Version, _now);
            await _careHomes.SaveChangesAsync(cancellationToken);

            // Documents: written through the private writer as the document
            // uploader does, then verified by the admin identity.
            string documentKey = await _media.DocumentKeyAsync("care-home-documents");
            foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            {
                facility.UploadDocument(
                    owner.Id, type, documentKey, "application/pdf", _media.DocumentLength, null, _now);
            }

            await _careHomes.SaveChangesAsync(cancellationToken);

            facility.Submit(owner.Id, facility.Version, _now);
            await _careHomes.SaveChangesAsync(cancellationToken);

            if (state == "pending")
            {
                return facility;
            }

            foreach (CareHomeDocument document in facility.Documents.ToArray())
            {
                facility.VerifyDocument(reviewer.Id, facility.Version, document.Id, null, true, _now);
            }

            await _careHomes.SaveChangesAsync(cancellationToken);

            facility.Review(
                reviewer.Id,
                facility.Version,
                state == "corrections" ? CareHomeReviewAction.CorrectionsRequested : CareHomeReviewAction.Approved,
                state == "corrections" ? "SANAD DEMO corrections requested" : null,
                _now,
                _today);
            await _careHomes.SaveChangesAsync(cancellationToken);

            if (state == "suspended")
            {
                facility.Review(
                    reviewer.Id,
                    facility.Version,
                    CareHomeReviewAction.Suspended,
                    "SANAD DEMO suspension",
                    _now,
                    _today);
                await _careHomes.SaveChangesAsync(cancellationToken);
            }

            return facility;
        }

        private CareHomeProfileDraft FacilityProfile(string arabicName, string englishName) => new(
            arabicName,
            $"{englishName} [{_marker}]",
            "دار رعاية تجريبية تقدّم رعاية يومية وإقامة دائمة لكبار السن مع متابعة تمريض على مدار الساعة ووجبات متوازنة.",
            "SANAD DEMO care and accommodation profile",
            "Demo Contact",
            "+201000000000",
            null,
            "Cairo",
            "Nasr City",
            "District 1",
            "SANAD DEMO street address",
            "القبول بعد زيارة تعارف قصيرة ومراجعة التقرير الطبي الأخير؛ لا نقبل الحالات التي تحتاج عناية مركزة.",
            "SANAD DEMO admission conditions",
            [],
            []);


        private async Task<T> EnsureLookupAsync<T>(
            DbSet<T> set, System.Linq.Expressions.Expression<Func<T, bool>> predicate, Func<T> factory)
            where T : class
        {
            T? existing = await set.FirstOrDefaultAsync(predicate, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            T created = factory();
            set.Add(created);
            await SaveLookupAsync();
            return created;
        }

        private async Task SaveLookupAsync() => await _caregivers.SaveChangesAsync(cancellationToken);

        // ------------------------------------------------------------------
        // care-home-inventory
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> CareHomeInventoryAsync()
        {
            await EnsureAccountsLoadedAsync();

            CareHomeFacility facility = await _careHomes.Facilities
                .FirstOrDefaultAsync(
                    item => item.OwnerUserId == _accounts["carehome.owner1"].Id,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "the approved demo facility is missing; provision care-home-facilities first.");

            var records = new List<PortalProvisionRecord>();

            (string English, string Arabic, decimal Price, CareHomeAllocationMode Mode)[] types =
            [
                ("Shared Room", "غرفة مشتركة", 3500m, CareHomeAllocationMode.Shared),
                ("Private Room", "غرفة خاصة", 6500m, CareHomeAllocationMode.Private),
                ("Suite", "جناح خاص", 9500m, CareHomeAllocationMode.Suite)
            ];

            string mediaKey = await _media.ImagePrivateKeyAsync("care-home-media", maxBytes: 5_242_880);

            int roomNumber = 101;
            foreach ((string english, string arabic, decimal price, CareHomeAllocationMode mode) in types)
            {
                CareHomeRoomType roomType = await EnsureRoomTypeAsync(facility, arabic, english, price, mode);
                records.Add(new PortalProvisionRecord(
                    $"RoomType:{english}", roomType.Id.ToString(), "ready"));

                if (!await _careHomes.RoomTypeMedia.AnyAsync(
                        item => item.RoomTypeId == roomType.Id, cancellationToken))
                {
                    _careHomes.RoomTypeMedia.Add(CareHomeRoomTypeMedia.Create(
                        roomType.Id,
                        CareHomeRoomTypeMediaKind.Cover,
                        0,
                        mediaKey,
                        "image/jpeg",
                        _media.ImageLength,
                        _now));
                    await _careHomes.SaveChangesAsync(cancellationToken);
                }

                for (int index = 0; index < 2; index++)
                {
                    CareHomeRoom room = await EnsureRoomAsync(facility, roomType, roomNumber.ToString());
                    roomNumber++;
                    records.Add(new PortalProvisionRecord(
                        $"Room:{room.RoomNumber}", room.Id.ToString(), "ready"));

                    if (!await _careHomes.RoomMedia.AnyAsync(
                            item => item.RoomId == room.Id, cancellationToken))
                    {
                        _careHomes.RoomMedia.Add(CareHomeRoomMedia.Create(
                            room.Id,
                            CareHomeRoomMediaKind.Cover,
                            0,
                            mediaKey,
                            "image/jpeg",
                            _media.ImageLength,
                            _now));
                        await _careHomes.SaveChangesAsync(cancellationToken);
                    }

                    foreach (string bedLabel in (string[])["A", "B"])
                    {
                        CareHomeBed? bed = await _careHomes.Beds.FirstOrDefaultAsync(
                            item => item.RoomId == room.Id && item.Label == bedLabel,
                            cancellationToken);
                        if (bed is null)
                        {
                            _careHomes.Beds.Add(CareHomeBed.Create(facility.Id, room.Id, bedLabel, _now));
                            await _careHomes.SaveChangesAsync(cancellationToken);
                        }
                    }
                }
            }

            int bedCount = await _careHomes.Beds.CountAsync(
                item => item.FacilityId == facility.Id, cancellationToken);
            if (bedCount < 12)
            {
                throw new InvalidOperationException(
                    $"expected at least 12 demo beds on the approved facility but found {bedCount}.");
            }

            return new PortalProvisionGroupResult(
                DemoCorpusGroups.CareHomeInventory,
                Provisioned: true,
                Detail: "3 room types (shared/private/suite), 6 rooms, 12 beds, all media produced by the private writer",
                Records: records);
        }

        private async Task<CareHomeRoomType> EnsureRoomTypeAsync(
            CareHomeFacility facility,
            string arabicName,
            string englishName,
            decimal monthlyPrice,
            CareHomeAllocationMode mode)
        {
            string taggedName = $"{englishName} [{_marker}]";
            CareHomeRoomType? existing = await _careHomes.RoomTypes.FirstOrDefaultAsync(
                item => item.FacilityId == facility.Id && item.EnglishName == taggedName,
                cancellationToken);

            if (existing is not null)
            {
                return existing;
            }

            CareHomeRoomType created = CareHomeRoomType.Create(
                facility.Id, arabicName, taggedName, monthlyPrice, mode, _now);
            _careHomes.RoomTypes.Add(created);
            await _careHomes.SaveChangesAsync(cancellationToken);
            return created;
        }

        private async Task<CareHomeRoom> EnsureRoomAsync(
            CareHomeFacility facility,
            CareHomeRoomType roomType,
            string roomNumber)
        {
            CareHomeRoom? existing = await _careHomes.Rooms.FirstOrDefaultAsync(
                item => item.FacilityId == facility.Id && item.RoomNumber == roomNumber,
                cancellationToken);

            if (existing is not null)
            {
                return existing;
            }

            CareHomeRoom created = CareHomeRoom.Create(facility.Id, roomType.Id, roomNumber, _now);
            _careHomes.Rooms.Add(created);
            await _careHomes.SaveChangesAsync(cancellationToken);
            return created;
        }

        // ------------------------------------------------------------------
        // care-home-visits
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> CareHomeVisitsAsync()
        {
            await EnsureAccountsLoadedAsync();

            CareHomeFacility facility = await _careHomes.Facilities
                .FirstOrDefaultAsync(
                    item => item.OwnerUserId == _accounts["carehome.owner1"].Id,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "the approved demo facility is missing; provision care-home-facilities first.");

            CareHomeVisitSettings? settings = await _careHomes.VisitSettings
                .FirstOrDefaultAsync(item => item.FacilityId == facility.Id, cancellationToken);

            if (settings is null)
            {
                var operatingHours = Enum.GetValues<DayOfWeek>()
                    .Select(day => new CareHomeVisitTimeWindow(day, new TimeOnly(9, 0), new TimeOnly(18, 0)))
                    .ToArray();
                var visitingWindows = Enum.GetValues<DayOfWeek>()
                    .SelectMany(day => new[]
                    {
                        new CareHomeVisitTimeWindow(day, new TimeOnly(10, 0), new TimeOnly(12, 0)),
                        new CareHomeVisitTimeWindow(day, new TimeOnly(14, 0), new TimeOnly(17, 0))
                    })
                    .ToArray();
                var closures = new[]
                {
                    new CareHomeVisitClosure(_today.AddDays(7), "SANAD DEMO maintenance closure")
                };

                settings = CareHomeVisitSettings.Create(
                    facility.Id, visitorCapacity: 3, operatingHours, visitingWindows, closures, _now);
                _careHomes.VisitSettings.Add(settings);
                await _careHomes.SaveChangesAsync(cancellationToken);
            }

            Family family = _familyByOwnerRole["family.owner"];
            var records = new List<PortalProvisionRecord>
            {
                new("CareHomeVisitSettings", settings.Id.ToString(), $"v{settings.Version}")
            };

            (string Slot, CareHomeVisitStatus End, CareHomeVisitKind Kind, int DayOffset, int Hour)[] plan =
            [
                ("pending-1", CareHomeVisitStatus.Pending, CareHomeVisitKind.Prospective, 3, 10),
                ("pending-2", CareHomeVisitStatus.Pending, CareHomeVisitKind.Resident, 4, 14),
                ("approved-1", CareHomeVisitStatus.Approved, CareHomeVisitKind.Prospective, 5, 10),
                ("approved-2", CareHomeVisitStatus.Approved, CareHomeVisitKind.Resident, 5, 15),
                ("approved-3", CareHomeVisitStatus.Approved, CareHomeVisitKind.Prospective, 6, 11),
                ("rejected-1", CareHomeVisitStatus.Rejected, CareHomeVisitKind.Prospective, 8, 10),
                ("family-cancelled", CareHomeVisitStatus.FamilyCancelled, CareHomeVisitKind.Resident, 9, 14),
                ("facility-cancelled", CareHomeVisitStatus.FacilityCancelled, CareHomeVisitKind.Prospective, 10, 10),
                ("reschedule-source", CareHomeVisitStatus.Rescheduled, CareHomeVisitKind.Prospective, 11, 10)
            ];

            foreach ((string slot, CareHomeVisitStatus end, CareHomeVisitKind kind, int dayOffset, int hour) in plan)
            {
                DateTime startsAt = _now.Date.AddDays(dayOffset).AddHours(hour);
                CareHomeVisit visit = await EnsureVisitAsync(
                    facility, family, kind, startsAt, slot, end);
                records.Add(new PortalProvisionRecord(
                    $"CareHomeVisit:{slot}", visit.Id.ToString(), visit.Status.ToString()));
            }

            // The tenth visit is the replacement created by the reschedule, so
            // the pair is internally consistent (source Rescheduled -> new
            // ReschedulePending) instead of a synthetic dangling link.
            CareHomeVisit source = await _careHomes.Visits.FirstAsync(
                item => item.IdempotencyKey == VisitKey("reschedule-source"), cancellationToken);
            CareHomeVisit? replacement = await _careHomes.Visits.FirstOrDefaultAsync(
                item => item.RescheduleOfVisitId == source.Id, cancellationToken);

            if (replacement is null)
            {
                replacement = CareHomeVisit.RequestReschedule(
                    source, _now.Date.AddDays(12).AddHours(10), _now);
                _careHomes.Visits.Add(replacement);
                await _careHomes.SaveChangesAsync(cancellationToken);
            }

            // Only once the replacement exists is the source retired, so the pair
            // is always consistent (a Rescheduled source plus its ReschedulePending
            // replacement) and a replay finds both and skips. RequestReschedule
            // requires the source to still be a future approved visit, so the
            // source can only be marked after the replacement is created.
            if (source.Status == CareHomeVisitStatus.Approved)
            {
                source.MarkRescheduled(_accounts["carehome.owner1"].Id, _now);
                await _careHomes.SaveChangesAsync(cancellationToken);
            }

            records.Add(new PortalProvisionRecord(
                "CareHomeVisit:reschedule-replacement", replacement.Id.ToString(), replacement.Status.ToString()));

            return new PortalProvisionGroupResult(
                DemoCorpusGroups.CareHomeVisits,
                Provisioned: true,
                Detail: "visit settings (hours, two daily windows, one closure) and 10 visits across pending, approved, rejected, both cancellation owners and a reschedule pair",
                Records: records);
        }

        private string VisitKey(string slot) =>
            DemoCorpusIdentity.RecordKey(_runTag, DemoCorpusGroups.CareHomeVisits, slot);

        private async Task<CareHomeVisit> EnsureVisitAsync(
            CareHomeFacility facility,
            Family family,
            CareHomeVisitKind kind,
            DateTime startsAtUtc,
            string slot,
            CareHomeVisitStatus end)
        {
            string key = VisitKey(slot);
            CareHomeVisit? visit = await _careHomes.Visits.FirstOrDefaultAsync(
                item => item.IdempotencyKey == key, cancellationToken);

            if (visit is null)
            {
                visit = CareHomeVisit.Request(
                    facility.Id,
                    family.Id,
                    _accounts["family.owner"].Id,
                    kind,
                    kind == CareHomeVisitKind.Resident ? _dependents["elderly.1"].Id : null,
                    "SANAD DEMO Visitor",
                    "+201000000000",
                    visitorCount: 2,
                    startsAtUtc,
                    _now,
                    key);
                _careHomes.Visits.Add(visit);
                await _careHomes.SaveChangesAsync(cancellationToken);
            }

            if (visit.Status == CareHomeVisitStatus.Pending && end != CareHomeVisitStatus.Pending)
            {
                switch (end)
                {
                    case CareHomeVisitStatus.Approved:
                        visit.Approve(_accounts["carehome.owner1"].Id, _now);
                        break;
                    case CareHomeVisitStatus.Rejected:
                        visit.Reject(_accounts["carehome.owner1"].Id, "SANAD DEMO visit rejection", _now);
                        break;
                    case CareHomeVisitStatus.FamilyCancelled:
                        visit.Cancel(byFacility: false, _accounts["family.owner"].Id, "SANAD DEMO family cancellation", _now);
                        break;
                    case CareHomeVisitStatus.FacilityCancelled:
                        visit.Cancel(byFacility: true, _accounts["carehome.owner1"].Id, "SANAD DEMO facility cancellation", _now);
                        break;
                    case CareHomeVisitStatus.Rescheduled:
                        visit.Approve(_accounts["carehome.owner1"].Id, _now);
                        break;
                    default:
                        throw new InvalidOperationException($"unsupported demo visit state '{end}'.");
                }

                await _careHomes.SaveChangesAsync(cancellationToken);
            }

            // The source is deliberately left Approved here: the caller creates the
            // replacement through RequestReschedule (which requires the source to
            // still be a future approved visit) and only then marks the source
            // Rescheduled. Marking it here would flip it first and make the
            // reschedule refuse its own source.
            return visit;
        }

        // ------------------------------------------------------------------
        // community
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> CommunityAsync()
        {
            await EnsureAccountsLoadedAsync();
            var records = new List<PortalProvisionRecord>();

            // The private image is written by the real writer (which names every
            // file with a fresh version-7 GUID) on the FIRST run only. A replay
            // must reuse the exact key the demo posts already carry: otherwise
            // the storage root gains an orphan file AND, because this row is
            // identified by its storage key, the lookup below misses and a
            // second community-image row is inserted on every replay.
            Post? existingImagePost = await _community.Posts.FirstOrDefaultAsync(
                item => item.TitleEnglish == $"SANAD DEMO named-family post [{_marker}]",
                cancellationToken);

            CommunityImage? image = null;
            if (existingImagePost?.ImageUrl is not null)
            {
                image = await _community.CommunityImages.FirstOrDefaultAsync(
                    item => item.StorageKey == existingImagePost.ImageUrl, cancellationToken);
                if (image is null)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(
                        existingImagePost.ImageUrl,
                        @"images/([0-9a-fA-F-]{36})/file");
                    if (match.Success && Guid.TryParse(match.Groups[1].Value, out Guid parsed))
                    {
                        image = await _community.CommunityImages.FindAsync(new CommunityImageId(parsed));
                    }
                }
            }

            if (image is null)
            {
                string storageKey = await _media.ImagePrivateKeyAsync("community", maxBytes: 2_097_152);
                image = CommunityImage.Create(
                    storageKey, "image/jpeg", _media.ImageLength, _accounts["carehome.owner1"].Id, _now);
                _community.CommunityImages.Add(image);
                await _community.SaveChangesAsync(cancellationToken);
            }

            string imageKey = CommunityImageUrls.For(image.Id);

            (string Slot, string AuthorRole, bool Anonymous, bool Removed)[] plan =
            [
                ("named-family", "family.owner", false, false),
                ("named-caregiver", "caregiver.1", false, false),
                ("named-facility", "carehome.owner1", false, false),
                ("anonymous-member", "family.member", true, false),
                ("named-owner2", "family.owner2", false, false),
                ("removed", "family.owner", false, true)
            ];

            var posts = new Dictionary<string, Post>(StringComparer.Ordinal);
            foreach ((string slot, string authorRole, bool anonymous, bool removed) in plan)
            {
                // The post's ImageUrl is the AUTHORIZED read route derived from
                // the CommunityImage row's id, never a raw storage key: the
                // public API serves uploads only through
                // /api/v1/community/uploads/images/{id}/file, which resolves the
                // row, re-checks visibility and authorization, then streams the
                // private file. Writing the raw 'private/...' key into ImageUrl
                // (the pre-fix behaviour) is unreachable from a client and
                // double-counted as an orphan-looking private artifact.
                string? imageUrl = slot is "named-family" or "named-caregiver"
                    ? CommunityImageUrls.For(image.Id)
                    : null;
                Post post = await EnsurePostAsync(slot, _accounts[authorRole], anonymous, removed, imageUrl);
                posts[slot] = post;
                records.Add(new PortalProvisionRecord($"Post:{slot}", post.Id.ToString(), post.IsAnonymous ? "anonymous" : "named"));
            }

            // Deduplicated views: three distinct demo viewers on the named post,
            // so the mobile report can show a recorded lifetime count. Anonymous
            // posts get no viewer rows at all.
            (string Slot, string ViewerRole)[] views =
            [
                ("named-family", "caregiver.2"),
                ("named-family", "carehome.owner2"),
                ("named-family", "family.owner2"),
                ("named-owner2", "family.owner")
            ];

            foreach ((string slot, string viewerRole) in views)
            {
                await EnsurePostViewAsync(posts[slot], _accounts[viewerRole]);
            }

            records.Add(new PortalProvisionRecord("PostView", $"{views.Length} lifetime views", "ready"));

            (string Slot, string AuthorRole, int Index)[] comments =
            [
                ("named-family", "family.owner", 0),
                ("named-family", "caregiver.1", 1),
                ("named-family", "family.member", 2),
                ("named-caregiver", "family.owner", 0),
                ("named-caregiver", "caregiver.2", 1),
                ("named-facility", "family.owner2", 0),
                ("named-facility", "carehome.owner2", 1),
                ("anonymous-member", "family.owner", 0),
                ("named-owner2", "family.member", 0),
                ("named-owner2", "caregiver.3", 1),
                ("removed", "caregiver.4", 0),
                ("named-family", "carehome.owner1", 3)
            ];

            foreach ((string slot, string authorRole, int index) in comments)
            {
                Post post = posts[slot];
                await EnsureCommentAsync(post, _accounts[authorRole], index);
            }

            Comment? likedComment = await _community.Comments.FirstOrDefaultAsync(
                item => item.PostId == posts["named-family"].Id, cancellationToken);
            if (likedComment is not null)
            {
                await EnsureInteractionAsync(CommunityInteractionTarget.Comment, likedComment.Id.Value, "caregiver.1", like: true);
            }

            records.Add(new PortalProvisionRecord("Comment", "12 comments + 1 comment like", "ready"));

            // Likes: the caller's own post carries likes from others, and one
            // comment the caller owns is liked by another demo account, so the
            // viewer flags (isOwn/canEdit/canDelete/isLiked) all have real rows.
            await EnsureInteractionAsync(CommunityInteractionTarget.Post, posts["named-family"].Id.Value, "caregiver.1", like: true);
            await EnsureInteractionAsync(CommunityInteractionTarget.Post, posts["named-family"].Id.Value, "family.owner2", like: true);
            await EnsureInteractionAsync(CommunityInteractionTarget.Post, posts["named-caregiver"].Id.Value, "family.owner", like: true);
            await EnsureInteractionAsync(CommunityInteractionTarget.Post, posts["named-owner2"].Id.Value, "family.member", like: true);

            records.Add(new PortalProvisionRecord("Interaction", "4 post likes", "ready"));

            return new PortalProvisionGroupResult(
                DemoCorpusGroups.Community,
                Provisioned: true,
                Detail: "6 posts (named, anonymous, one removed), deduplicated views, 12 comments, likes, one demo image produced by the private writer",
                Records: records);
        }

        private string PostKey(string slot) =>
            DemoCorpusIdentity.RecordKey(_runTag, DemoCorpusGroups.Community, slot);

        private async Task<Post> EnsurePostAsync(
            string slot,
            User author,
            bool anonymous,
            bool removed,
            string? imageUrl)
        {
            string title = $"SANAD DEMO {slot} post";
            Post? post = await _community.Posts.FirstOrDefaultAsync(
                item => item.TitleEnglish == $"{title} [{_marker}]", cancellationToken);

            if (post is not null)
            {
                return post;
            }

            post = Post.Create(
                ArabicPostTitle(slot),
                $"{title} [{_marker}]",
                ArabicPostBody(slot),
                "SANAD DEMO community post body.",
                imageUrl,
                anonymous,
                author.Id,
                _now.AddDays(-3));
            _community.Posts.Add(post);
            await _community.SaveChangesAsync(cancellationToken);

            post.Publish(author.Id, _now.AddDays(-3));

            if (removed)
            {
                post.Delete(author.Id, _now.AddDays(-1));
            }

            await _community.SaveChangesAsync(cancellationToken);
            return post;
        }

        // Meaningful Arabic demo copy for the six community posts. The English
        // title (with the run marker) remains the load key; these strings are
        // display text only, so a repair of already-persisted rows can safely
        // mirror them.
        private static string ArabicPostTitle(string slot) => slot switch
        {
            "named-family" => "كيف أرتّب يومًا مريحًا لكبير السن في المنزل؟",
            "named-caregiver" => "متابعة ضغط مريض مسن: تجربة شهر كامل",
            "named-facility" => "جولة داخل دار رعاية سند التجريبية",
            "anonymous-member" => "سؤال: كم ساعة تمريض يحتاج مريض السرير؟",
            "named-owner2" => "تجهيز غرفة خاصة قبل انتقال الأب لدار الرعاية",
            _ => "منشور تجريبي حُذف للتحقق من الإخفاء"
        };

        private static string ArabicPostBody(string slot) => slot switch
        {
            "named-family" => "خطة يوم بسيطة: وجبات خفيفة كل ثلاث ساعات، مشي قصير في الحديقة، وتمارين ذاكرة مسائية. جرّبها مع من تحب وشاركنا تجربتك.",
            "named-caregiver" => "تابعنا قراءات الضغط والسكر يوميًا لمدة شهر لمريض مسن، مع جدول أدوية مبسّط؛ التزام المريض تحسّن بوضوح والنتائج استقرت.",
            "named-facility" => "صور من الغرف المشتركة والخاصة، حديقة المشي، ومواعيد الأنشطة اليومية داخل دار رعاية سند التجريبية. أهلًا بزيارة تعارف قصيرة.",
            "anonymous-member" => "سؤال من عائلة تفضّل عدم ذكر اسمها: كم ساعة تمريض منزلية مناسبة لمريض طريح الفراش، وكيف نختار الكيرجيفر المناسب؟",
            "named-owner2" => "قبل الانتقال: سرير طبي بأرياج جانبية، أدراج قريبة، إضاءة ليلية خفيفة، وأشياء مألوفة تقلّل قلق الانتقال.",
            _ => "ذلك المنشور التجريبي حذفه صاحبه للتحقق من سلوك الإخفاء والعناصر المرتبطة به."
        };

        private static string ArabicComment(int index)
        {
            int slot = index % 4;
            return slot switch
            {
                0 => "شكرًا على المعلومة، سنجربها مع جدتنا هذا الأسبوع ونخبركم بالنتيجة.",
                1 => "استفدت من النصيحة؛ هل يوجد كيرجيفر معتمد يغطي منطقة مدينة نصر؟",
                2 => "لدينا تجربة مشابهة مع والدنا، المشي القصير فعلًا يحسّن المزاج والنوم.",
                _ => "معلومة مفيدة جدًا، بانتظار باقي التفاصيل في الردود."
            };
        }

        private static string ArabicWellnessTitle(int index) => index switch
        {
            1 => "تمارين إطالة صباحية آمنة لكبار السن",
            2 => "نصائح تغذية لتحسين المناعة في الشتاء",
            3 => "كيف تشجّع كبير السن على المشي اليومي؟",
            4 => "أطعمة غنية بالكالسيوم لعظام أقوى",
            5 => "تمارين ذاكرة بسيطة داخل المنزل",
            _ => "نوم مريح لكبار السن: عادات مسائية مفيدة"
        };

        private static string ArabicWellnessSection(int section, int index) => section switch
        {
            1 => $"نصيحة أسبوعية رقم {index} تشرح فائدة العادة وسبب ملاءمتها لكبار السن بلغة مبسطة.",
            _ => "خطوات التطبيق: ابدأ بخمس دقائق يوميًا، زد الوقت تدريجيًا، وتوقف عند أي ألم و استشر الطبيب."
        };

        private static string ArabicLibraryTitle(string slot) => slot switch
        {
            "article-1" => "دليل العائلة للتعامل مع فقدان الذاكرة",
            "article-2" => "الصحة النفسية لكبار السن: علامات مبكرة ودعم فعلي",
            "article-3" => "تعديل المنزل بأمان لكبير السن",
            "image-1" => "صور إرشادية: تمارين كرسي للمسنين",
            "image-2" => "جدول أدوية مصوّر مبسّط للاستخدام اليومي",
            _ => "فيديو: جولة داخل دار رعاية سند التجريبية"
        };

        private static string ArabicLibrarySummary(string slot) => slot switch
        {
            "article-1" => "شرح مبسط لأسباب فقدان الذاكرة مع خطوات عملية تساعد العائلة على التعامل اليومي.",
            "article-2" => "كيف تلاحظ تغيرات المزاج لدى كبير السن مبكرًا، ومتى تطلب استشارة متخصص.",
            "article-3" => "قائمة تدقيق لتأمين الحمام والمطبخ والسلالم داخل منزل كبير السن.",
            "image-1" => "سلسلة صور توضح تمارين الكرسي الآمنة مع عدد التكرارات المقترحة.",
            "image-2" => "نموذج مصوّر لترتيب جرعات الأدوية صباحًا ومساءً لتقليل النسيان.",
            _ => "مقطع مرئي قصير يعرض مرافق دار رعاية سند التجريبية والأنشطة اليومية."
        };

        private static string ArabicLibrarySectionTitle(int section, string slot) => section switch
        {
            1 => $"مقدمة: {ArabicLibraryTitle(slot)}",
            _ => "خطوات عملية للاستخدام اليومي"
        };

        private static string ArabicLibrarySectionBody(int section, string slot) => section switch
        {
            1 => $"هذا القسم يعرّف بالموضوع وأهميته للعائلة والعناية بكبار السن ({slot}).",
            _ => "طبّق الخطوات تدريجيًا، تابع الملاحظات اليومية، وشارك النتائج مع مقدم الرعاية."
        };

        private static string ArabicFaqQuestion(string slot) => slot switch
        {
            "family-1" => "كيف أحجز كيرجيفر للزيارة المنزلية من التطبيق؟",
            "family-2" => "ما الفرق بين الاشتراك المجاني والاشتراك المدفوع؟",
            "family-3" => "كيف أضيف فردًا مسنًا من العائلة إلى حسابي؟",
            "family-4" => "كيف أتابع سجل الرعاية اليومي لوالدي؟",
            "caregiver-1" => "كيف أستلم مستحقاتي بعد إتمام الزيارة؟",
            "caregiver-2" => "كيف أحدّث ملفي المهني وشهاداتي؟",
            "elderly-1" => "كيف أجيب على طلب زيارة أو محادثة من عائلتي؟",
            _ => "كيف أرسل ملف دار الرعاية للمراجعة والاعتماد؟"
        };

        private static string ArabicFaqAnswer() =>
            "هذا جواب تجريبي يوضح السلوك المتوقع داخل النسخة التجريبية؛ خطوات التنفيذ الفعلية تظهر في النسخة الرسمية.";

        private static string ArabicNotificationTitle(int index, string type) => type switch
        {
            "booking-confirmed" => "تم تأكيد حجز الكيرجيفر",
            "booking-cancelled" => "تم إلغاء الحجز",
            "booking-requested" => "طلب حجز جديد بانتظار موافقتك",
            "care-record-added" => "سجل رعاية جديد من الكيرجيفر",
            "comment-reply" => "رد جديد على تعليقك",
            "renewal-reminder" => "تذكير: تجديد الاشتراك قريب",
            "payment-succeeded" => "تمت عملية الدفع بنجاح",
            "payout-released" => "تم صرف مستحقاتك",
            "visit-requested" => "طلب زيارة تعارف جديدة",
            "stay-checked-in" => "تم تسجيل دخول الإقامة",
            "moderation-result" => "نتيجة مراجعة المحتوى",
            _ => "استفسار دعم جديد"
        };

        private static string ArabicNotificationBody(string type) => type switch
        {
            "booking-confirmed" => "تم تأكيد حجز الزيارة المنزلية؛ يمكنك متابعة تفاصيل الموعد من صفحة الحجوزات.",
            "booking-cancelled" => "أُلغي الحجز المذكور ولم يتم خصم أي مبالغ.",
            "booking-requested" => "وصولك طلب حجز جديد؛ راجع التفاصيل ثم وافق أو رفض من لوحة الكيرجيفر.",
            "care-record-added" => "أضاف الكيرجيفر سجل رعاية جديد مع القراءات والملاحظات.",
            "comment-reply" => "رد أحد المستخدمين على تعليقك في المجتمع.",
            "renewal-reminder" => "اشتراكك الحالي يقترب من موعد التجديد؛ الدفع سيتم تلقائيًا.",
            "payment-succeeded" => "نجحت عملية الدفع وتم تسجيل الفاتورة في محفظتك.",
            "payout-released" => "تمت صرف مستحقات الأسبوع إلى الحساب المرتبط.",
            "visit-requested" => "طلب عائلة زيارة تعارف قصيرة قبل الحجز النهائي.",
            "stay-checked-in" => "تم تسجيل دخول نزيل الإقامة بنجاح.",
            "moderation-result" => "انتهت مراجعة المحتوى المُبلّغ عنه واتُّخذ الإجراء المناسب.",
            _ => "إشعار تجريبي لأغراض العرض فقط."
        };

        private async Task EnsurePostViewAsync(Post post, User viewer)
        {
            CommunityPostId postId = post.Id;
            bool exists = await _community.PostViews.AnyAsync(
                item => item.PostId == postId && item.ViewerUserId == viewer.Id,
                cancellationToken);

            if (exists)
            {
                return;
            }

            _community.PostViews.Add(
                Sanad.Modules.Community.Domain.Posts.PostView.Create(post.Id, viewer.Id, _now.AddDays(-2)));
            post.IncrementViews();
            await _community.SaveChangesAsync(cancellationToken);
        }

        private async Task<Comment> EnsureCommentAsync(Post post, User author, int index)
        {
            string english = $"SANAD DEMO comment {index} on {post.Id.Value:N} [{_marker}]";
            Comment? existing = await _community.Comments.FirstOrDefaultAsync(
                item => item.PostId == post.Id && item.ContentEnglish == english,
                cancellationToken);

            if (existing is not null)
            {
                return existing;
            }

            Comment comment = Comment.Create(
                post.Id,
                author.Id,
                ArabicComment(index),
                english,
                _now.AddDays(-2).AddMinutes(index));
            _community.Comments.Add(comment);
            post.IncrementComments();
            await _community.SaveChangesAsync(cancellationToken);
            return comment;
        }

        private async Task EnsureInteractionAsync(
            CommunityInteractionTarget target,
            Guid targetId,
            string userRole,
            bool like)
        {
            UserId userId = _accounts[userRole].Id;
            bool exists = await _community.Interactions.AnyAsync(
                item => item.TargetType == target
                    && item.TargetId == targetId
                    && item.UserId == userId
                    && item.Kind == CommunityInteractionKind.Like,
                cancellationToken);

            if (exists)
            {
                return;
            }

            _community.Interactions.Add(CommunityInteraction.Create(
                target, targetId, userId, CommunityInteractionKind.Like, _now.AddDays(-2)));
            await _community.SaveChangesAsync(cancellationToken);
            _ = like;
        }

        // ------------------------------------------------------------------
        // cms-library
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> CmsLibraryAsync()
        {
            await EnsureAccountsLoadedAsync();
            var records = new List<PortalProvisionRecord>();

            LibraryCategory first = await EnsureLibraryCategoryAsync("Family Care", "رعاية الأم والعيلة");
            LibraryCategory second = await EnsureLibraryCategoryAsync("Wellness", "الصحة والرعاية الذاتية");
            records.Add(new PortalProvisionRecord("LibraryCategory:first", first.Id.ToString(), "ready"));
            records.Add(new PortalProvisionRecord("LibraryCategory:second", second.Id.ToString(), "ready"));

            string coverKey = await _media.ImagePublicKeyAsync("library");
            string videoKey = await _media.VideoKeyAsync("library-video");

            (string Slot, LibraryContentType Type, LibraryCategory Category)[] plan =
            [
                ("article-1", LibraryContentType.Article, first),
                ("article-2", LibraryContentType.Article, second),
                ("article-3", LibraryContentType.Article, first),
                ("image-1", LibraryContentType.Article, second),
                ("image-2", LibraryContentType.Article, first),
                ("video-1", LibraryContentType.Video, second)
            ];

            foreach ((string slot, LibraryContentType type, LibraryCategory category) in plan)
            {
                LibraryContent content = await EnsureLibraryContentAsync(
                    slot,
                    type,
                    category,
                    coverKey,
                    type == LibraryContentType.Video ? videoKey : null);
                records.Add(new PortalProvisionRecord(
                    $"LibraryContent:{slot}", content.Id.ToString(), content.Status.ToString()));
            }

            return new PortalProvisionGroupResult(
                DemoCorpusGroups.CmsLibrary,
                Provisioned: true,
                Detail: "2 categories and 6 library items (3 articles, 2 images, 1 video); covers from the public writer, video from the private writer",
                Records: records);
        }

        private async Task<LibraryCategory> EnsureLibraryCategoryAsync(string englishName, string arabicName)
        {
            string tagged = $"{englishName} [{_marker}]";
            LibraryCategory? existing = await _cms.LibraryCategories.FirstOrDefaultAsync(
                item => item.EnglishName == tagged, cancellationToken);

            if (existing is not null)
            {
                return existing;
            }

            LibraryCategory created = LibraryCategory.Create(arabicName, tagged, isActive: true, DateTime.UtcNow);
            _cms.LibraryCategories.Add(created);
            await _cms.SaveChangesAsync(cancellationToken);
            return created;
        }

        private async Task<LibraryContent> EnsureLibraryContentAsync(
            string slot,
            LibraryContentType type,
            LibraryCategory category,
            string coverKey,
            string? videoKey)
        {
            string englishTitle = $"SANAD DEMO {slot} [{_marker}]";
            LibraryContent? existing = await _cms.LibraryContents
                .Include(item => item.Sections)
                .FirstOrDefaultAsync(item => item.EnglishTitle == englishTitle, cancellationToken);

            if (existing is not null)
            {
                return existing;
            }

            // Video content is domain-forbidden from carrying article sections
            // (LibraryContent.Create enforces it), so sections are supplied only
            // for the article/image items.
            List<LibraryContentSectionDraft> sections = type == LibraryContentType.Video
                ? []
                :
                [
                    new(1, ArabicLibrarySectionTitle(1, slot), $"SANAD DEMO section one for {slot}",
                        [ArabicLibrarySectionBody(1, slot)], ["Demo bullet one"]),
                    new(2, ArabicLibrarySectionTitle(2, slot), "SANAD DEMO second section",
                        [ArabicLibrarySectionBody(2, slot)], ["Demo bullet two"])
                ];

            LibraryContent content = LibraryContent.Create(
                type,
                category.Id,
                ArabicLibraryTitle(slot),
                englishTitle,
                ArabicLibrarySummary(slot),
                "SANAD DEMO library summary.",
                videoUrl: null,
                videoStorageKey: videoKey,
                durationSeconds: videoKey is null ? null : 6,
                coverImagePath: coverKey,
                isPremium: slot is "article-2" or "video-1",
                sections,
                _now.AddDays(-4));
            _cms.LibraryContents.Add(content);
            await _cms.SaveChangesAsync(cancellationToken);
            content.Publish();
            await _cms.SaveChangesAsync(cancellationToken);
            return content;
        }

        // ------------------------------------------------------------------
        // cms-wellness
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> CmsWellnessAsync()
        {
            await EnsureAccountsLoadedAsync();
            var records = new List<PortalProvisionRecord>();
            string imageKey = await _media.ImagePublicKeyAsync("wellness-tips");

            for (int index = 1; index <= 6; index++)
            {
                string englishTitle = $"SANAD DEMO wellness tip {index} [{_marker}]";
                WellnessTip? existing = await _cms.WellnessTips
                    .FirstOrDefaultAsync(item => item.EnglishTitle == englishTitle, cancellationToken);

                if (existing is null)
                {
                    existing = WellnessTip.Create(
                        ArabicWellnessTitle(index),
                        englishTitle,
                        index % 2 == 0 ? "Nutrition" : "Mobility",
                        imageKey,
                        [
                            new WellnessTipSectionDraft(1, ArabicWellnessSection(1, index), "SANAD DEMO introduction"),
                            new WellnessTipSectionDraft(2, ArabicWellnessSection(2, index), "SANAD DEMO steps")
                        ]);
                    _cms.WellnessTips.Add(existing);
                    await _cms.SaveChangesAsync(cancellationToken);

                    if (index != 6)
                    {
                        existing.Publish();
                        await _cms.SaveChangesAsync(cancellationToken);
                    }
                }

                records.Add(new PortalProvisionRecord(
                    $"WellnessTip:{index}", existing.Id.ToString(), existing.Status.ToString()));
            }

            return new PortalProvisionGroupResult(
                DemoCorpusGroups.CmsWellness,
                Provisioned: true,
                Detail: "6 wellness tips (5 published, 1 draft) with the demo image from the public writer",
                Records: records);
        }

        // ------------------------------------------------------------------
        // cms-legal
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> CmsLegalAsync()
        {
            var records = new List<PortalProvisionRecord>();

            LegalDocument privacy = await EnsureLegalDocumentAsync(
                LegalDocumentType.PrivacyPolicy,
                [
                    new(LegalSectionType.Text, 1, "نص تجريبي - ليس نصًا قانونيًا", "DEMO - not legal text",
                        "هذا نص تجريبي لأغراض العرض فقط ولا يمثل سياسة خصوصية فعلية.", "SANAD DEMO placeholder. Not legal text.",
                        null, null),
                    new(LegalSectionType.UserRights, 2, "حقوق المستخدم (تجريبي)", "User rights (demo)",
                        "بصفتك مستخدمًا تجريبيًا يمكنك تصفّح بيانات العرض وطلب إزالتها في أي وقت.", "Demo user rights for display only.",
                        ["الحق في تصفّح المحتوى التجريبي", "الحق في طلب حذف بيانات العرض"], ["Demo right one", "Demo right two"])
                ]);
            records.Add(new PortalProvisionRecord("LegalDocument:privacy", privacy.Id.ToString(), privacy.Status.ToString()));

            LegalDocument terms = await EnsureLegalDocumentAsync(
                LegalDocumentType.TermsAndConditions,
                [
                    new(LegalSectionType.Text, 1, "نص تجريبي - ليس نصًا قانونيًا", "DEMO - not legal text",
                        "هذا نص تجريبي لأغراض العرض فقط ولا يمثل شروط استخدام فعلية.", "SANAD DEMO terms placeholder. Not legal text.",
                        null, null)
                ]);
            records.Add(new PortalProvisionRecord("LegalDocument:terms", terms.Id.ToString(), terms.Status.ToString()));

            return new PortalProvisionGroupResult(
                DemoCorpusGroups.CmsLegal,
                Provisioned: true,
                Detail: "privacy policy and terms, Public audience, published, every title and body carries 'DEMO - not legal text'",
                Records: records);
        }

        private async Task<LegalDocument> EnsureLegalDocumentAsync(
            LegalDocumentType type,
            IReadOnlyList<LegalSectionDraft> sections)
        {
            LegalDocument? existing = await _cms.LegalDocuments
                .Include(item => item.Sections)
                .Where(item => item.DocumentType == type
                    && item.Audience == LegalAudience.Public
                    && item.Status == LegalDocumentStatus.Published)
                .OrderByDescending(item => item.Version)
                .FirstOrDefaultAsync(cancellationToken);

            // The published public version is a singleton the application reads:
            // reuse it and never overwrite its text.
            if (existing is not null)
            {
                return existing;
            }

            LegalDocument document = LegalDocument.Create(type, LegalAudience.Public, version: 1, sections);
            _cms.LegalDocuments.Add(document);
            await _cms.SaveChangesAsync(cancellationToken);
            document.Publish();
            await _cms.SaveChangesAsync(cancellationToken);
            return document;
        }

        // ------------------------------------------------------------------
        // cms-support
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> CmsSupportAsync()
        {
            await EnsureAccountsLoadedAsync();
            var records = new List<PortalProvisionRecord>();

            SupportContact? contact = await _cms.SupportContacts
                .FirstOrDefaultAsync(item => item.Id == SupportContact.SingletonId, cancellationToken);

            if (contact is null)
            {
                contact = SupportContact.Create("+201000000000", "demo-support@test.sanad.local");
                _cms.SupportContacts.Add(contact);
                await _cms.SaveChangesAsync(cancellationToken);
                records.Add(new PortalProvisionRecord("SupportContact", "1", "created"));
            }
            else
            {
                records.Add(new PortalProvisionRecord("SupportContact", "1", "reused (singleton, never overwritten)"));
            }

            for (int index = 1; index <= 3; index++)
            {
                string key = DemoCorpusIdentity.RecordKey(_runTag, DemoCorpusGroups.CmsSupport, $"message-{index}");
                PartnerContact? existing = await _identity.PartnerContacts.FirstOrDefaultAsync(
                    item => item.IdempotencyKey == key, cancellationToken);

                if (existing is null)
                {
                    existing = PartnerContact.Create(
                        $"SANAD DEMO partner {index}",
                        "SANAD DEMO Partner Company",
                        $"demo.partner{index}@test.sanad.local",
                        $"SANAD DEMO inquiry {index}",
                        $"SANAD DEMO partner message {index} [{_marker}]",
                        key,
                        $"demo-client-{index}",
                        _now.AddDays(-index),
                        phone: "+201000000000",
                        companyType: "Care Home",
                        city: "Cairo");
                    _identity.PartnerContacts.Add(existing);
                    await _identity.SaveChangesAsync(cancellationToken);
                }

                records.Add(new PortalProvisionRecord($"PartnerContact:{index}", existing.Id.ToString(), "ready"));
            }

            return new PortalProvisionGroupResult(
                DemoCorpusGroups.CmsSupport,
                Provisioned: true,
                Detail: "support contact singleton reused (never overwritten) and 3 partner-contact messages",
                Records: records);
        }

        // ------------------------------------------------------------------
        // cms-faq
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> CmsFaqAsync()
        {
            await EnsureAccountsLoadedAsync();
            var records = new List<PortalProvisionRecord>();

            (string Slot, LegalAudience Audience, bool Active)[] plan =
            [
                ("family-1", LegalAudience.Family, true),
                ("family-2", LegalAudience.Family, true),
                ("family-3", LegalAudience.Family, false),
                ("family-4", LegalAudience.Family, true),
                ("caregiver-1", LegalAudience.MedicalCaregiver, true),
                ("caregiver-2", LegalAudience.CompanionCaregiver, true),
                ("elderly-1", LegalAudience.Elderly, true),
                ("carehome-1", LegalAudience.CareHomeOwner, true)
            ];

            int order = 1;
            foreach ((string slot, LegalAudience audience, bool active) in plan)
            {
                string english = $"SANAD DEMO FAQ {slot} [{_marker}]";
                HelpFaq? existing = await _cms.HelpFaqs.FirstOrDefaultAsync(
                    item => item.EnglishQuestion == english, cancellationToken);

                if (existing is null)
                {
                    existing = HelpFaq.Create(
                        audience,
                        ArabicFaqQuestion(slot),
                        english,
                        ArabicFaqAnswer(),
                        "SANAD DEMO answer describing the expected behaviour.",
                        order,
                        active);
                    _cms.HelpFaqs.Add(existing);
                    await _cms.SaveChangesAsync(cancellationToken);
                }

                records.Add(new PortalProvisionRecord($"HelpFaq:{slot}", existing.Id.ToString(), active ? "active" : "inactive"));
                order++;
            }

            return new PortalProvisionGroupResult(
                DemoCorpusGroups.CmsFaq,
                Provisioned: true,
                Detail: "8 bilingual FAQs across audiences (7 active, 1 deactivated)",
                Records: records);
        }

        // ------------------------------------------------------------------
        // notifications
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> NotificationsAsync()
        {
            await EnsureAccountsLoadedAsync();
            var records = new List<PortalProvisionRecord>();

            (string Role, string Category, string Type, bool Read, string Language)[] plan =
            [
                ("family.owner", "bookings", "booking-confirmed", false, "ar"),
                ("family.owner", "bookings", "booking-cancelled", true, "en"),
                ("family.owner", "care-records", "care-record-added", true, "ar"),
                ("family.member", "community", "comment-reply", false, "en"),
                ("family.owner2", "subscriptions", "renewal-reminder", false, "ar"),
                ("family.owner2", "subscriptions", "payment-succeeded", true, "en"),
                ("caregiver.1", "bookings", "booking-requested", false, "ar"),
                ("caregiver.1", "payouts", "payout-released", true, "en"),
                ("carehome.owner1", "visits", "visit-requested", false, "ar"),
                ("carehome.owner1", "bookings", "stay-checked-in", true, "en"),
                ("carehome.owner2", "community", "moderation-result", false, "ar"),
                ("support", "support", "partner-inquiry", false, "en")
            ];

            int index = 1;
            Guid destination = (_dependents.Count > 0 ? _dependents["elderly.1"].Id : default).Value;
            if (destination == Guid.Empty)
            {
                destination = _accounts["family.owner"].Id.Value;
            }

            foreach ((string role, string category, string type, bool read, string language) in plan)
            {
                string key = DemoCorpusIdentity.RecordKey(_runTag, DemoCorpusGroups.Notifications, $"notification-{index}");
                Notification? existing = await _notifications.Notifications.FirstOrDefaultAsync(
                    item => item.IdempotencyKey == key, cancellationToken);

                if (existing is null)
                {
                    existing = Notification.Create(
                        _accounts[role].Id.Value,
                        category,
                        type,
                        language == "ar" ? ArabicNotificationTitle(index, type) : $"SANAD DEMO notification {index}",
                        language == "ar"
                            ? ArabicNotificationBody(type)
                            : "SANAD DEMO notification body for display only.",
                        "demo",
                        destination,
                        _now.AddHours(-index),
                        key);

                    if (read)
                    {
                        existing.MarkRead(_now.AddHours(-index).AddMinutes(5));
                    }

                    _notifications.Notifications.Add(existing);
                    await _notifications.SaveChangesAsync(cancellationToken);
                }

                records.Add(new PortalProvisionRecord(
                    $"Notification:{index}", existing.Id.ToString(), read ? "read" : "unread"));
                index++;
            }

            for (int message = 1; message <= 3; message++)
            {
                string key = DemoCorpusIdentity.RecordKey(_runTag, DemoCorpusGroups.Notifications, $"email-{message}");
                bool exists = await _notifications.EmailOutboxMessages.AnyAsync(
                    item => item.IdempotencyKey == key, cancellationToken);

                if (!exists)
                {
                    _notifications.EmailOutboxMessages.Add(EmailOutboxMessage.Create(
                        $"demo.family.owner@test.sanad.local",
                        $"SANAD DEMO email {message}",
                        $"SANAD DEMO plain-text email body {message}.",
                        $"<p>SANAD DEMO HTML email body {message}.</p>",
                        key,
                        _now.AddHours(-message)));
                    await _notifications.SaveChangesAsync(cancellationToken);
                }
            }

            records.Add(new PortalProvisionRecord("EmailOutbox", "3 pending messages", "ready"));

            return new PortalProvisionGroupResult(
                DemoCorpusGroups.Notifications,
                Provisioned: true,
                Detail: "12 notifications for demo accounts only (read and unread, both languages) plus 3 email outbox rows",
                Records: records);
        }

        // ------------------------------------------------------------------
        // chat
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> ChatAsync()
        {
            await EnsureAccountsLoadedAsync();
            var records = new List<PortalProvisionRecord>();

            (string Slot, string First, string Second, bool HiddenForFirst, string? Blocker)[] plan =
            [
                ("open", "family.owner", "caregiver.1", false, null),
                ("hidden", "family.owner", "carehome.owner1", true, null),
                ("blocked", "family.owner2", "caregiver.3", false, "family.owner2"),
                ("reported", "family.owner", "caregiver.2", false, null)
            ];

            var conversations = new Dictionary<string, Conversation>(StringComparer.Ordinal);
            foreach ((string slot, string first, string second, bool hiddenForFirst, string? blocker) in plan)
            {
                Conversation conversation = await EnsureConversationAsync(slot, first, second, hiddenForFirst);
                conversations[slot] = conversation;
                records.Add(new PortalProvisionRecord(
                    $"Conversation:{slot}",
                    conversation.Id.ToString(),
                    conversation.IsHiddenFor(_accounts[first].Id) ? "hidden-for-first" : "visible"));

                if (blocker is not null)
                {
                    string blocked = blocker == first ? second : first;
                    await EnsureChatBlockAsync(blocker, blocked);
                }
            }

            // 27 messages across the four threads; the open thread carries an
            // image attachment, the reported thread a PDF, and the open thread
            // one location message. Every message has its own deterministic
            // client reference, so a rerun finds each row instead of adding one.
            (string Slot, string First, string Second, int Count, int ImageIndex, int DocumentIndex, int LocationIndex)[] threads =
            [
                ("open", "family.owner", "caregiver.1", 10, 4, -1, 7),
                ("hidden", "family.owner", "carehome.owner1", 6, -1, -1, -1),
                ("blocked", "family.owner2", "caregiver.3", 4, -1, -1, -1),
                ("reported", "family.owner", "caregiver.2", 7, -1, 3, -1)
            ];

            int messageCount = 0;
            foreach ((string slot, string first, string second, int count, int imageIndex, int documentIndex, int locationIndex) in threads)
            {
                Conversation conversation = conversations[slot];

                Attachment? image = imageIndex >= 0
                    ? await EnsureChatAttachmentAsync(
                        conversation, _accounts[second], "image/jpeg", isDocument: false)
                    : null;
                Attachment? document = documentIndex >= 0
                    ? await EnsureChatAttachmentAsync(
                        conversation, _accounts[first], "application/pdf", isDocument: true)
                    : null;

                for (int index = 0; index < count; index++)
                {
                    bool fromFirst = index % 2 == 0;
                    User sender = _accounts[fromFirst ? first : second];
                    MessageType type = index == imageIndex || index == documentIndex
                        ? MessageType.File
                        : index == locationIndex
                            ? MessageType.Location
                            : MessageType.Text;
                    Attachment? attachment = index == imageIndex
                        ? image
                        : index == documentIndex
                            ? document
                            : null;

                    await EnsureChatMessageAsync(conversation, slot, index, sender, type, attachment);
                    messageCount++;
                }
            }

            records.Add(new PortalProvisionRecord("Message", $"{messageCount} messages", "ready"));
            records.Add(new PortalProvisionRecord("Attachment", "2 attachments (one image, one PDF)", "ready"));

            // Delivery/read stamps and per-user read marks, so the thread list
            // and the read receipts both have real state.
            Message[] openMessages = await _chat.Messages
                .Where(item => item.ConversationId == conversations["open"].Id)
                .OrderBy(item => item.SentOnUtc)
                .ToArrayAsync(cancellationToken);

            foreach (Message message in openMessages.Take(openMessages.Length - 2))
            {
                message.MarkDelivered(message.SentOnUtc.AddMinutes(1));
                message.MarkRead(message.SentOnUtc.AddMinutes(2));
            }

            foreach (Message message in openMessages.Skip(openMessages.Length - 2))
            {
                message.MarkDelivered(message.SentOnUtc.AddMinutes(1));
            }

            await _chat.SaveChangesAsync(cancellationToken);

            await EnsureChatReadStateAsync(conversations["open"], _accounts["family.owner"], openMessages[^1]);
            await EnsureChatReadStateAsync(conversations["open"], _accounts["caregiver.1"], openMessages[^2]);
            await EnsureChatReadStateAsync(conversations["hidden"], _accounts["carehome.owner1"], null);
            await EnsureChatReadStateAsync(conversations["reported"], _accounts["family.owner"], null);
            records.Add(new PortalProvisionRecord("ConversationReadState", "4 read marks", "ready"));

            // Outbox rows already published by the dispatcher shape, so the
            // demo leaves no pending provider intent behind.
            Message[] openFromCaregiver = openMessages
                .Where(item => item.SenderUserId == _accounts["caregiver.1"].Id)
                .ToArray();
            int outbox = 0;
            foreach (Message message in openFromCaregiver.Take(3))
            {
                if (await EnsureChatOutboxAsync(conversations["open"], message, _accounts["family.owner"]))
                {
                    outbox++;
                }
            }

            records.Add(new PortalProvisionRecord("ChatOutbox", $"{outbox} new published intent rows", "ready"));

            // One open report on a message and one already-dismissed report on
            // the conversation, so both the moderation queue and the resolved
            // state have a row.
            await EnsureChatReportAsync(
                conversations["reported"], openMessages: null, messageIndex: 2, upheld: false, dismissed: false);
            await EnsureChatReportAsync(
                conversations["reported"], null, messageIndex: null, upheld: false, dismissed: true);
            records.Add(new PortalProvisionRecord("Report", "1 open / 1 dismissed", "ready"));

            return new PortalProvisionGroupResult(
                DemoCorpusGroups.Chat,
                Provisioned: true,
                Detail: "4 direct threads (open, hidden, blocked, reported), 27 messages, 2 attachments, delivery/read stamps, read marks, block and reports",
                Records: records);
        }

        private async Task<Conversation> EnsureConversationAsync(
            string slot,
            string firstRole,
            string secondRole,
            bool hiddenForFirst)
        {
            UserId first = _accounts[firstRole].Id;
            UserId second = _accounts[secondRole].Id;

            Conversation? conversation = await _chat.Conversations
                .FirstOrDefaultAsync(
                    item => item.Scope == ConversationScope.Direct
                        && ((item.ParticipantOne == first && item.ParticipantTwo == second)
                            || (item.ParticipantOne == second && item.ParticipantTwo == first)),
                    cancellationToken);

            if (conversation is null)
            {
                conversation = Conversation.Create(
                    first, second, ConversationScope.Direct, bookingId: null, _now.AddDays(-3));
                _chat.Conversations.Add(conversation);
                await _chat.SaveChangesAsync(cancellationToken);
            }

            if (hiddenForFirst && !conversation.IsHiddenFor(first))
            {
                conversation.Hide(first);
                await _chat.SaveChangesAsync(cancellationToken);
            }

            _ = slot;
            return conversation;
        }

        private async Task EnsureChatBlockAsync(string blockerRole, string blockedRole)
        {
            UserId blocker = _accounts[blockerRole].Id;
            UserId blocked = _accounts[blockedRole].Id;

            bool exists = await _chat.Blocks.AnyAsync(
                item => item.BlockerUserId == blocker && item.BlockedUserId == blocked,
                cancellationToken);

            if (exists)
            {
                return;
            }

            _chat.Blocks.Add(Block.Create(blocker, blocked, _now.AddDays(-1)));
            await _chat.SaveChangesAsync(cancellationToken);
        }

        private async Task<Attachment> EnsureChatAttachmentAsync(
            Conversation conversation,
            User uploader,
            string contentType,
            bool isDocument)
        {
            Attachment? existing = await _chat.Attachments.FirstOrDefaultAsync(
                item => item.ConversationId == conversation.Id && item.ContentType == contentType,
                cancellationToken);

            if (existing is not null)
            {
                return existing;
            }

            string key = isDocument
                ? await _media.DocumentKeyAsync("chat-attachments")
                : await _media.ImagePrivateKeyAsync("chat-attachments", maxBytes: 5_242_880);
            long length = isDocument ? _media.DocumentLength : _media.ImageLength;

            Attachment attachment = Attachment.Create(
                conversation.Id, uploader.Id, key, contentType, length, _now.AddDays(-2));
            _chat.Attachments.Add(attachment);
            await _chat.SaveChangesAsync(cancellationToken);
            return attachment;
        }

        private async Task<Message> EnsureChatMessageAsync(
            Conversation conversation,
            string slot,
            int index,
            User sender,
            MessageType type,
            Attachment? attachment)
        {
            string key = DemoCorpusIdentity.RecordKey(
                _runTag, DemoCorpusGroups.Chat, $"{slot}-{index}");

            Message? existing = await _chat.Messages.FirstOrDefaultAsync(
                item => item.ConversationId == conversation.Id && item.ClientMessageId == key,
                cancellationToken);

            if (existing is not null)
            {
                return existing;
            }

            DateTime sentOn = _now.AddDays(-2).AddMinutes(index * 7);
            Message message = type switch
            {
                MessageType.Text => Message.Create(
                    conversation.Id, sender.Id, type,
                    $"SANAD DEMO chat {slot} #{index} [{_marker}]", null, key, sentOn),
                MessageType.File => Message.Create(
                    conversation.Id, sender.Id, type, null, attachment?.Id, key, sentOn),
                _ => Message.Create(
                    conversation.Id, sender.Id, type, null, null, key, sentOn,
                    30.0444, 31.2357, "SANAD DEMO location")
            };

            if (type == MessageType.File && attachment is not null && !attachment.IsBound)
            {
                attachment.Bind();
            }

            _chat.Messages.Add(message);
            conversation.UpdateLastActivity(sentOn);
            await _chat.SaveChangesAsync(cancellationToken);
            return message;
        }

        private async Task EnsureChatReadStateAsync(
            Conversation conversation,
            User user,
            Message? lastRead)
        {
            bool exists = await _chat.ReadStates.AnyAsync(
                item => item.ConversationId == conversation.Id && item.UserId == user.Id,
                cancellationToken);

            if (exists)
            {
                return;
            }

            Guid lastReadId = lastRead?.Id.Value
                ?? await _chat.Messages
                    .Where(item => item.ConversationId == conversation.Id)
                    .OrderBy(item => item.SentOnUtc)
                    .Select(item => item.Id.Value)
                    .LastAsync(cancellationToken);

            _chat.ReadStates.Add(ConversationReadState.Create(
                conversation.Id, user.Id, lastReadId, _now.AddDays(-1)));
            await _chat.SaveChangesAsync(cancellationToken);
        }

        private async Task<bool> EnsureChatOutboxAsync(
            Conversation conversation,
            Message message,
            User recipient)
        {
            bool exists = await _chat.Outbox.AnyAsync(
                item => item.MessageId == message.Id && item.RecipientUserId == recipient.Id,
                cancellationToken);

            if (exists)
            {
                return false;
            }

            ChatOutboxItem item = ChatOutboxItem.Create(
                conversation.Id, message.Id, recipient.Id, message.SentOnUtc);
            Guid token = Guid.NewGuid();
            item.Claim(token, message.SentOnUtc.AddSeconds(5));
            item.MarkPublished(message.SentOnUtc.AddSeconds(6), token);
            _chat.Outbox.Add(item);
            await _chat.SaveChangesAsync(cancellationToken);
            return true;
        }

        private async Task EnsureChatReportAsync(
            Conversation conversation,
            object? openMessages,
            int? messageIndex,
            bool upheld,
            bool dismissed)
        {
            _ = openMessages;
            Message? target = messageIndex is null
                ? null
                : await _chat.Messages
                    .Where(item => item.ConversationId == conversation.Id)
                    .OrderBy(item => item.SentOnUtc)
                    .Skip(messageIndex.Value)
                    .FirstAsync(cancellationToken);

            string reason = dismissed
                ? $"SANAD DEMO resolved report [{_marker}]"
                : $"SANAD DEMO report on message [{_marker}]";

            Report? existing = await _chat.Reports.FirstOrDefaultAsync(
                item => item.ConversationId == conversation.Id && item.Reason == reason,
                cancellationToken);

            if (existing is not null)
            {
                return;
            }

            UserId reporter = _accounts["family.owner"].Id;
            Report report = Report.Create(
                conversation.Id,
                target?.Id,
                reporter,
                reason,
                _now.AddDays(-1));

            if (dismissed)
            {
                report.Dismiss(_accounts["superadmin"].Id, _now.AddHours(-2));
            }
            else if (upheld)
            {
                report.Uphold(_accounts["superadmin"].Id, _now.AddHours(-2));
            }

            _chat.Reports.Add(report);
            await _chat.SaveChangesAsync(cancellationToken);
        }

        // ------------------------------------------------------------------
        // caregiver-bookings
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> CaregiverBookingsAsync()
        {
            await EnsureDependentFixtureAsync();
            const string group = DemoCorpusGroups.CaregiverBookings;

            // The 14 approved rows. Money-bearing end states (a declined paid
            // booking and the seam failed-refund row) resolve to the armed
            // Failed refund shape, which is exactly why this group is
            // seam-gated; the Pending/Ambiguous rows hold a bounded in-flight or
            // unconfirmed attempt and are never retryable.
            (string Slot, string Owner, string Dependent, string Caregiver, BookingCaregiverType Type, BookingShiftType Shift, int DayOffset, int StartHour, int EndHour, string State)[] plan =
            [
                ("pending-payment", "family.owner", "elderly.1", "caregiver.1", BookingCaregiverType.Medical, BookingShiftType.HomeVisit, 2, 10, 12, "pending-payment"),
                ("pending-approval", "family.owner", "elderly.1", "caregiver.2", BookingCaregiverType.Medical, BookingShiftType.HomeVisit, 3, 10, 12, "pending-approval"),
                ("confirmed", "family.owner", "elderly.1", "caregiver.1", BookingCaregiverType.Medical, BookingShiftType.EightHourShift, 4, 9, 17, "confirmed"),
                ("confirmed-companion", "family.owner2", "dependent.3", "caregiver.3", BookingCaregiverType.Companion, BookingShiftType.Hourly, 8, 9, 11, "confirmed"),
                ("in-progress", "family.owner2", "dependent.3", "caregiver.3", BookingCaregiverType.Companion, BookingShiftType.Hourly, 0, 9, 11, "in-progress"),
                ("completed", "family.owner", "elderly.1", "caregiver.1", BookingCaregiverType.Medical, BookingShiftType.HomeVisit, -20, 10, 12, "completed"),
                ("completed-paid-out", "family.owner", "elderly.2", "caregiver.2", BookingCaregiverType.Medical, BookingShiftType.TwelveHourShift, -25, 8, 20, "completed-paid-out"),
                ("declined", "family.owner", "elderly.1", "caregiver.1", BookingCaregiverType.Medical, BookingShiftType.HomeVisit, 5, 10, 12, "declined"),
                ("expired-unpaid", "family.owner2", "dependent.4", "caregiver.4", BookingCaregiverType.Companion, BookingShiftType.Hourly, 6, 14, 16, "expired-unpaid"),
                ("cancelled-family-unpaid", "family.owner2", "dependent.3", "caregiver.3", BookingCaregiverType.Companion, BookingShiftType.Hourly, 7, 10, 12, "cancelled-family-unpaid"),
                ("cancelled-family-refunded", "family.owner", "elderly.1", "caregiver.1", BookingCaregiverType.Medical, BookingShiftType.HomeVisit, -10, 10, 12, "cancelled-family-refunded"),
                ("cancelled-caregiver-refund-pending", "family.owner", "elderly.2", "caregiver.2", BookingCaregiverType.Medical, BookingShiftType.HomeVisit, -12, 10, 12, "cancelled-caregiver-refund-pending"),
                ("cancelled-caregiver-ambiguous", "family.owner", "elderly.1", "caregiver.2", BookingCaregiverType.Medical, BookingShiftType.HomeVisit, -14, 10, 12, "cancelled-caregiver-ambiguous"),
                ("seam-failed-refund", "family.owner", "elderly.2", "caregiver.1", BookingCaregiverType.Medical, BookingShiftType.HomeVisit, -16, 10, 12, "seam-failed-refund")
            ];

            var records = new List<PortalProvisionRecord>();
            foreach ((string slot, string owner, string dependent, string caregiverRole, BookingCaregiverType type, BookingShiftType shift, int dayOffset, int startHour, int endHour, string state) in plan)
            {
                DateTime act = _now.AddDays(dayOffset);
                Booking booking = await EnsureDemoBookingAsync(
                    group, slot, owner, dependent, caregiverRole, type, shift, startHour, endHour, act);
                await DriveCaregiverBookingAsync(group, slot, state, booking, act);
                records.Add(new PortalProvisionRecord(
                    $"Booking:{slot}",
                    booking.Id.ToString(),
                    $"{booking.Status} / refund {BookingRefundStates.Resolve(booking)}"));
            }

            CaregiverPayout? payout = await EnsureCompletedPayoutAsync(group, "completed-paid-out");
            records.Add(new PortalProvisionRecord(
                "CaregiverPayout",
                payout?.Id.ToString() ?? "<none>",
                payout is null ? "skipped" : payout.Status.ToString()));

            return new PortalProvisionGroupResult(
                group,
                Provisioned: true,
                Detail: "14 caregiver bookings spanning every status, the refund states Pending/Ambiguous/Failed/Succeeded and one recorded payout over a completed visit",
                Records: records);
        }

        private async Task<Booking> EnsureDemoBookingAsync(
            string group,
            string slot,
            string ownerRole,
            string dependentRole,
            string caregiverRole,
            BookingCaregiverType type,
            BookingShiftType shift,
            int startHour,
            int endHour,
            DateTime act)
        {
            string marker = DemoCorpusIdentity.RecordKey(_runTag, group, slot);
            Booking? existing = await _families.Bookings
                .FirstOrDefaultAsync(item => item.SpecialInstructions == marker, cancellationToken);

            if (existing is not null)
            {
                return existing;
            }

            Caregiver caregiver = await DemoCaregiverAsync(caregiverRole);
            Family family = _familyByOwnerRole[ownerRole];
            var dependent = _dependents[dependentRole];
            DateOnly date = DateOnly.FromDateTime(act);
            DateTime createdOn = act.AddHours(-1);

            Booking booking = Booking.Create(
                family.Id,
                _accounts[ownerRole].Id,
                dependent.Id,
                caregiver.Id,
                type,
                shift,
                date,
                new TimeOnly(startHour, 0),
                new TimeOnly(endHour, 0),
                "SANAD DEMO service address, Nasr City, Cairo",
                marker,
                BookingPriceSnapshot.Calculate(500m, 15m),
                acceptanceDeadlineUtc: createdOn.AddHours(48),
                currentDate: date,
                createdOnUtc: createdOn);

            _families.Bookings.Add(booking);
            await _families.SaveChangesAsync(cancellationToken);
            return booking;
        }

        private async Task DriveCaregiverBookingAsync(
            string group,
            string slot,
            string state,
            Booking booking,
            DateTime act)
        {
            string orderId = DemoCorpusIdentity.CaregiverOrderId(_runTag, slot);
            string transactionId = DemoCorpusIdentity.CaregiverRefundTransactionId(_runTag, slot);

            async Task ConfirmAsync()
            {
                if (booking.Status == BookingStatus.PendingPayment)
                {
                    PortalProvisionDomainChecks.DrivePendingToConfirmed(booking, orderId, transactionId, act);
                    await _families.SaveChangesAsync(cancellationToken);
                }
            }

            async Task CompleteAsync()
            {
                await ConfirmAsync();
                if (booking.Status == BookingStatus.Confirmed)
                {
                    booking.StartVisit(act.AddMinutes(-1));
                    await _families.SaveChangesAsync(cancellationToken);
                }

                if (booking.Status == BookingStatus.InProgress)
                {
                    booking.CompleteVisit("SANAD DEMO completed caregiver visit notes", act);
                    await _families.SaveChangesAsync(cancellationToken);
                }
            }

            switch (state)
            {
                case "pending-payment":
                    break;

                case "pending-approval":
                    if (booking.Status == BookingStatus.PendingPayment)
                    {
                        booking.RecordPaymentIntent(orderId, PaymentMethod.Card, act.AddMinutes(-4));
                        booking.MarkAsPaid(orderId, transactionId, act.AddMinutes(-3));
                        await _families.SaveChangesAsync(cancellationToken);
                    }

                    break;

                case "confirmed":
                    await ConfirmAsync();
                    break;

                case "in-progress":
                    await ConfirmAsync();
                    if (booking.Status == BookingStatus.Confirmed)
                    {
                        booking.StartVisit(act.AddMinutes(-1));
                        await _families.SaveChangesAsync(cancellationToken);
                    }

                    break;

                case "completed":
                case "completed-paid-out":
                    await CompleteAsync();
                    break;

                case "declined":
                    if (booking.Status == BookingStatus.PendingPayment)
                    {
                        booking.RecordPaymentIntent(orderId, PaymentMethod.Card, act.AddMinutes(-4));
                        booking.MarkAsPaid(orderId, transactionId, act.AddMinutes(-3));
                        await _families.SaveChangesAsync(cancellationToken);
                    }

                    if (booking.Status == BookingStatus.PendingCaregiverApproval)
                    {
                        booking.DeclineByCaregiver("SANAD DEMO caregiver decline", act);
                        await _families.SaveChangesAsync(cancellationToken);
                    }

                    break;

                case "expired-unpaid":
                    if (booking.Status == BookingStatus.PendingPayment)
                    {
                        booking.Expire(act.AddDays(3));
                        await _families.SaveChangesAsync(cancellationToken);
                    }

                    break;

                case "cancelled-family-unpaid":
                    if (booking.Status == BookingStatus.PendingPayment)
                    {
                        booking.CancelByFamily("SANAD DEMO family cancellation before payment", act);
                        await _families.SaveChangesAsync(cancellationToken);
                    }

                    break;

                case "cancelled-family-refunded":
                    await ConfirmAsync();
                    if (booking.Status == BookingStatus.Confirmed)
                    {
                        booking.CancelByFamily("SANAD DEMO family cancellation, refunded", act);
                        await _families.SaveChangesAsync(cancellationToken);
                    }

                    if (booking.Status == BookingStatus.CancelledByFamily && booking.RefundedOnUtc is null)
                    {
                        booking.MarkRefunded($"{orderId}:refund", act.AddMinutes(5));
                        await _families.SaveChangesAsync(cancellationToken);
                    }

                    break;

                case "cancelled-caregiver-refund-pending":
                    await ConfirmAsync();
                    if (booking.Status == BookingStatus.Confirmed)
                    {
                        booking.CancelByCaregiver("SANAD DEMO caregiver cancellation", act);
                        await _families.SaveChangesAsync(cancellationToken);
                    }

                    if (!booking.IsRefundAttemptInFlight
                        && !booking.IsRefundOutcomeAmbiguous
                        && booking.Status == BookingStatus.CancelledByCaregiver
                        && booking.RefundedOnUtc is null)
                    {
                        long award = Math.Min(booking.RefundableCeilingMinorUnits(), 100L);
                        if (award > 0)
                        {
                            booking.ClaimRefundAttempt(Guid.NewGuid(), award, act.AddMinutes(1));
                            await _families.SaveChangesAsync(cancellationToken);
                        }
                    }

                    break;

                case "cancelled-caregiver-ambiguous":
                    await ConfirmAsync();
                    if (booking.Status == BookingStatus.Confirmed)
                    {
                        booking.CancelByCaregiver("SANAD DEMO caregiver cancellation, unconfirmed refund", act);
                        await _families.SaveChangesAsync(cancellationToken);
                    }

                    if (!booking.IsRefundOutcomeAmbiguous
                        && !booking.IsRefundAttemptInFlight
                        && booking.RefundedOnUtc is null)
                    {
                        long award = Math.Min(booking.RefundableCeilingMinorUnits(), 100L);
                        if (award > 0)
                        {
                            booking.ClaimRefundAttempt(Guid.NewGuid(), award, act.AddMinutes(1));
                            // The reason lands in refund_ambiguity_reason
                            // (varchar(64)), so it is kept inside that bound.
                            booking.MarkRefundOutcomeAmbiguous(
                                $"{orderId}:ambiguous",
                                "SANAD DEMO unconfirmed refund; no provider was contacted.",
                                act.AddMinutes(2),
                                award);
                            await _families.SaveChangesAsync(cancellationToken);
                        }
                    }

                    break;

                case "seam-failed-refund":
                    await ConfirmAsync();
                    if (booking.Status == BookingStatus.Confirmed)
                    {
                        PortalProvisionDomainChecks.DriveConfirmedToFailedRefund(booking, act);
                        await _families.SaveChangesAsync(cancellationToken);
                    }

                    // A reused slot seeded before the colon-free id fix keeps its
                    // legacy provider ids on the succeeded transaction (the reuse path
                    // above intentionally no-ops). The admin refund retry selects that
                    // succeeded transaction and sends the id it stores — which the
                    // seam would not intercept — so refusing here is what stops the
                    // run from reporting the slot as protected when it is not.
                    await EnsureSeamArmedIdsConsistentAsync(
                        group, slot, booking, orderId, transactionId, cancellationToken);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"unsupported demo caregiver booking state '{state}'.");
            }
        }

        /// <summary>
        /// Fail-closed guard for a seam-armed caregiver slot, wired into the
        /// <c>seam-failed-refund</c> drive path for BOTH fresh creation and
        /// reuse. It loads the booking's payment transactions and evaluates
        /// the persisted armed ids against the configured ones, informed by
        /// how the admin refund-retry locates its id (first succeeded
        /// transaction with a stored <c>PaymobTransactionId</c>, booking id
        /// as its fallback when no succeeded transaction exists). This guard
        /// is DELIBERATELY STRICTER than the handler's selection: the handler
        /// could still act on the booking fallback alone, while this check
        /// requires the full configured transaction/order/booking-fallback
        /// triple to match for this one seeded provider-call-armed slot. Any
        /// difference REFUSES the run — the slot is reported as NOT
        /// seam-protected — instead of printing an allowlist entry the
        /// persisted data might never send to the seam. The keyed repair
        /// (aligning the persisted locators with the configured id) is an
        /// explicit Owner-authorized data correction, not something a replay
        /// silently papers over.
        /// </summary>
        private async Task EnsureSeamArmedIdsConsistentAsync(
            string group,
            string slot,
            Booking booking,
            string orderId,
            string transactionId,
            CancellationToken cancellationToken)
        {
            await _families.Entry(booking)
                .Collection(item => item.PaymentTransactions)
                .LoadAsync(cancellationToken);

            PaymentTransaction? succeeded = booking.PaymentTransactions.FirstOrDefault(
                item => item.Status == PaymentTransactionStatus.Succeeded
                    && item.PaymobTransactionId is not null);

            string? mismatch = DemoCorpusIdentity.SeamArmedIdMismatch(
                slot,
                orderId,
                transactionId,
                succeeded?.PaymobOrderId,
                succeeded?.PaymobTransactionId,
                booking.PaymobTransactionId);

            if (mismatch is not null)
            {
                throw new InvalidOperationException(
                    $"seam-gated demo group '{group}' slot '{slot}' is NOT seam-protected and the run refuses: "
                        + $"{mismatch}. Align the persisted provider locators with the configured "
                        + "FixtureRefundSeam allowlist entry (an Owner-authorized keyed repair) and re-run; "
                        + "the run made no provider call.");
            }
        }

        private async Task<CaregiverPayout?> EnsureCompletedPayoutAsync(string group, string slot)
        {
            string marker = DemoCorpusIdentity.RecordKey(_runTag, group, slot);
            Booking? booking = await _families.Bookings
                .FirstOrDefaultAsync(item => item.SpecialInstructions == marker, cancellationToken);

            if (booking is null || booking.Status != BookingStatus.Completed)
            {
                return null;
            }

            CaregiverPayout? existing = await _caregivers.Payouts
                .FirstOrDefaultAsync(item => item.BookingId == booking.Id, cancellationToken);

            if (existing is not null)
            {
                return existing;
            }

            Caregiver? caregiver = await _caregivers.Caregivers
                .FirstOrDefaultAsync(item => item.Id == booking.CaregiverId, cancellationToken);

            if (caregiver is null)
            {
                return null;
            }

            CaregiverPayout payout = CaregiverPayout.Record(
                booking.Id,
                caregiver.Id,
                grossAmount: booking.PriceSnapshot.TotalPayableAmount,
                platformFeeAmount: booking.PriceSnapshot.PlatformFeeAmount,
                currency: booking.PriceSnapshot.Currency,
                policyVersion: 1,
                chargeRuleVersion: booking.PriceSnapshot.PlatformChargeRuleVersion,
                bankCode: "CIB",
                // masked_iban is varchar(8); the domain's own mask is "****" + last 4.
                maskedIban: "****4821",
                transferReference: $"{marker}:payout",
                evidence: "SANAD DEMO payout evidence (no real transfer was made)",
                reason: "SANAD DEMO completed caregiver visit payout",
                recordedBy: _accounts["finance.admin"].Id,
                recordedOnUtc: _now.AddDays(-24));

            _caregivers.Payouts.Add(payout);
            await _caregivers.SaveChangesAsync(cancellationToken);
            return payout;
        }

        private async Task<Caregiver> DemoCaregiverAsync(string role)
        {
            await EnsureAccountsLoadedAsync();
            User user = _accounts.TryGetValue(role, out User? account)
                ? account
                : (await EnsureAccountAsync(
                    role,
                    $"مقدم رعاية demo {role}",
                    $"Demo Caregiver {role}",
                    AccountType.MedicalCaregiver,
                    elderlyAccount: false,
                    suspend: false)).User;

            Caregiver? caregiver = await _caregivers.Caregivers
                .FirstOrDefaultAsync(item => item.UserId == user.Id, cancellationToken);

            return caregiver ?? throw new InvalidOperationException(
                $"the caregiver profile for '{role}' is missing; provision caregivers-profiles first.");
        }

        // ------------------------------------------------------------------
        // caregiver-ratings
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> CaregiverRatingsAsync()
        {
            await EnsureDependentFixtureAsync();
            const string group = DemoCorpusGroups.CaregiverRatings;

            // A rating's BookingId is unique, so each of the 8 ratings owns one
            // completed prerequisite booking created here (settled past visits
            // with no refund exposure, so the group stays data-only and needs no
            // seam); the bookings are marker-owned and cannot collide with the
            // caregiver-bookings rows.
            (string Slot, string Caregiver, int Stars, bool Hidden, string Arabic, string English)[] plan =
            [
                ("r1", "caregiver.1", 5, false, "رعاية ممتازة ومهنية عالية، شكراً جزيلاً.", "Excellent and highly professional care."),
                ("r2", "caregiver.1", 4, false, "خدمة جيدة والمواعيد دقيقة.", "Good service with punctual visits."),
                ("r3", "caregiver.2", 5, false, "ممرضة متمكنة وتتعامل بلطف مع كبار السن.", "Skilled nurse, kind with elderly residents."),
                ("r4", "caregiver.3", 4, false, "رفيقة مطمئنة وتتواصل بشكل جيد.", "Reassuring companion with good communication."),
                ("r5", "caregiver.3", 5, false, "ساعدت والدي على ممارسة الأنشطة اليومية.", "Helped my parent with the daily activities."),
                ("r6", "caregiver.4", 3, false, "خدمة مقبولة لكن تحتاج مزيداً من الخبرة.", "Acceptable service, but needs more experience."),
                ("r7", "caregiver.2", 2, false, "تأخرت في الوصول ولم تلتزم بالجدول.", "Arrived late and did not follow the schedule."),
                ("r8", "caregiver.4", 1, true, "مراجعة تجريبية مخفية لأغراض العرض.", "Hidden demo review kept for the moderation queue.")
            ];

            var records = new List<PortalProvisionRecord>();
            var affected = new List<string>();

            foreach ((string slot, string caregiverRole, int stars, bool hidden, string arabic, string english) in plan)
            {
                Booking booking = await EnsureRatingPrerequisiteBookingAsync(slot, caregiverRole);
                Caregiver caregiver = await DemoCaregiverAsync(caregiverRole);
                string reviewText = $"{arabic}\n{english}";

                CaregiverRating? rating = await _caregivers.CaregiverRatings
                    .FirstOrDefaultAsync(item => item.BookingId == booking.Id.Value, cancellationToken);

                if (rating is null)
                {
                    rating = CaregiverRating.Create(
                        caregiver.Id,
                        booking.Id.Value,
                        _familyByOwnerRole["family.owner"].Id,
                        _accounts["family.owner"].Id,
                        stars,
                        reviewText,
                        _now.AddDays(-18));
                    _caregivers.CaregiverRatings.Add(rating);
                    await _caregivers.SaveChangesAsync(cancellationToken);
                }

                if (hidden && !rating.IsHidden)
                {
                    string reason = $"SANAD DEMO moderation hide [{_marker}]";
                    rating.Hide(_accounts["superadmin"].Id, reason, _now.AddDays(-5));
                    _caregivers.RatingModerationActions.Add(CaregiverRatingModerationAction.Record(
                        rating.Id,
                        caregiver.Id,
                        CaregiverRatingModerationActionType.Hidden,
                        _accounts["superadmin"].Id,
                        reason,
                        _now.AddDays(-5)));
                    await _caregivers.SaveChangesAsync(cancellationToken);
                }

                if (!affected.Contains(caregiverRole, StringComparer.Ordinal))
                {
                    affected.Add(caregiverRole);
                }

                records.Add(new PortalProvisionRecord(
                    $"CaregiverRating:{slot}",
                    rating.Id.ToString(),
                    rating.IsHidden ? "hidden" : $"{rating.Stars} stars"));
            }

            // The stored summary is the non-hidden aggregate, exactly as the
            // family re-rating and the admin hide/restore paths recompute it.
            foreach (string caregiverRole in affected)
            {
                Caregiver caregiver = await DemoCaregiverAsync(caregiverRole);
                List<CaregiverRating> ratings = await _caregivers.CaregiverRatings
                    .Where(item => item.CaregiverId == caregiver.Id)
                    .ToListAsync(cancellationToken);
                List<CaregiverRating> visible = ratings
                    .Where(item => item.ModerationState == RatingModerationState.Visible)
                    .ToList();
                decimal average = visible.Count == 0
                    ? 0m
                    : visible.Sum(item => (decimal)item.Stars) / visible.Count;

                caregiver.UpdateFamilyRatingSummary(average, visible.Count, _now);
                await _caregivers.SaveChangesAsync(cancellationToken);
                records.Add(new PortalProvisionRecord(
                    $"CaregiverSummary:{caregiverRole}",
                    caregiver.Id.ToString(),
                    $"{average:0.00} over {visible.Count} visible"));
            }

            return new PortalProvisionGroupResult(
                group,
                Provisioned: true,
                Detail: "8 ratings (7 visible, 1 hidden with its append-only moderation action) over 8 prerequisite completed bookings, with the stored caregiver summaries recomputed over the visible set",
                Records: records);
        }

        private async Task<Booking> EnsureRatingPrerequisiteBookingAsync(string slot, string caregiverRole)
        {
            const string group = DemoCorpusGroups.CaregiverRatings;
            int index = slot.Length > 0 ? slot[^1] - '0' : 0;
            int dayOffset = -30 + index;
            DateTime act = _now.AddDays(dayOffset);
            bool companion = caregiverRole is "caregiver.3" or "caregiver.4";

            Booking booking = await EnsureDemoBookingAsync(
                group,
                $"booking-{slot}",
                "family.owner",
                "elderly.1",
                caregiverRole,
                companion ? BookingCaregiverType.Companion : BookingCaregiverType.Medical,
                companion ? BookingShiftType.Hourly : BookingShiftType.HomeVisit,
                10,
                12,
                act);

            string key = DemoCorpusIdentity.RecordKey(_runTag, group, $"booking-{slot}");

            if (booking.Status == BookingStatus.PendingPayment)
            {
                PortalProvisionDomainChecks.DrivePendingToConfirmed(
                    booking, $"{key}:order", $"{key}:txn", act);
                await _families.SaveChangesAsync(cancellationToken);
            }

            if (booking.Status == BookingStatus.Confirmed)
            {
                booking.StartVisit(act.AddMinutes(-1));
                await _families.SaveChangesAsync(cancellationToken);
            }

            if (booking.Status == BookingStatus.InProgress)
            {
                booking.CompleteVisit("SANAD DEMO completed rating visit notes", act);
                await _families.SaveChangesAsync(cancellationToken);
            }

            return booking;
        }

        // ------------------------------------------------------------------
        // care-home-bookings
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> CareHomeBookingsAsync()
        {
            await EnsureDependentFixtureAsync();
            const string group = DemoCorpusGroups.CareHomeBookings;
            (CareHomeFacility facility, CareHomeRoomType roomType, CareHomeRoom room, CareHomeBed bed) =
                await DemoCareHomeResourcesAsync();
            _ = facility;
            _ = roomType;
            User owner = _accounts["carehome.owner1"];

            // 13 booking slots plus the extension segment off the accepted paid
            // stay: 14 care-home booking rows. The seam failed-refund row and
            // the rejected refund that stays Pending both persist a
            // provider-armed retry shape, which is why this group is
            // seam-gated.
            (string Slot, string State, int DayOffset)[] plan =
            [
                ("pending-payment", "pending-payment", 2),
                ("paid-awaiting-decision", "paid-awaiting-decision", 3),
                ("accepted", "accepted", 4),
                ("rejected-refund-pending", "rejected-refund-pending", 5),
                ("cancelled-unpaid", "cancelled-unpaid", 6),
                ("expired-unpaid", "expired-unpaid", 7),
                ("refund-initiated", "refund-initiated", -10),
                ("refunded", "refunded", -12),
                ("checked-in", "checked-in", -5),
                ("checked-out", "checked-out", -6),
                ("dispute", "dispute", -7),
                ("seam-failed-refund", "seam-failed-refund", -14),
                ("manually-refunded", "manually-refunded", -16)
            ];

            var records = new List<PortalProvisionRecord>();
            foreach ((string slot, string state, int dayOffset) in plan)
            {
                DateTime act = _now.AddDays(dayOffset);
                TimeSpan hold = state == "expired-unpaid"
                    ? TimeSpan.FromMinutes(1)
                    : TimeSpan.FromDays(90);
                CareHomeBooking booking = await EnsureDemoCareHomeBookingAsync(slot, act, hold);
                await DriveCareHomeBookingAsync(group, booking, slot, state, act, owner, room, bed);
                records.Add(new PortalProvisionRecord(
                    $"CareHomeBooking:{slot}",
                    booking.Id.ToString(),
                    $"{booking.Status}/{booking.PaymentStatus}/refund {booking.RefundStatus}"));
            }

            // The extension: created off the accepted paid segment, then paid
            // so it holds the PaidAwaitingDecision state a real extension
            // reaches. The ExtensionOfBookingId link is the idempotency key.
            CareHomeBooking parent = await _careHomes.Bookings.FirstAsync(
                item => item.CareNeedsNotes == DemoCorpusIdentity.RecordKey(_runTag, group, "accepted"),
                cancellationToken);
            CareHomeBooking? extension = await _careHomes.Bookings
                .FirstOrDefaultAsync(item => item.ExtensionOfBookingId == parent.Id, cancellationToken);

            if (extension is null)
            {
                extension = CareHomeBooking.CreateExtension(
                    parent,
                    _accounts["family.owner"].Id,
                    baseAmount: 3500m,
                    feeAmount: 350m,
                    taxAmount: 175m,
                    ruleVersion: 4,
                    _now.AddDays(4).AddMinutes(-2),
                    checkoutHoldDuration: TimeSpan.FromDays(90));
                _careHomes.Bookings.Add(extension);
                await _careHomes.SaveChangesAsync(cancellationToken);
            }

            if (extension.Status == CareHomeBookingStatus.PendingPayment)
            {
                extension.MarkPaid(
                    PortalProvisionIdentity.StableNumber(
                        DemoCorpusIdentity.RecordKey(_runTag, group, "extension")),
                    _now.AddDays(4).AddMinutes(-1));
                await _careHomes.SaveChangesAsync(cancellationToken);
            }

            records.Add(new PortalProvisionRecord(
                "CareHomeBooking:extension",
                extension.Id.ToString(),
                $"{extension.Status}/{extension.PaymentStatus} of {parent.Id}"));

            return new PortalProvisionGroupResult(
                group,
                Provisioned: true,
                Detail: "14 care-home bookings covering every status, the refund lifecycle (Pending/Initiated/Failed/Completed/ManuallyCompleted), check-in, check-out, an open dispute and a paid extension",
                Records: records);
        }

        private async Task<(CareHomeFacility Facility, CareHomeRoomType RoomType, CareHomeRoom Room, CareHomeBed Bed)> DemoCareHomeResourcesAsync()
        {
            await EnsureAccountsLoadedAsync();
            CareHomeFacility facility = await _careHomes.Facilities
                .FirstOrDefaultAsync(
                    item => item.OwnerUserId == _accounts["carehome.owner1"].Id,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "the approved demo facility is missing; provision care-home-facilities first.");
            CareHomeRoomType roomType = await _careHomes.RoomTypes
                .FirstOrDefaultAsync(item => item.FacilityId == facility.Id, cancellationToken)
                ?? throw new InvalidOperationException(
                    "demo room types are missing; provision care-home-inventory first.");
            CareHomeRoom room = await _careHomes.Rooms
                .FirstOrDefaultAsync(item => item.FacilityId == facility.Id, cancellationToken)
                ?? throw new InvalidOperationException(
                    "demo rooms are missing; provision care-home-inventory first.");
            CareHomeBed bed = await _careHomes.Beds
                .FirstOrDefaultAsync(item => item.RoomId == room.Id, cancellationToken)
                ?? throw new InvalidOperationException(
                    "demo beds are missing; provision care-home-inventory first.");

            return (facility, roomType, room, bed);
        }

        private async Task<CareHomeBooking> EnsureDemoCareHomeBookingAsync(
            string slot,
            DateTime act,
            TimeSpan checkoutHold)
        {
            const string group = DemoCorpusGroups.CareHomeBookings;
            string marker = DemoCorpusIdentity.RecordKey(_runTag, group, slot);
            CareHomeBooking? existing = await _careHomes.Bookings
                .FirstOrDefaultAsync(item => item.CareNeedsNotes == marker, cancellationToken);

            if (existing is not null)
            {
                return existing;
            }

            (CareHomeFacility facility, CareHomeRoomType roomType, _, _) =
                await DemoCareHomeResourcesAsync();

            CareHomeBooking booking = CareHomeBooking.Create(
                facility.Id,
                _accounts["family.owner"].Id,
                _familyByOwnerRole["family.owner"].Id,
                _dependents["elderly.1"].Id,
                roomType.Id,
                DateOnly.FromDateTime(act.AddDays(2)),
                baseAmount: 3500m,
                feeAmount: 350m,
                taxAmount: 175m,
                ruleVersion: 4,
                arabicName: "الجد التجريبي",
                englishName: $"SANAD DEMO Resident {_marker}",
                age: 78,
                medicalJson: null,
                contactName: "SANAD DEMO Family Owner",
                contactPhone: "+201000000000",
                relationship: "Grandfather",
                careNotes: marker,
                utcNow: act,
                checkoutHoldDuration: checkoutHold);

            _careHomes.Bookings.Add(booking);
            await _careHomes.SaveChangesAsync(cancellationToken);
            return booking;
        }

        private async Task DriveCareHomeBookingAsync(
            string group,
            CareHomeBooking booking,
            string slot,
            string state,
            DateTime act,
            User owner,
            CareHomeRoom room,
            CareHomeBed bed)
        {
            string marker = DemoCorpusIdentity.RecordKey(_runTag, group, slot);
            long transactionId = PortalProvisionIdentity.StableNumber(marker);
            string refundReference = $"{marker}:refund";

            async Task PayAsync(DateTime when)
            {
                if (booking.Status == CareHomeBookingStatus.PendingPayment)
                {
                    booking.MarkPaid(transactionId, when);
                    await _careHomes.SaveChangesAsync(cancellationToken);
                }
            }

            async Task AcceptAsync(DateTime when)
            {
                await PayAsync(when.AddMinutes(-1));
                if (booking.Status == CareHomeBookingStatus.PaidAwaitingDecision)
                {
                    booking.Accept(when);
                    await _careHomes.SaveChangesAsync(cancellationToken);
                }
            }

            async Task RejectAsync(DateTime when)
            {
                await PayAsync(when.AddMinutes(-1));
                if (booking.Status == CareHomeBookingStatus.PaidAwaitingDecision)
                {
                    booking.Reject("SANAD DEMO facility rejection", when);
                    await _careHomes.SaveChangesAsync(cancellationToken);
                }
            }

            async Task CheckInAsync(DateTime when)
            {
                await AcceptAsync(when.AddMinutes(-10));
                if (booking.Status == CareHomeBookingStatus.Accepted && booking.AssignedRoomId is null)
                {
                    booking.AssignPhysicalResource(room.Id, bed.Id, when.AddMinutes(-6));
                    await _careHomes.SaveChangesAsync(cancellationToken);
                }

                if (booking.Status == CareHomeBookingStatus.Accepted && booking.ActualCheckInOnUtc is null)
                {
                    booking.RecordCheckIn(owner.Id, when.AddMinutes(-5));
                    await _careHomes.SaveChangesAsync(cancellationToken);
                }
            }

            switch (state)
            {
                case "pending-payment":
                    break;

                case "paid-awaiting-decision":
                    await PayAsync(act.AddMinutes(-30));
                    break;

                case "accepted":
                    await AcceptAsync(act.AddMinutes(-30));
                    break;

                case "rejected-refund-pending":
                    await RejectAsync(act.AddMinutes(-30));
                    break;

                case "cancelled-unpaid":
                    if (booking.Status == CareHomeBookingStatus.PendingPayment)
                    {
                        booking.CancelByFamily(act);
                        await _careHomes.SaveChangesAsync(cancellationToken);
                    }

                    break;

                case "expired-unpaid":
                    if (booking.Status == CareHomeBookingStatus.PendingPayment)
                    {
                        booking.ExpireIfNeeded(act.AddHours(1));
                        await _careHomes.SaveChangesAsync(cancellationToken);
                    }

                    break;

                case "refund-initiated":
                    await RejectAsync(act.AddMinutes(-30));
                    if (booking.Status == CareHomeBookingStatus.RefundPending
                        && booking.RefundClaimedOnUtc is null
                        && !booking.IsRefundOutcomeAmbiguous)
                    {
                        if (booking.TryClaimRefund(act.AddMinutes(-29)))
                        {
                            booking.MarkRefundInitiated(refundReference, act.AddMinutes(-28));
                            await _careHomes.SaveChangesAsync(cancellationToken);
                        }
                    }

                    break;

                case "refunded":
                    await RejectAsync(act.AddMinutes(-30));
                    if (booking.Status == CareHomeBookingStatus.RefundPending
                        && booking.RefundClaimedOnUtc is null
                        && !booking.IsRefundOutcomeAmbiguous)
                    {
                        if (booking.TryClaimRefund(act.AddMinutes(-29)))
                        {
                            booking.MarkRefundInitiated(refundReference, act.AddMinutes(-28));
                            await _careHomes.SaveChangesAsync(cancellationToken);
                        }
                    }

                    if (booking.RefundStatus == CareHomeRefundStatus.Initiated)
                    {
                        booking.MarkRefundCompleted(refundReference, act.AddMinutes(-27));
                        await _careHomes.SaveChangesAsync(cancellationToken);
                    }

                    break;

                case "checked-in":
                    await CheckInAsync(act);
                    break;

                case "checked-out":
                    await CheckInAsync(act);
                    if (booking.ActualCheckInOnUtc is not null && booking.ActualCheckOutOnUtc is null)
                    {
                        booking.RecordCheckOut(owner.Id, act.AddMinutes(-3));
                        await _careHomes.SaveChangesAsync(cancellationToken);
                    }

                    break;

                case "dispute":
                    await CheckInAsync(act);
                    if (booking.ActualCheckInOnUtc is not null && booking.ActualCheckOutOnUtc is null)
                    {
                        booking.RecordCheckOut(owner.Id, act.AddMinutes(-3));
                        await _careHomes.SaveChangesAsync(cancellationToken);

                        CareHomeCheckInDispute dispute = CareHomeCheckInDispute.Open(
                            booking.Id,
                            booking.FacilityId,
                            checkoutOnUtc: booking.ActualCheckOutOnUtc,
                            _accounts["family.owner"].Id,
                            act.AddMinutes(-2),
                            "SANAD DEMO checkout dispute: the family confirmation is missing.");
                        _careHomes.CheckInDisputes.Add(dispute);
                        await _careHomes.SaveChangesAsync(cancellationToken);
                    }

                    break;

                case "seam-failed-refund":
                    await RejectAsync(act.AddMinutes(-30));
                    if (booking.Status == CareHomeBookingStatus.RefundPending
                        && booking.RefundClaimedOnUtc is null
                        && !booking.IsRefundOutcomeAmbiguous)
                    {
                        if (booking.TryClaimRefund(act.AddMinutes(-29)))
                        {
                            booking.MarkRefundFailed(
                                "SANAD DEMO scripted refund rejection (seam; no provider call).",
                                act.AddMinutes(-28));
                            await _careHomes.SaveChangesAsync(cancellationToken);
                        }
                    }

                    break;

                case "manually-refunded":
                    await RejectAsync(act.AddMinutes(-30));
                    if (booking.RefundStatus is CareHomeRefundStatus.Pending
                        && booking.RefundClaimedOnUtc is null)
                    {
                        booking.MarkRefundManuallyCompleted(
                            refundReference,
                            "SANAD DEMO manual refund completed outside the provider.",
                            _accounts["finance.admin"].Id,
                            act.AddMinutes(-28));
                        await _careHomes.SaveChangesAsync(cancellationToken);
                    }

                    break;

                default:
                    throw new InvalidOperationException(
                        $"unsupported demo care-home booking state '{state}'.");
            }
        }

        // ------------------------------------------------------------------
        // finance-platform
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> FinancePlatformAsync()
        {
            await EnsureDependentFixtureAsync();
            const string group = DemoCorpusGroups.FinancePlatform;
            var records = new List<PortalProvisionRecord>();

            PlatformChargeRule rule = await EnsureActiveChargeRuleAsync();
            records.Add(new PortalProvisionRecord(
                "PlatformChargeRule",
                rule.Id.ToString(),
                $"active v{rule.Version} fee {rule.PlatformFeeRatePercentage.ToString(CultureInfo.InvariantCulture)}% "
                    + $"tax {rule.TaxRatePercentage.ToString(CultureInfo.InvariantCulture)}% (reused, never overwritten)"));

            _premiumPlan = await EnsurePlanVersionAsync(
                SubscriptionPlan.PremiumKey, SubscriptionPlan.Premium,
                isPublished: true, isAvailableForNewSales: false);

            Family family = await EnsureFamilyAsync("family.owner", "SANAD DEMO Family");
            FamilySubscription subscription = await EnsureCurrentPremiumSubscriptionAsync(family, createdOffsetDays: -20);

            var invoiceService = new SubscriptionInvoiceService(_families, _media.Storage);
            DateTime anchor = subscription.CreatedOnUtc;
            decimal feeAmount = FeeAmount(rule, _premiumPlan.Price);
            var attemptIds = new List<Guid>();

            for (int index = 1; index <= 8; index++)
            {
                string slot = $"invoice-{index}";
                string orderId = DemoCorpusIdentity.RecordKey(_runTag, group, $"{slot}-order");
                string transactionId = DemoCorpusIdentity.RecordKey(_runTag, group, $"{slot}-txn");
                string providerEventId = $"{transactionId}:event";
                DateTime periodStart;
                DateTime periodEnd;
                if (index == 1)
                {
                    periodStart = anchor;
                    periodEnd = subscription.CurrentPeriodEndsOnUtc;
                }
                else
                {
                    int offset = index - 9;
                    periodStart = anchor.AddMonths(offset);
                    periodEnd = anchor.AddMonths(offset + 1);
                }

                // One distinct succeeded capture per invoice: the invoice's
                // PaymentAttemptId is unique filtered, so the initial purchase
                // and every renewal own their own payment attempt exactly as the
                // real checkout/renewal flow produces them.
                SubscriptionPaymentAttempt? attempt = await _families.SubscriptionPaymentAttempts
                    .FirstOrDefaultAsync(item => item.PaymobOrderId == orderId, cancellationToken);

                if (attempt is null)
                {
                    DateTime created = index == 1 ? anchor : periodStart;
                    attempt = index == 1
                        ? SubscriptionPaymentAttempt.Create(
                            family.Id, _premiumPlan,
                            basePrice: _premiumPlan.Price,
                            discountPercentage: 0m,
                            discountAmount: 0m,
                            taxableAmount: _premiumPlan.Price,
                            taxRatePercentage: 0m,
                            taxAmount: 0m,
                            totalPayable: _premiumPlan.Price,
                            couponCode: null,
                            method: SubscriptionPaymentMethod.Card,
                            createdOnUtc: created)
                        : SubscriptionPaymentAttempt.CreateRenewal(
                            subscription, _premiumPlan,
                            platformFeeRatePercentage: rule.PlatformFeeRatePercentage,
                            platformFeeAmount: feeAmount,
                            taxRatePercentage: 0m,
                            taxAmount: 0m,
                            totalPayable: _premiumPlan.Price,
                            platformChargeRuleVersion: rule.Version,
                            method: SubscriptionPaymentMethod.Card,
                            createdOnUtc: created,
                            providerEventId: providerEventId);

                    attempt.RecordPaymobOrder(orderId);
                    Require(
                        attempt.TryMarkSucceeded(transactionId, created.AddMinutes(1)),
                        $"the demo platform-payment capture '{slot}' could not be marked succeeded.");
                    if (index == 1)
                    {
                        attempt.LinkSubscription(subscription.Id);
                        attempt.CapturePlatformCharge(rule.PlatformFeeRatePercentage, feeAmount, rule.Version);
                    }

                    _families.SubscriptionPaymentAttempts.Add(attempt);
                    await _families.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    Require(
                        string.Equals(attempt.PaymobTransactionId, transactionId, StringComparison.Ordinal),
                        $"the stored platform-payment capture '{slot}' transaction id {attempt.PaymobTransactionId} is not "
                            + $"this run's deterministic id {transactionId}; refusing to reuse a foreign capture.");
                }

                Result invoice = index == 1
                    ? await invoiceService.CreateInitialAsync(
                        attempt, subscription, periodStart, periodEnd, cancellationToken)
                    : await invoiceService.CreateRenewalAsync(
                        attempt, subscription, periodStart, periodEnd, providerEventId, cancellationToken);
                if (invoice.IsFailure)
                {
                    throw new InvalidOperationException(
                        $"the demo subscription invoice '{slot}' was refused: {invoice.Error.Code}");
                }

                // The invoice service only adds the row to the context, so it is
                // flushed here: the trailing read-back queries the database (not
                // the change tracker), and without this save the last capture's
                // invoice would never be persisted.
                await _families.SaveChangesAsync(cancellationToken);

                attemptIds.Add(attempt.Id);
                records.Add(new PortalProvisionRecord(
                    $"SubscriptionPaymentAttempt:{slot}",
                    attempt.Id.ToString(),
                    index == 1 ? "Succeeded (initial purchase)" : "Succeeded (renewal)"));
            }

            List<SubscriptionInvoice> invoices = await _families.SubscriptionInvoices.AsNoTracking()
                .Where(item => item.PaymentAttemptId != null && attemptIds.Contains(item.PaymentAttemptId!.Value))
                .ToListAsync(cancellationToken);
            Require(
                invoices.Count == 8,
                $"the platform invoices read back as {invoices.Count} rows for the 8 captures, not one invoice per capture.");
            foreach (SubscriptionInvoice stored in invoices.OrderBy(item => item.InvoiceNumber, StringComparer.Ordinal))
            {
                records.Add(new PortalProvisionRecord(
                    $"SubscriptionInvoice:{stored.InvoiceNumber}",
                    stored.Id.ToString(),
                    $"{stored.Kind} {stored.TotalPayable.ToString(CultureInfo.InvariantCulture)} {stored.Currency}"));
            }

            return new PortalProvisionGroupResult(
                group,
                Provisioned: true,
                Detail: $"the active platform charge rule (v{rule.Version}, reused and never overwritten) plus 8 subscription "
                    + "invoices with their own succeeded captures on the seeded current premium subscription",
                Records: records);
        }

        /// <summary>
        /// Reuse the one active <see cref="PlatformChargeRule"/> the runtime
        /// reads, or append a new version if none is active. Never deactivates,
        /// mutates or overwrites an existing rule: the reader's own invariant is
        /// one active rule, so a second would violate
        /// <c>ux_platform_charge_rules_active</c>.
        /// </summary>
        private async Task<PlatformChargeRule> EnsureActiveChargeRuleAsync()
        {
            if (_activeChargeRule is not null)
            {
                return _activeChargeRule;
            }

            PlatformChargeRule? active = await _finance.PlatformChargeRules
                .Where(item => item.IsActive)
                .OrderByDescending(item => item.Version)
                .FirstOrDefaultAsync(cancellationToken);

            if (active is null)
            {
                int nextVersion =
                    (await _finance.PlatformChargeRules
                        .Select(item => (int?)item.Version)
                        .MaxAsync(cancellationToken) ?? 0) + 1;
                active = PlatformChargeRule.Create(
                    fee: 2.00m,
                    tax: 14.00m,
                    version: nextVersion,
                    effectiveOnUtc: _now.AddDays(-90),
                    createdOnUtc: _now.AddDays(-90),
                    isActive: true);
                _finance.PlatformChargeRules.Add(active);
                await _finance.SaveChangesAsync(cancellationToken);
            }

            _activeChargeRule = active;
            return active;
        }

        private static decimal FeeAmount(PlatformChargeRule rule, decimal basePrice) =>
            decimal.Round(basePrice * rule.PlatformFeeRatePercentage / 100m, 2, MidpointRounding.ToEven);

        /// <summary>
        /// The family's one current premium subscription, created (with its
        /// settled period snapshot) only when the family has none. A stored
        /// current subscription under a different plan is refused, never adopted.
        /// </summary>
        private async Task<FamilySubscription> EnsureCurrentPremiumSubscriptionAsync(Family family, int createdOffsetDays)
        {
            if (_premiumPlan is null)
            {
                _premiumPlan = await EnsurePlanVersionAsync(
                    SubscriptionPlan.PremiumKey, SubscriptionPlan.Premium,
                    isPublished: true, isAvailableForNewSales: false);
            }

            FamilySubscription? existing = await _families.FamilySubscriptions
                .FirstOrDefaultAsync(item => item.FamilyId == family.Id && item.IsCurrent, cancellationToken);

            if (existing is not null)
            {
                Require(
                    string.Equals(existing.PlanKey, SubscriptionPlan.PremiumKey, StringComparison.Ordinal),
                    $"the stored current subscription for family {family.Id} is on plan '{existing.PlanKey}', not the "
                        + $"premium plan; refusing to attach a demo capture to it.");
                return existing;
            }

            DateTime created = _now.AddDays(createdOffsetDays);
            FamilySubscription subscription = FamilySubscription.Create(family.Id, _premiumPlan, created);
            subscription.SetCurrentPeriodSettlement(_premiumPlan.Price, taxRatePercentage: 0m);
            _families.FamilySubscriptions.Add(subscription);
            await _families.SaveChangesAsync(cancellationToken);
            return subscription;
        }

        /// <summary>
        /// One dedicated demo capture slot: its own owner login, family, current
        /// premium subscription and a succeeded payment attempt whose provider
        /// transaction id is the deterministic id the plan allowlists. Exactly
        /// the mirror of the fixture's seam claim slot.
        /// </summary>
        private async Task<(FamilySubscription Subscription, SubscriptionPaymentAttempt Attempt)> EnsureCaptureSlotAsync(
            string slot,
            string role,
            string transactionId,
            string orderId,
            int createdOffsetDays)
        {
            if (_captureSlots.TryGetValue(slot, out (FamilySubscription, SubscriptionPaymentAttempt) ready))
            {
                return ready;
            }

            await EnsureDemoOwnerAsync(role);
            Family family = await EnsureFamilyAsync(role, $"SANAD DEMO {slot} family");
            FamilySubscription subscription = await EnsureCurrentPremiumSubscriptionAsync(family, createdOffsetDays);

            SubscriptionPaymentAttempt? attempt = await _families.SubscriptionPaymentAttempts
                .FirstOrDefaultAsync(
                    item => item.SubscriptionId == subscription.Id
                        && item.Status == SubscriptionPaymentAttemptStatus.Succeeded,
                    cancellationToken);

            if (attempt is null)
            {
                PlatformChargeRule rule = await EnsureActiveChargeRuleAsync();
                DateTime created = _now.AddDays(createdOffsetDays);
                attempt = SubscriptionPaymentAttempt.Create(
                    family.Id, _premiumPlan,
                    basePrice: _premiumPlan.Price,
                    discountPercentage: 0m,
                    discountAmount: 0m,
                    taxableAmount: _premiumPlan.Price,
                    taxRatePercentage: 0m,
                    taxAmount: 0m,
                    totalPayable: _premiumPlan.Price,
                    couponCode: null,
                    method: SubscriptionPaymentMethod.Card,
                    createdOnUtc: created);
                attempt.RecordPaymobOrder(orderId);
                Require(
                    attempt.TryMarkSucceeded(transactionId, created.AddMinutes(1)),
                    $"the demo capture slot '{slot}' could not be marked succeeded.");
                attempt.LinkSubscription(subscription.Id);
                attempt.CapturePlatformCharge(
                    rule.PlatformFeeRatePercentage, FeeAmount(rule, _premiumPlan.Price), rule.Version);
                _families.SubscriptionPaymentAttempts.Add(attempt);
                await _families.SaveChangesAsync(cancellationToken);
            }
            else
            {
                // Exact allowlist invariant: the persisted capture id must be
                // this run's colon-free provider id, byte-for-byte. Legacy
                // colon-bearing ids are never accepted; a target carrying them
                // must be repaired to the colon-free form before provisioning.
                Require(
                    string.Equals(attempt.PaymobTransactionId, transactionId, StringComparison.Ordinal),
                    $"the capture slot '{slot}' transaction id {attempt.PaymobTransactionId} is not this run's "
                        + $"deterministic allowlist id {transactionId}.");
            }

            _captureSlots[slot] = (subscription, attempt);
            return (subscription, attempt);
        }

        private async Task<User> EnsureDemoOwnerAsync(string role)
        {
            if (_accounts.TryGetValue(role, out User? cached))
            {
                return cached;
            }

            (User user, _) = await EnsureAccountAsync(
                role,
                $"مالك عائلة demo {role}",
                $"Demo Owner {role}",
                AccountType.Family,
                elderlyAccount: false,
                suspend: false);
            _accounts[role] = user;
            return user;
        }

        // ------------------------------------------------------------------
        // finance-claims
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> FinanceClaimsAsync()
        {
            await EnsureDependentFixtureAsync();
            const string group = DemoCorpusGroups.FinanceClaims;
            var records = new List<PortalProvisionRecord>();

            // Four claims, each on its OWN succeeded capture so the approval's
            // full-basis reservation never competes with another claim for one
            // capture's refund budget. Every capture's provider transaction id is
            // the deterministic id the plan allowlists: Submitted / Approved and
            // Failed resolve to a scripted Rejected, Ambiguous to Unknown.
            (string Slot, string Role, string Outcome)[] plan =
            [
                ("submitted", "seam.claim.submitted", "submitted"),
                ("approved", "seam.claim.approved", "approved"),
                ("ambiguous", "seam.claim.ambiguous", "ambiguous"),
                ("failed", "seam.claim.failed", "failed")
            ];

            foreach ((string slot, string role, string outcome) in plan)
            {
                string transactionId = DemoCorpusIdentity.ClaimCaptureTransactionId(_runTag, slot);
                string idempotencyKey = DemoCorpusIdentity.ClaimIdempotencyKey(_runTag, slot);
                (FamilySubscription subscription, SubscriptionPaymentAttempt attempt) = await EnsureCaptureSlotAsync(
                    slot, role, transactionId, DemoCorpusIdentity.ClaimCaptureOrderId(_runTag, slot), createdOffsetDays: -12);
                _ = subscription;

                DuplicateChargeClaim claim = await EnsureDuplicateChargeClaimAsync(
                    slot, role, idempotencyKey, attempt, outcome);

                records.Add(new PortalProvisionRecord(
                    $"SubscriptionPaymentAttempt:{slot}",
                    attempt.Id.ToString(),
                    $"Succeeded ({transactionId})"));
                records.Add(new PortalProvisionRecord(
                    $"DuplicateChargeClaim:{slot}",
                    claim.Id.ToString(),
                    $"{claim.State}/{claim.ExecutionState}"));
            }

            (string Slot, string Role)[] shutdowns =
            [
                ("shutdown-1", "shutdown.owner"),
                ("shutdown-2", "shutdown.owner2")
            ];

            // Two service-shutdown refunds, each on its own owner login, family,
            // current premium subscription and succeeded capture (the one-current-
            // subscription-per-family index forbids two on one family). The
            // recorder path moves no provider money, so this stays data-only.
            foreach ((string slot, string role) in shutdowns)
            {
                (FamilySubscription subscription, SubscriptionPaymentAttempt attempt, Guid refundCaseId, long award) =
                    await EnsureShutdownRefundAsync(slot, role);
                records.Add(new PortalProvisionRecord(
                    $"FamilySubscription:{slot}",
                    subscription.Id.ToString(),
                    subscription.PlanKey));
                records.Add(new PortalProvisionRecord(
                    $"SubscriptionPaymentAttempt:{slot}",
                    attempt.Id.ToString(),
                    $"Succeeded ({attempt.PaymobTransactionId})"));
                records.Add(new PortalProvisionRecord(
                    $"SubscriptionRefundRequest:{slot}",
                    refundCaseId.ToString(),
                    $"ServiceShutdown award {award.ToString(CultureInfo.InvariantCulture)} minor units"));
            }

            return new PortalProvisionGroupResult(
                group,
                Provisioned: true,
                Detail: "4 duplicate-charge claims (Submitted, Approved-awaiting-execution, Ambiguous, Failed) each on its own "
                    + "succeeded capture, plus 2 service-shutdown refunds on their own owner families",
                Records: records);
        }

        private async Task<DuplicateChargeClaim> EnsureDuplicateChargeClaimAsync(
            string slot,
            string role,
            string idempotencyKey,
            SubscriptionPaymentAttempt attempt,
            string outcome)
        {
            DuplicateChargeClaim? existing = await _finance.DuplicateChargeClaims.AsNoTracking()
                .FirstOrDefaultAsync(item => item.IdempotencyKey == idempotencyKey, cancellationToken);

            DuplicateChargeClaim claim;
            if (existing is not null)
            {
                Require(
                    existing.PaymentAttemptId == attempt.Id,
                    $"the stored demo claim '{slot}' points at a foreign capture; refusing to touch it.");
                claim = existing;
            }
            else
            {
                var command = new SubmitDuplicateChargeClaimCommand(
                    _accounts[role].Id,
                    attempt.Id,
                    IdempotencyKey: idempotencyKey,
                    EvidenceReference: $"{idempotencyKey}:evidence",
                    CustomerReportedOnUtc: _now.AddMinutes(-40));

                // Direct handler construction — the store's refund-gateway seam
                // is a REFUSING stub here, so submission itself can never reach a
                // provider; submission never executes.
                Result<FamilyDuplicateChargeClaimView> submitted = await
                    new SubmitDuplicateChargeClaimCommandHandler(
                        _families,
                        new DuplicateChargeClaimStore(
                            _finance,
                            new SubscriptionRefundRecorder(_finance),
                            RefusingRefundGateway.Instance,
                            new ProvisionerClock(_now)),
                        new ProvisionerClock(_now))
                        .Handle(command, cancellationToken);
                if (!submitted.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"the demo duplicate-charge submission '{slot}' was refused: {submitted.Error.Code}");
                }

                claim = await _finance.DuplicateChargeClaims.AsNoTracking()
                    .FirstAsync(item => item.Id == submitted.Value.ClaimId, cancellationToken);
            }

            if (outcome == "submitted")
            {
                Require(
                    claim.State == DuplicateChargeClaimState.Submitted,
                    $"the demo claim '{slot}' read back as {claim.State}; expected Submitted.");
                return claim;
            }

            if (claim.State != DuplicateChargeClaimState.Approved)
            {
                var approvalStore = new DuplicateChargeClaimStore(
                    _finance,
                    new SubscriptionRefundRecorder(_finance),
                    RefusingRefundGateway.Instance,
                    new ProvisionerClock(_now));
                Result<DuplicateChargeClaimAdminView> approved = await approvalStore.ApproveAsync(
                    claim.Id,
                    _accounts["superadmin"].Id.Value,
                    AccountType.SuperAdmin.ToString(),
                    _now,
                    verifiedBasis: "SANAD DEMO synthetic basis: the provider outcome is scripted by the refund seam.",
                    decisionNote: idempotencyKey,
                    familyReason: null,
                    cancellationToken);
                if (!approved.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"the demo duplicate-charge approval '{slot}' was refused: {approved.Error.Code}");
                }

                claim = await _finance.DuplicateChargeClaims.AsNoTracking()
                    .FirstAsync(item => item.Id == claim.Id, cancellationToken);
            }

            if (outcome == "approved")
            {
                Require(
                    claim.State == DuplicateChargeClaimState.Approved
                        && claim.ExecutionState == DuplicateChargeExecutionState.AwaitingExecution,
                    $"the demo claim '{slot}' read back as {claim.State}/{claim.ExecutionState}; expected "
                        + "Approved/AwaitingExecution.");
                return claim;
            }

            if (claim.ExecutionState is DuplicateChargeExecutionState.None or DuplicateChargeExecutionState.AwaitingExecution)
            {
                // In-process scripted execution off the SAME shape the real
                // gateway produces: Failed for a definitive refusal, Ambiguous
                // for a lost refund response. No provider is contacted.
                ISubscriptionRefundGateway gateway = outcome == "ambiguous"
                    ? new ScriptedUnknownRefundGateway(attempt.PaymobTransactionId!)
                    : new ScriptedRejectingRefundGateway(attempt.PaymobTransactionId!);
                var executionStore = new DuplicateChargeClaimStore(
                    _finance,
                    new SubscriptionRefundRecorder(_finance),
                    gateway,
                    new ProvisionerClock(_now));
                Result<DuplicateChargeClaimAdminView> executed = await executionStore.ExecuteAsync(
                    claim.Id,
                    expectedExecutionAttemptId: null,
                    _accounts["superadmin"].Id.Value,
                    AccountType.SuperAdmin.ToString(),
                    _now,
                    cancellationToken);
                if (!executed.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"the demo duplicate-charge execution '{slot}' was refused: {executed.Error.Code}");
                }

                claim = await _finance.DuplicateChargeClaims.AsNoTracking()
                    .FirstAsync(item => item.Id == claim.Id, cancellationToken);
            }

            DuplicateChargeExecutionState expected = outcome == "ambiguous"
                ? DuplicateChargeExecutionState.Ambiguous
                : DuplicateChargeExecutionState.Failed;
            Require(
                claim.State == DuplicateChargeClaimState.Approved && claim.ExecutionState == expected,
                $"the demo claim '{slot}' read back as {claim.State}/{claim.ExecutionState}; expected "
                    + $"Approved/{expected}.");
            return claim;
        }

        private async Task<(FamilySubscription Subscription, SubscriptionPaymentAttempt Attempt, Guid RefundCaseId, long AwardMinorUnits)>
            EnsureShutdownRefundAsync(string slot, string role)
        {
            const string group = DemoCorpusGroups.FinanceClaims;
            string key = DemoCorpusIdentity.RecordKey(_runTag, group, $"{slot}-refund");
            string transactionId = DemoCorpusIdentity.RecordKey(_runTag, group, $"{slot}-capture");
            (FamilySubscription subscription, SubscriptionPaymentAttempt attempt) = await EnsureCaptureSlotAsync(
                slot, role, transactionId, $"{transactionId}:order", createdOffsetDays: -10);

            SubscriptionRefundRequest? existing = await _finance.SubscriptionRefundRequests.AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.SubscriptionId == subscription.Id && item.IdempotencyKey == key,
                    cancellationToken);
            if (existing is not null)
            {
                return (subscription, attempt, existing.RefundCaseId, existing.RequestedAmountMinorUnits);
            }

            var command = new RefundSubscriptionServiceShutdownCommand(
                subscription.Id,
                _accounts[role].Id,
                ShutdownOnUtc: _now,
                IdempotencyKey: key,
                EvidenceReference: $"{key}:evidence");

            // The recorder path only: no gateway seam, so no provider could ever
            // be reached even by a bug in this group.
            Result<SubscriptionRefundResponse> result = await
                new RefundSubscriptionServiceShutdownCommandHandler(
                    _families, new SubscriptionRefundRecorder(_finance))
                    .Handle(command, cancellationToken);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"the demo service-shutdown refund '{slot}' was refused: {result.Error.Code}");
            }

            Require(
                result.Value.AwardedMinorUnits > 0,
                $"the demo service-shutdown refund '{slot}' computed a {result.Value.AwardedMinorUnits} award; a successful "
                    + "shutdown refund requires a positive prorated movement.");
            return (subscription, attempt, result.Value.RefundCaseId, result.Value.AwardedMinorUnits);
        }

        // ------------------------------------------------------------------
        // finance-complaints
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> FinanceComplaintsAsync()
        {
            await EnsureDependentFixtureAsync();
            const string group = DemoCorpusGroups.FinanceComplaints;
            var records = new List<PortalProvisionRecord>();

            // Four reviewed complaints, each on a DEDICATED deterministic refund
            // case (one review per case is enforced by
            // ux_complaint_reviews_refund_case_id, so the four can never share one
            // and can never collide with a real capture-derived case).
            (string Slot, string Outcome)[] plan =
            [
                ("received", "received"),
                ("investigating", "investigating"),
                ("resolved", "resolved"),
                ("refused", "refused")
            ];

            string markerPrefix = DemoCorpusIdentity.GroupMarker(_runTag, group);
            foreach ((string slot, string outcome) in plan)
            {
                string marker = $"{markerPrefix}:{slot}";
                ComplaintReview? review = await _finance.ComplaintReviews
                    .FirstOrDefaultAsync(item => item.Description == marker, cancellationToken);

                if (review is null)
                {
                    Guid refundCaseId = DemoCorpusIdentity.ComplaintRefundCaseId(_runTag, slot);
                    Require(
                        !await _finance.ComplaintReviews
                            .AnyAsync(item => item.RefundCaseId == refundCaseId, cancellationToken),
                        $"the demo complaint '{slot}' derived refund case {refundCaseId} is already in use; refusing to "
                            + "share one case between two reviews.");

                    review = ComplaintReview.Open(
                        refundCaseId: refundCaseId,
                        familyId: _familyByOwnerRole["family.owner"].Id.Value,
                        reportingParty: ComplaintReportingParty.Family,
                        reportedByUserId: _accounts["family.owner"].Id.Value,
                        reportedOnUtc: _now.AddDays(-4),
                        visitedOnUtc: _now.AddDays(-5),
                        isSafetyConcern: slot == "investigating",
                        description: marker);
                    _finance.ComplaintReviews.Add(review);
                    await _finance.SaveChangesAsync(cancellationToken);
                }

                var store = new ComplaintReviewStore(_finance);
                if (outcome is "investigating" or "resolved" or "refused"
                    && review.State == ComplaintReviewState.Received)
                {
                    Result<ComplaintReviewSnapshot> begun = await store.BeginInvestigationAsync(
                        review.Id,
                        _accounts["superadmin"].Id.Value,
                        _now.AddDays(-3),
                        $"{marker}:investigation",
                        cancellationToken);
                    if (!begun.IsSuccess)
                    {
                        throw new InvalidOperationException(
                            $"the demo complaint investigation '{slot}' was refused: {begun.Error.Code}");
                    }
                }

                if (outcome == "resolved" && review.State == ComplaintReviewState.Investigating)
                {
                    Result<ComplaintReviewSnapshot> approved = await store.ApproveAsync(
                        review.Id,
                        _accounts["superadmin"].Id.Value,
                        _now.AddDays(-2),
                        awardMinorUnits: 45_000L,
                        decisionNote: marker,
                        evidenceReference: $"{marker}:approval",
                        cancellationToken);
                    if (!approved.IsSuccess)
                    {
                        throw new InvalidOperationException(
                            $"the demo complaint approval '{slot}' was refused: {approved.Error.Code}");
                    }
                }

                if (outcome == "refused" && review.State == ComplaintReviewState.Investigating)
                {
                    Result<ComplaintReviewSnapshot> refused = await store.RefuseAsync(
                        review.Id,
                        _accounts["superadmin"].Id.Value,
                        _now.AddDays(-1),
                        marker,
                        cancellationToken);
                    if (!refused.IsSuccess)
                    {
                        throw new InvalidOperationException(
                            $"the demo complaint refusal '{slot}' was refused: {refused.Error.Code}");
                    }
                }

                ComplaintReview readBack = await _finance.ComplaintReviews.AsNoTracking()
                    .FirstAsync(item => item.Id == review.Id, cancellationToken);
                records.Add(new PortalProvisionRecord(
                    $"ComplaintReview:{slot}",
                    readBack.Id.ToString(),
                    $"{readBack.State} (case {readBack.RefundCaseId})"));
            }

            return new PortalProvisionGroupResult(
                group,
                Provisioned: true,
                Detail: "4 reviewed complaints (Received, Investigating, Resolved-approved, Refused) each on its own "
                    + "deterministic refund case, the approval append-only in the case ledger",
                Records: records);
        }

        // ------------------------------------------------------------------
        // cms-landing
        // ------------------------------------------------------------------

        private async Task<PortalProvisionGroupResult> CmsLandingAsync()
        {
            await EnsureAccountsLoadedAsync();
            const string group = DemoCorpusGroups.CmsLanding;
            var records = new List<PortalProvisionRecord>();

            Guid[] faqIds = await _cms.HelpFaqs.AsNoTracking()
                .Where(item => item.EnglishQuestion.Contains(_marker))
                .OrderBy(item => item.DisplayOrder)
                .Select(item => item.Id.Value)
                .Take(4)
                .ToArrayAsync(cancellationToken);
            Guid[] ratingIds = await _caregivers.CaregiverRatings.AsNoTracking()
                .Where(item => item.ModerationState == RatingModerationState.Visible)
                .OrderByDescending(item => item.Stars)
                .ThenBy(item => item.Id)
                .Select(item => item.Id)
                .Take(3)
                .ToArrayAsync(cancellationToken);

            string imageKey = await _media.ImagePublicKeyAsync("landing");

            (LandingSectionType Type, int Order, string Arabic, string English, string? ContentJson, bool HasImage)[] plan =
            [
                (LandingSectionType.Hero, 1, "مرحباً بك في سند", "Welcome to Sanad", null, true),
                (LandingSectionType.About, 2, "عن سند", "About Sanad", null, false),
                (LandingSectionType.Features, 3, "مزايا سند", "Sanad features",
                    "[{\"key\":\"trips\",\"arabic\":\"رعاية منزلية\",\"english\":\"Home care\"},"
                        + "{\"key\":\"carehomes\",\"arabic\":\"دور رعاية\",\"english\":\"Care homes\"}]", false),
                (LandingSectionType.Steps, 4, "كيف يعمل سند", "How Sanad works",
                    "[{\"order\":1,\"arabic\":\"سجّل\",\"english\":\"Sign up\"},"
                        + "{\"order\":2,\"arabic\":\"احجز\",\"english\":\"Book\"},"
                        + "{\"order\":3,\"arabic\":\"تابع\",\"english\":\"Follow up\"}]", false),
                (LandingSectionType.Testimonials, 5, "آراء العملاء", "Customer testimonials",
                    TestimonialJson(ratingIds), false),
                (LandingSectionType.Faq, 6, "الأسئلة الشائعة", "Frequently asked questions",
                    FaqJson(faqIds), false)
            ];

            foreach ((LandingSectionType type, int order, string arabic, string english, string? contentJson, bool hasImage) in plan)
            {
                string englishTitle = $"{english} [{_marker}]";
                LandingSection? section = await _cms.LandingSections
                    .FirstOrDefaultAsync(item => item.EnglishTitle == englishTitle, cancellationToken);

                if (section is null)
                {
                    section = LandingSection.Create(
                        type,
                        arabicTitle: arabic,
                        englishTitle: englishTitle,
                        arabicBody: $"محتوى تجريبي لقسم {arabic}.",
                        englishBody: $"SANAD DEMO landing body for {english}.",
                        contentJson: contentJson,
                        imagePath: hasImage ? imageKey : null,
                        displayOrder: order,
                        isVisible: true,
                        createdOnUtc: _now.AddDays(-6));
                    _cms.LandingSections.Add(section);
                    await _cms.SaveChangesAsync(cancellationToken);
                }

                if (section.Status != LandingSectionStatus.Published)
                {
                    section.Publish();
                    await _cms.SaveChangesAsync(cancellationToken);
                }

                records.Add(new PortalProvisionRecord(
                    $"LandingSection:{type}",
                    section.Id.ToString(),
                    $"{section.Status} order {section.DisplayOrder}"));
            }

            return new PortalProvisionGroupResult(
                group,
                Provisioned: true,
                Detail: "6 ordered landing sections (Hero/About/Features/Steps/Testimonials/Faq) published, the hero carrying the "
                    + "public demo image and the testimonial/FAQ sections referencing seeded rating and FAQ ids; stats stays virtual",
                Records: records);
        }

        private static string TestimonialJson(IReadOnlyList<Guid> ratingIds) =>
            ratingIds.Count == 0
                ? "[]"
                : "[" + string.Join(
                    ",",
                    ratingIds.Select((id, index) =>
                        $"{{\"ratingId\":\"{id}\",\"arabic\":\"خدمة رعاية متميزة وتعامل راقٍ - رأي العميل التجريبي {index + 1}\","
                            + $"\"english\":\"SANAD DEMO testimonial {index + 1}\"}}")) + "]";

        private static string FaqJson(IReadOnlyList<Guid> faqIds) =>
            faqIds.Count == 0
                ? "[]"
                : "[" + string.Join(",", faqIds.Select(id => $"{{\"faqId\":\"{id}\"}}")) + "]";

        // ------------------------------------------------------------------
        // shared helpers
        // ------------------------------------------------------------------

        private static DateTime AsUtc(DateTime value) =>
            value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

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

        private static string Tag(string englishName, string marker) => $"{englishName} {marker}";

        private static void RequireOwnership(
            User user,
            string marker,
            AccountType accountType,
            string identifier)
        {
            string englishName = user.EnglishFullName?.Value ?? string.Empty;
            bool tagged = englishName.Contains(marker, StringComparison.Ordinal);
            bool hasAccount = user.Accounts.Any(account => account.AccountType == accountType);

            if (!tagged && !hasAccount)
            {
                throw new InvalidOperationException(
                    $"demo identity collision: '{identifier}' already exists and is not a demo identity "
                        + $"(marker present={tagged}, account {accountType} present={hasAccount}); refusing to adopt it.");
            }
        }

        private static string Describe(Exception exception)
        {
            var messages = new List<string>();
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                string message = $"{current.GetType().Name}: {current.Message.Replace('\r', ' ').Replace('\n', ' ')}";

                // A Postgres constraint violation names the offending column and
                // value only in Detail (the message just says "value too long"),
                // so it is surfaced here rather than lost with the inner
                // exception. Detail never echoes the connection string.
                if (current is Npgsql.PostgresException postgres
                    && !string.IsNullOrWhiteSpace(postgres.Detail))
                {
                    message += $" [detail: {postgres.Detail.Replace('\r', ' ').Replace('\n', ' ')}]";
                }

                messages.Add(message);
            }

            return string.Join(" <- ", messages);
        }

        /// <summary>
        /// Revert every uncommitted change in the shared per-module contexts: an
        /// <c>Added</c> entity is detached and a <c>Modified</c>/<c>Deleted</c>
        /// entity is returned to its stored state. Everything already persisted by
        /// an earlier <c>SaveChangesAsync</c> is untouched (its entry is
        /// <c>Unchanged</c>), and every read is idempotent, so the next group
        /// starts from the committed database rather than from a leaked partial
        /// aggregate.
        /// </summary>
        private void DiscardPendingWrites()
        {
            foreach (DbContext context in ModuleContexts())
            {
                foreach (EntityEntry entry in context.ChangeTracker.Entries().ToArray())
                {
                    switch (entry.State)
                    {
                        case EntityState.Added:
                            entry.State = EntityState.Detached;
                            break;
                        case EntityState.Modified:
                        case EntityState.Deleted:
                            entry.State = EntityState.Unchanged;
                            break;
                    }
                }
            }
        }

        private IEnumerable<DbContext> ModuleContexts()
        {
            yield return _identity;
            yield return _families;
            yield return _finance;
            yield return _caregivers;
            yield return _careHomes;
            yield return _community;
            yield return _cms;
            yield return _notifications;
            yield return _chat;
        }

        private PortalProvisionReport WriteReport(
            IReadOnlyList<PortalProvisionGroupResult> groups,
            IReadOnlyList<string> unresolved)
        {
            Directory.CreateDirectory(options.WorkRoot);
            string manifestPath = Path.Combine(
                options.WorkRoot, $"demo-corpus-manifest-{_runTag}.json");

            var report = new PortalProvisionReport(
                Passed: unresolved.Count == 0,
                ManifestPath: manifestPath,
                RunTag: _runTag,
                InstantUtc: _now,
                FamilyLoginEmail: DemoCorpusIdentity.Email(_runTag, "family.owner"),
                Groups: groups,
                UnresolvedGroups: unresolved);

            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

            return report;
        }
    }

}
