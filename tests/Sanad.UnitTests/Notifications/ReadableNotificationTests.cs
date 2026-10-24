using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sanad.API.CareHomesIntegration;
using Sanad.API.NotificationsIntegration;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Notifications.Application.Abstractions.Recipients;
using Sanad.Modules.Notifications.Application.Notifications;
using Sanad.Modules.Notifications.Domain.Notifications;
using Sanad.Modules.Notifications.Infrastructure.Persistence;

namespace Sanad.UnitTests.Notifications;

/// <summary>
/// F8 — readable notifications. Pins the copy rules that changed: every
/// generated notification reads in the recipient's persisted UI language with
/// Arabic as the documented default, Arabic recipients read Arabic labels, and
/// no generated title or body repeats a raw identifier or chat text. The
/// structured destination pair, category/type, idempotency keys, recipient
/// selection and delivery behavior are asserted unchanged by the existing
/// per-producer tests.
/// </summary>
public sealed class ReadableNotificationTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>Enough of a GUID to recognize one inside human copy.</summary>
    private static readonly Regex IdentifierPrefix = new("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}");

    // ---------------------------------------------------------------
    // Copy catalog.
    // ---------------------------------------------------------------

    [Fact]
    public void Catalog_owns_distinct_bilingual_copy_for_every_generated_notification()
    {
        foreach (NotificationCopy arabic in CatalogCopies(UiLanguage.Arabic))
        {
            Assert.False(string.IsNullOrWhiteSpace(arabic.Title));
            Assert.False(string.IsNullOrWhiteSpace(arabic.Body));
            AssertWithinLimits(arabic.Title, arabic.Body);
            AssertNoIdentifier(arabic.Title + arabic.Body);
        }

        NotificationCopy[] arabicCopies = [.. CatalogCopies(UiLanguage.Arabic)];
        NotificationCopy[] englishCopies = [.. CatalogCopies(UiLanguage.English)];
        Assert.Equal(arabicCopies.Length, englishCopies.Length);
        for (int index = 0; index < arabicCopies.Length; index++)
        {
            Assert.NotEqual(arabicCopies[index].Title, englishCopies[index].Title);
            Assert.NotEqual(arabicCopies[index].Body, englishCopies[index].Body);
            Assert.False(string.IsNullOrWhiteSpace(englishCopies[index].Title));
            Assert.False(string.IsNullOrWhiteSpace(englishCopies[index].Body));
            AssertWithinLimits(englishCopies[index].Title, englishCopies[index].Body);
            AssertNoIdentifier(englishCopies[index].Title + englishCopies[index].Body);
        }
    }

    [Fact]
    public void Booking_copy_reads_in_both_languages_without_an_identifier()
    {
        foreach (CareHomeNotificationEvent eventType in Enum.GetValues<CareHomeNotificationEvent>())
        {
            var copy = CareHomeBookingNotificationProcessor.Copy(eventType);
            Assert.False(string.IsNullOrWhiteSpace(copy.Type));
            Assert.True(copy.Type.Length <= Notification.MaximumTypeLength);
            Assert.False(string.IsNullOrWhiteSpace(copy.TitleAr));
            Assert.False(string.IsNullOrWhiteSpace(copy.TitleEn));
            Assert.NotEqual(copy.TitleAr, copy.TitleEn);
            Assert.NotEqual(copy.BodyAr, copy.BodyEn);
            AssertWithinLimits(copy.TitleAr, copy.BodyAr);
            AssertWithinLimits(copy.TitleEn, copy.BodyEn);
            AssertNoIdentifier(copy.TitleAr + copy.BodyAr + copy.TitleEn + copy.BodyEn);
        }
    }

    [Fact]
    public void Review_copy_reads_in_both_languages_without_an_identifier()
    {
        string[] actions =
        [
            "Submitted",
            nameof(CareHomeReviewAction.CorrectionsRequested),
            nameof(CareHomeReviewAction.Approved),
            nameof(CareHomeReviewAction.Rejected),
            nameof(CareHomeReviewAction.Suspended),
            nameof(CareHomeReviewAction.Reactivated),
            "UnknownAction"
        ];

        foreach (string action in actions)
        {
            var copy = CareHomeReviewNotificationProcessor.Copy(action);
            Assert.False(string.IsNullOrWhiteSpace(copy.TitleAr));
            Assert.False(string.IsNullOrWhiteSpace(copy.TitleEn));
            Assert.NotEqual(copy.TitleAr, copy.TitleEn);
            Assert.NotEqual(copy.BodyAr, copy.BodyEn);
            AssertWithinLimits(copy.TitleAr, copy.BodyAr);
            AssertWithinLimits(copy.TitleEn, copy.BodyEn);
            AssertNoIdentifier(copy.TitleAr + copy.BodyAr + copy.TitleEn + copy.BodyEn);
        }

        Assert.Equal(
            CareHomeReviewNotificationProcessor.Copy("UnknownAction"),
            CareHomeReviewNotificationProcessor.Copy("anything-else"));
    }

    [Fact]
    public void Catalog_resolves_an_absent_or_unknown_language_to_arabic()
    {
        Guid known = Guid.NewGuid();
        Guid absent = Guid.NewGuid();
        IReadOnlyDictionary<Guid, UiLanguage> languages = new Dictionary<Guid, UiLanguage>
        {
            [known] = UiLanguage.English
        };

        Assert.Equal(UiLanguage.English, NotificationTextCatalog.ResolveLanguage(languages, known));
        Assert.Equal(UiLanguage.Arabic, NotificationTextCatalog.ResolveLanguage(languages, absent));
        Assert.Equal(UiLanguage.Arabic, NotificationTextCatalog.ResolveLanguage(null, known));
        Assert.Equal(UiLanguage.Arabic, NotificationTextCatalog.DefaultLanguage);
        Assert.True(NotificationTextCatalog.IsArabic(UiLanguage.Arabic));
        Assert.False(NotificationTextCatalog.IsArabic(UiLanguage.English));
    }

    [Fact]
    public void Catalog_owns_only_the_negative_check_in_pair()
    {
        Assert.True(NotificationTextCatalog.TryResolveInboundCopy(
            "ElderlyCheckIn", "NegativeCheckIn", UiLanguage.English, out NotificationCopy english));
        Assert.True(NotificationTextCatalog.TryResolveInboundCopy(
            "ElderlyCheckIn", "NegativeCheckIn", UiLanguage.Arabic, out NotificationCopy arabic));
        Assert.Equal(NotificationTextCatalog.NegativeCheckIn(UiLanguage.English), english);
        Assert.Equal(NotificationTextCatalog.NegativeCheckIn(UiLanguage.Arabic), arabic);
        Assert.NotEqual(english.Title, arabic.Title);

        // Any other (category, type) pair keeps the copy the command carried.
        Assert.False(NotificationTextCatalog.TryResolveInboundCopy(
            "ElderlyCheckIn", "PositiveCheckIn", UiLanguage.Arabic, out _));
        Assert.False(NotificationTextCatalog.TryResolveInboundCopy(
            "ElderlySos", "SosCreated", UiLanguage.Arabic, out _));
        Assert.False(NotificationTextCatalog.TryResolveInboundCopy(
            "elderlycheckin", "negativecheckin", UiLanguage.Arabic, out _));
    }

    [Fact]
    public void Catalog_admin_status_copy_keeps_the_released_english_bytes_and_labels_known_arabic_statuses()
    {
        // The English copy of both admin notices is byte-for-byte what the
        // publisher already produced, so existing rows and emails are unchanged.
        Assert.Equal(
            new NotificationCopy("Account status changed", "Your account status is now Suspended."),
            NotificationTextCatalog.AdminUserStatus(UiLanguage.English, "Suspended"));
        Assert.Equal(
            new NotificationCopy("Your account password was reset",
                "Your account password was reset by an administrator. Contact support if you did not expect this."),
            NotificationTextCatalog.AdminUserPasswordReset(UiLanguage.English));

        Assert.Equal("حالة حسابك الآن نشطة.", AdminStatusBodyAr("Active"));
        Assert.Equal("حالة حسابك الآن بانتظار التحقق.", AdminStatusBodyAr("PendingVerification"));
        Assert.Equal("حالة حسابك الآن معلّقة.", AdminStatusBodyAr("Suspended"));
        Assert.Equal("حالة حسابك الآن محظورة.", AdminStatusBodyAr("Blocked"));

        // An unrecognized status name stays as supplied in both languages.
        Assert.Equal("حالة حسابك الآن SomethingNew.", AdminStatusBodyAr("SomethingNew"));
        Assert.Equal("Your account status is now SomethingNew.",
            NotificationTextCatalog.AdminUserStatus(UiLanguage.English, "SomethingNew").Body);

        Assert.NotEqual(
            NotificationTextCatalog.AdminUserPasswordReset(UiLanguage.Arabic),
            NotificationTextCatalog.AdminUserPasswordReset(UiLanguage.English));
        Assert.NotEqual(
            NotificationTextCatalog.SupportReplyTitle(UiLanguage.Arabic),
            NotificationTextCatalog.SupportReplyTitle(UiLanguage.English));
    }

    // ---------------------------------------------------------------
    // Persisted-language seam.
    // ---------------------------------------------------------------

    [Fact]
    public async Task LanguageGateway_answers_a_whole_fan_out_in_one_lookup_and_omits_unknown_users()
    {
        await using IdentityDbContext identity = await CreateIdentityDbAsync();
        User arabic = AddUser(identity, "arabic@example.test", UiLanguage.Arabic);
        User english = AddUser(identity, "english@example.test", UiLanguage.English);
        await identity.SaveChangesAsync();
        Guid unknown = Guid.NewGuid();
        var gateway = new NotificationLanguageGateway(identity);

        IReadOnlyDictionary<Guid, UiLanguage> languages = await gateway.GetUiLanguagesAsync(
            [arabic.Id.Value, english.Id.Value, arabic.Id.Value, unknown], CancellationToken.None);

        Assert.Equal(2, languages.Count);
        Assert.Equal(UiLanguage.Arabic, languages[arabic.Id.Value]);
        Assert.Equal(UiLanguage.English, languages[english.Id.Value]);
        Assert.DoesNotContain(unknown, languages.Keys);

        // A recipient with no persisted language keeps the Arabic default.
        Assert.Equal(UiLanguage.Arabic, NotificationTextCatalog.ResolveLanguage(languages, unknown));

        Assert.Empty(await gateway.GetUiLanguagesAsync([], CancellationToken.None));
        Assert.Empty(await gateway.GetUiLanguagesAsync([unknown], CancellationToken.None));
    }

    [Fact]
    public void Language_rows_translate_on_postgresql()
    {
        using var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_probe;Username=probe;Password=probe").Options);

        string sql = NotificationLanguageGateway
            .UserLanguageRows(identity, [new UserId(Guid.NewGuid()), new UserId(Guid.NewGuid())])
            .ToQueryString();

        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ui_language", sql, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------
    // Support replies: title localized, authored body untouched.
    // ---------------------------------------------------------------

    [Fact]
    public async Task Support_reply_localizes_the_title_and_stores_the_authored_body_byte_for_byte()
    {
        await using IdentityDbContext identity = await CreateIdentityDbAsync();
        User arabic = AddUser(identity, "arabic@example.test", UiLanguage.Arabic);
        User english = AddUser(identity, "english@example.test", UiLanguage.English);
        await identity.SaveChangesAsync();
        var sender = new CapturingSender();
        var gateway = new SupportReplyNotificationGateway(sender, new NotificationLanguageGateway(identity));
        Guid ticketId = Guid.NewGuid();
        const string AuthoredBody = "راجعنا تذكرتك وسنوافيك بالتفاصيل قريبًا.";
        const string EnglishAuthoredBody = "We reviewed your ticket and will follow up shortly.";

        await gateway.TryNotifyAsync(arabic.Id.Value, ticketId, AuthoredBody, CancellationToken.None);
        await gateway.TryNotifyAsync(english.Id.Value, ticketId, EnglishAuthoredBody, CancellationToken.None);

        Assert.Equal(2, sender.Commands.Count);
        CreateInAppNotificationCommand first = sender.Commands[0];
        CreateInAppNotificationCommand second = sender.Commands[1];

        Assert.Equal(NotificationTextCatalog.SupportReplyTitle(UiLanguage.Arabic), first.Title);
        Assert.Equal(NotificationTextCatalog.SupportReplyTitle(UiLanguage.English), second.Title);
        Assert.Equal(AuthoredBody, first.Body);
        Assert.Equal(EnglishAuthoredBody, second.Body);
        Assert.All(sender.Commands, command =>
        {
            Assert.Equal("Support", command.Category);
            Assert.Equal("SupportReply", command.Type);
            Assert.Equal("SupportTicket", command.DestinationEntityKind);
            Assert.Equal(ticketId, command.DestinationEntityId);
            Assert.Equal(DateTimeKind.Utc, command.CreatedOnUtc.Kind);
        });
        Assert.Equal(arabic.Id.Value, first.RecipientUserId);
        Assert.Equal(english.Id.Value, second.RecipientUserId);
    }

    // ---------------------------------------------------------------
    // Chat seam: Arabic default and ids kept off the copy.
    // ---------------------------------------------------------------

    [Fact]
    public async Task Chat_seam_localizes_an_unknown_recipient_to_arabic_and_keeps_ids_off_the_copy()
    {
        // The chat producer localizes from the recipient's PERSISTED language.
        // With no persisted language anywhere (the ArabicDefault gateway), the
        // documented Arabic default applies — the F8 complement to the English
        // pin in ChatMessageNotificationTests, which already owns replay
        // idempotency and the ids-only command shape.
        await using NotificationsDbContext notifications = new(
            new DbContextOptionsBuilder<NotificationsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        Guid recipient = UserId.New().Value;
        Guid conversationId = Guid.NewGuid();
        Guid messageId = Guid.NewGuid();
        var languages = FixedNotificationLanguageGateway.ArabicDefault;
        var handler = new CreateChatMessageNotificationCommandHandler(notifications, languages);

        var created = await handler.Handle(
            new CreateChatMessageNotificationCommand(
                recipient, conversationId, messageId, DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.Code : null);

        Notification row = Assert.Single(await notifications.Notifications.AsNoTracking().ToListAsync());

        // Arabic default copy, distinct from every released English title.
        NotificationCopy arabic = NotificationTextCatalog.ChatNewMessage(UiLanguage.Arabic);
        Assert.Equal(arabic.Title, row.Title);
        Assert.Equal(arabic.Body, row.Body);
        Assert.NotEqual(
            NotificationTextCatalog.ChatNewMessage(UiLanguage.English).Title, row.Title);

        // No raw routing id in the human copy — in EITHER string form of
        // BOTH ids (the structured formats and the compact key form).
        string copy = row.Title + row.Body;
        Assert.DoesNotContain(conversationId.ToString("D"), copy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(conversationId.ToString("N"), copy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(messageId.ToString("D"), copy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(messageId.ToString("N"), copy, StringComparison.OrdinalIgnoreCase);

        // Routing-metadata separation: the ids ride ONLY on the structured
        // destination pair and the idempotency key.
        Assert.Equal("ChatConversation", row.DestinationEntityKind);
        Assert.Equal(conversationId, row.DestinationEntityId);
        Assert.Equal(
            CreateChatMessageNotificationCommandHandler.BuildIdempotencyKey(messageId, recipient),
            row.IdempotencyKey);
        Assert.Contains(messageId.ToString("N"), row.IdempotencyKey, StringComparison.Ordinal);

        // The whole direct-message fan-out is one batched language lookup
        // carrying exactly the alert's single recipient.
        Assert.Equal(1, languages.LookupCount);
        Guid requested = Assert.Single(languages.RequestedIds);
        Assert.Equal(recipient, requested);
    }

    // ---------------------------------------------------------------
    // Care-home producers.
    // ---------------------------------------------------------------

    [Fact]
    public async Task Transfer_copy_keeps_bilingual_accommodation_labels_and_no_booking_identifier()
    {
        await using CareHomesDbContext careHomes = CreateCareHomesDb();
        CareHomeId facilityId = CareHomeId.New();
        CareHomeRoom room = CareHomeRoom.Create(facilityId, Guid.NewGuid(), "101", Now);
        CareHomeBed bed = CareHomeBed.Create(facilityId, room.Id, "A", Now);
        careHomes.Rooms.Add(room);
        careHomes.Beds.Add(bed);
        await careHomes.SaveChangesAsync();

        (string Arabic, string English) from = await CareHomeTransferNotificationProcessor
            .ResourceLabelsAsync(careHomes, room.Id, bed.Id, CancellationToken.None);
        (string Arabic, string English) roomOnly = await CareHomeTransferNotificationProcessor
            .ResourceLabelsAsync(careHomes, room.Id, null, CancellationToken.None);
        (string Arabic, string English) unassigned = await CareHomeTransferNotificationProcessor
            .ResourceLabelsAsync(careHomes, null, null, CancellationToken.None);

        Assert.Equal("غرفة 101، سرير A", from.Arabic);
        Assert.Equal("room 101, bed A", from.English);
        Assert.Equal("غرفة 101", roomOnly.Arabic);
        Assert.Equal("room 101", roomOnly.English);
        Assert.Equal("غير محدد", unassigned.Arabic);
        Assert.Equal("unassigned", unassigned.English);

        var copy = CareHomeTransferNotificationProcessor.Copy(from, unassigned, new DateOnly(2026, 10, 21));

        Assert.Equal("تم تحديث تخصيص الغرفة", copy.TitleAr);
        Assert.Equal("Care-home room assignment updated", copy.TitleEn);
        Assert.Contains("غرفة 101، سرير A", copy.BodyAr, StringComparison.Ordinal);
        Assert.Contains("غير محدد", copy.BodyAr, StringComparison.Ordinal);
        Assert.Contains("2026-10-21", copy.BodyAr, StringComparison.Ordinal);
        Assert.DoesNotContain("غير محدد", copy.BodyEn, StringComparison.Ordinal);
        Assert.Contains("room 101, bed A", copy.BodyEn, StringComparison.Ordinal);
        Assert.Contains("unassigned", copy.BodyEn, StringComparison.Ordinal);
        Assert.Contains("2026-10-21", copy.BodyEn, StringComparison.Ordinal);
        AssertWithinLimits(copy.TitleAr, copy.BodyAr);
        AssertWithinLimits(copy.TitleEn, copy.BodyEn);
        AssertNoIdentifier(copy.BodyAr + copy.BodyEn);
    }

    [Fact]
    public async Task License_monitor_copy_has_no_facility_identifier_and_follows_each_admin_language()
    {
        DateOnly today = new(2026, 10, 21);
        DateTime now = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        await using TestHost host = await TestHost.CreateAsync();

        User arabicAdmin = AddAdmin(host.Identity, "arabic-admin@example.test", UiLanguage.Arabic, now);
        User englishAdmin = AddAdmin(host.Identity, "english-admin@example.test", UiLanguage.English, now);
        await host.Identity.SaveChangesAsync();

        CareHomeFacility expired = CreateApprovedFacility(now, today.AddDays(-1));
        host.CareHomes.Add(expired);
        await host.CareHomes.SaveChangesAsync();

        await host.RunLicenseScanAsync(today);

        List<Notification> alerts = await host.Notifications.Notifications.AsNoTracking().ToListAsync();
        List<EmailOutboxMessage> emails = await host.Notifications.EmailOutboxMessages.AsNoTracking().ToListAsync();

        Assert.Equal(2, alerts.Count);
        Notification arabicAlert = Assert.Single(alerts, x => x.RecipientUserId == arabicAdmin.Id.Value);
        Notification englishAlert = Assert.Single(alerts, x => x.RecipientUserId == englishAdmin.Id.Value);
        Assert.Equal("انتهت صلاحية ترخيص دار الرعاية", arabicAlert.Title);
        Assert.Equal("Care-home license expired", englishAlert.Title);
        Assert.Equal("A care home has an expired operating license and requires review.", englishAlert.Body);
        Assert.All(alerts, alert =>
        {
            Assert.Equal("CareHomes", alert.Category);
            Assert.Equal("LicenseExpired", alert.Type);
            Assert.Equal("CareHome", alert.DestinationEntityKind);
            Assert.Equal(expired.Id.Value, alert.DestinationEntityId);
            Assert.DoesNotContain(expired.Id.Value.ToString(), alert.Title + alert.Body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(expired.Id.Value.ToString("N"), alert.Title + alert.Body, StringComparison.OrdinalIgnoreCase);
        });

        // The email channel keeps its released bilingual shape: same two
        // recipients, same idempotency keys, no raw identifier either.
        Assert.Equal(2, emails.Count);
        Assert.All(emails, email =>
        {
            Assert.Contains("expired", email.Body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(expired.Id.Value.ToString(), email.Body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(expired.Id.Value.ToString("N"), email.Body, StringComparison.OrdinalIgnoreCase);
        });
    }

    // ---------------------------------------------------------------
    // Harness.
    // ---------------------------------------------------------------

    private static IEnumerable<NotificationCopy> CatalogCopies(UiLanguage language) =>
    [
        NotificationTextCatalog.ChatNewMessage(language),
        NotificationTextCatalog.NegativeCheckIn(language),
        NotificationTextCatalog.HelpRequest(language),
        NotificationTextCatalog.MedicationDoseMissed(language),
        NotificationTextCatalog.SosAlert(language),
        NotificationTextCatalog.AdminUserStatus(language, "Active"),
        NotificationTextCatalog.AdminUserPasswordReset(language)
    ];

    private static string AdminStatusBodyAr(string statusName) =>
        NotificationTextCatalog.AdminUserStatus(UiLanguage.Arabic, statusName).Body;

    private static void AssertWithinLimits(string title, string body)
    {
        Assert.True(title.Length <= Notification.MaximumTitleLength,
            $"Notification title exceeds {Notification.MaximumTitleLength} characters.");
        Assert.True(body.Length <= Notification.MaximumBodyLength,
            $"Notification body exceeds {Notification.MaximumBodyLength} characters.");
    }

    private static void AssertNoIdentifier(string copy) =>
        Assert.DoesNotMatch(IdentifierPrefix, copy);

    private static async Task<IdentityDbContext> CreateIdentityDbAsync()
    {
        var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await identity.Database.EnsureCreatedAsync();
        return identity;
    }

    private static CareHomesDbContext CreateCareHomesDb() =>
        new(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static User AddUser(IdentityDbContext db, string email, UiLanguage language)
    {
        User user = User.Create(FullName.Create("Test"), FullName.Create("User"), Email.Create(email),
            PhoneNumber.Create("+201" + Random.Shared.NextInt64(100000000, 999999999)), null);
        user.AddAccount(AccountType.Family);
        user.VerifyEmail(Now);
        user.VerifyPhone(Now);
        user.SetInitialPasswordHash("test-password-hash", Now);
        user.Activate(Now);
        user.ChangeUiLanguage(language, Now);
        db.Users.Add(user);
        return user;
    }

    private static User AddAdmin(IdentityDbContext db, string email, UiLanguage language, DateTime now)
    {
        User user = User.Create(FullName.Create("Admin"), FullName.Create("Admin"), Email.Create(email),
            PhoneNumber.Create("+201" + Random.Shared.NextInt64(100000000, 999999999)), null);
        user.AddAccount(AccountType.SuperAdmin);
        user.VerifyEmail(now);
        user.VerifyPhone(now);
        user.SetInitialPasswordHash("test-password-hash", now);
        user.Activate(now);
        user.ChangeUiLanguage(language, now);
        db.Users.Add(user);
        return user;
    }

    /// <summary>
    /// One approved facility whose verified operating license expired before
    /// the scan date, built through the same domain flow the released license
    /// monitor test uses.
    /// </summary>
    private static CareHomeFacility CreateApprovedFacility(DateTime now, DateOnly licenseExpiry)
    {
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, now);
        facility.SaveDraft(owner, facility.Version, Sanad.UnitTests.CareHomes.CareHomeFacilityTests.Draft(), now);
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            facility.UploadDocument(owner, type, $"{type}-{Guid.NewGuid():N}.pdf", "application/pdf", 100,
                type == CareHomeDocumentType.OperatingLicense ? licenseExpiry : null,
                now.AddSeconds(type == CareHomeDocumentType.OperatingLicense ? 1 : 0));
        facility.Submit(owner, facility.Version, now.AddMinutes(1));
        foreach (CareHomeDocument document in facility.Documents)
        {
            bool license = document.Type == CareHomeDocumentType.OperatingLicense;
            facility.VerifyDocument(admin, facility.Version, document.Id, license ? licenseExpiry : null,
                !license, now.AddMinutes(2));
        }
        facility.Review(admin, facility.Version, CareHomeReviewAction.Approved, null, now.AddMinutes(3),
            DateOnly.FromDateTime(now).AddDays(-2));
        return facility;
    }

    private sealed class CapturingSender : MediatR.ISender
    {
        public List<CreateInAppNotificationCommand> Commands { get; } = [];

        public Task<TResponse> Send<TResponse>(MediatR.IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            if (request is CreateInAppNotificationCommand command)
            {
                Commands.Add(command);
                return Task.FromResult((TResponse)(object)
                    Sanad.BuildingBlocks.Application.Results.Result<Guid>.Success(Guid.NewGuid()));
            }

            throw new NotSupportedException($"Unexpected request {request.GetType().Name}.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : MediatR.IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(MediatR.IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestHost : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        public CareHomesDbContext CareHomes { get; }
        public IdentityDbContext Identity { get; }
        public NotificationsDbContext Notifications { get; }

        private TestHost(ServiceProvider services, CareHomesDbContext careHomes, IdentityDbContext identity,
            NotificationsDbContext notifications)
        { this.services = services; CareHomes = careHomes; Identity = identity; Notifications = notifications; }

        public static async Task<TestHost> CreateAsync()
        {
            ServiceCollection services = new();
            var careHomes = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var notifications = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            await careHomes.Database.EnsureCreatedAsync();
            await identity.Database.EnsureCreatedAsync();
            await notifications.Database.EnsureCreatedAsync();
            services.AddSingleton(careHomes).AddSingleton(identity).AddSingleton(notifications);
            return new TestHost(services.BuildServiceProvider(), careHomes, identity, notifications);
        }

        public async Task RunLicenseScanAsync(DateOnly date)
        {
            CareHomeLicenseExpiryMonitor monitor = new(services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<CareHomeLicenseExpiryMonitor>.Instance);
            MethodInfo method = typeof(CareHomeLicenseExpiryMonitor)
                .GetMethod("RunOnceAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)method.Invoke(monitor, [date, CancellationToken.None])!;
        }

        public async ValueTask DisposeAsync()
        { await CareHomes.DisposeAsync(); await Identity.DisposeAsync(); await Notifications.DisposeAsync(); await services.DisposeAsync(); }
    }
}

/// <summary>
/// F8 test double for the persisted-language read seam. With no map it answers
/// "no persisted language" for every recipient, which is the documented Arabic
/// default; otherwise it answers only for the recipients it was given. It is
/// created fresh per use so a test that counts lookups cannot be affected by a
/// parallel test class, and the counters let a fan-out prove it batched every
/// recipient into one lookup.
/// </summary>
public sealed class FixedNotificationLanguageGateway(IReadOnlyDictionary<Guid, UiLanguage>? languages = null)
    : INotificationLanguageGateway
{
    /// <summary>A fresh double with no persisted language for anyone (Arabic default).</summary>
    public static FixedNotificationLanguageGateway ArabicDefault => new();

    /// <summary>A fresh double that persists one language for one recipient.</summary>
    public static FixedNotificationLanguageGateway For(Guid userId, UiLanguage language) =>
        new(new Dictionary<Guid, UiLanguage> { [userId] = language });

    public int LookupCount { get; private set; }

    public IReadOnlyCollection<Guid> RequestedIds { get; private set; } = [];

    public Task<IReadOnlyDictionary<Guid, UiLanguage>> GetUiLanguagesAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        LookupCount++;
        RequestedIds = [.. userIds];
        IReadOnlyDictionary<Guid, UiLanguage> resolved = languages is null
            ? new Dictionary<Guid, UiLanguage>()
            : languages.Where(pair => userIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        return Task.FromResult(resolved);
    }
}
