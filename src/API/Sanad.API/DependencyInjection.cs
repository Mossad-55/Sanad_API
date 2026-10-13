using System.IdentityModel.Tokens.Jwt;
using System.Text;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Sanad.API.Authorization;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Behaviors;
using Sanad.Modules.Identity.Application.Authentication.Registration;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Infrastructure;
using Sanad.Modules.Identity.Infrastructure.Security;
using Sanad.Modules.Cms.Infrastructure;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Cms.Application.Splash;
using Sanad.Modules.Caregivers.Infrastructure;
using Sanad.Modules.Families.Infrastructure;
using Sanad.BuildingBlocks.Infrastructure.Storage;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.Modules.Caregivers.Application.Lookups;
using Sanad.Modules.Caregivers.Application.Discovery;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;
using Sanad.Modules.Families.Application.Abstractions.Identity;
using Sanad.API.IdentityIntegration;
using Sanad.Modules.Caregivers.Application.Onboarding;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Application.Abstractions.Caregivers;
using Sanad.Modules.Identity.Application.Abstractions.Caregivers;
using Sanad.Modules.Identity.Application.Abstractions.Families;
using Sanad.API.CaregiversIntegration;
using Sanad.API.Seeding;
using Sanad.API.Options;
using Sanad.API.NotificationsIntegration;
using Sanad.API.CareHomesIntegration;
using Sanad.Modules.Notifications.Infrastructure;
using Sanad.Modules.Notifications.Application.Abstractions.Recipients;
using Sanad.Modules.Families.Application.Abstractions.Notifications;
using Sanad.Modules.Families.Application.Abstractions.HelpRequests;
using Sanad.API.HelpRequestsIntegration;
using Sanad.API.MedicationIntegration;
using Sanad.Modules.Families.Application.Abstractions.Medications;
using Sanad.Modules.CareHomes.Infrastructure;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.Community.Infrastructure;
using Sanad.Modules.Community.Application.Posts;
using Sanad.Modules.Chat.Infrastructure;
using Sanad.API.ChatIntegration;
using Sanad.Modules.Chat.Application.Conversations;
using Sanad.Modules.Chat.Application.Sync;
using Sanad.Modules.CareHomes.Application.FamilyIntake;
using Sanad.Modules.CareHomes.Application.Bookings;
using Sanad.Modules.Finance.Infrastructure;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Identity.Application.Users.Admin;
using Microsoft.Extensions.Options;
using Sanad.Modules.Notifications.Application.Abstractions.Data;
using Sanad.Modules.Notifications.Domain.Notifications;

namespace Sanad.API;

public static class DependencyInjection
{
    public static IServiceCollection AddSanadApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddControllers();
        services.AddTransient<PrivateStorageRequestGuard>();

        services.AddOptions<TestUserSeedOptions>()
            .Bind(configuration.GetSection(TestUserSeedOptions.SectionName));

        services.AddOptions<ElderlyProfileOptions>()
            .Bind(configuration.GetSection(ElderlyProfileOptions.SectionName))
            .Validate(options => Sanad.Modules.Families.Domain.Elderlies.ElderlyTimeZone.IsValid(options.DefaultTimeZoneId),
                "ElderlyProfile:DefaultTimeZoneId must be a valid IANA time zone.")
            .ValidateOnStart();

        services.AddScoped<TestUserDataSeeder>();
        services.AddScoped<TestPlatformChargeRuleSeeder>();

        // Temporary: allow any client during mobile development.
        // Lock down to known origins before production launch.
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
                policy
                    .AllowAnyOrigin()
                    .AllowAnyHeader()
                    .AllowAnyMethod());
        });

        services.AddProblemDetails();

        services.AddOpenApi();

        services.AddIdentityInfrastructure(
            configuration);

        services.AddCmsInfrastructure(
            configuration);

        services.AddCaregiversInfrastructure(
            configuration);

        services.AddFamiliesInfrastructure(
            configuration);

        services.AddCareHomesInfrastructure(configuration);
        services.AddSingleton(provider => ResolveCareHomeBookingTiming(configuration, provider.GetRequiredService<IHostEnvironment>()));

        services.AddNotificationsInfrastructure(configuration);
        services.Configure<Sanad.Modules.Notifications.Infrastructure.Push.PushFirebaseOptions>(configuration.GetSection(Sanad.Modules.Notifications.Infrastructure.Push.PushFirebaseOptions.SectionName));
        services.AddSingleton<Sanad.Modules.Notifications.Application.Abstractions.Push.IPushSender>(sp =>
        {
            if (sp.GetService<IHostEnvironment>()?.IsDevelopment() == true)
                return new Sanad.Modules.Notifications.Infrastructure.Push.DevelopmentNoOpPushSender();
            return ActivatorUtilities.CreateInstance<Sanad.Modules.Notifications.Infrastructure.Push.FcmPushSender>(sp);
        });
        services.AddCommunityInfrastructure(configuration);
        services.AddChatInfrastructure(configuration);
        services.AddFinanceInfrastructure(configuration);
        services.AddHostedService<CareHomeLicenseExpiryMonitor>();
        services.AddHostedService<CareHomeBookingExpiryHostedService>();
        services.AddHostedService<CareHomeTransferNotificationProcessor>();
        services.AddHostedService<CareHomeBookingNotificationProcessor>();
        services.AddHostedService<CareHomeReviewNotificationProcessor>();
        services.AddHostedService<EmailOutboxProcessor>();
        services.AddHostedService<PushOutboxProcessor>();
        services.AddScoped<IElderlyCheckInAlertGateway, ElderlyCheckInAlertGateway>();
        services.AddScoped<ICaregiverBookingRatingEligibility, CaregiverBookingRatingEligibilityGateway>();
        services.AddScoped<IHelpRequestCatalogGateway, HelpRequestCatalogGateway>();
        services.AddScoped<IHelpRequestNotificationGateway, HelpRequestNotificationGateway>();
        services.AddScoped<IMedicationLatenessSettingGateway, MedicationLatenessSettingGateway>();
        services.AddScoped<IMedicationLateAlertGateway, MedicationLateAlertGateway>();
        services.AddScoped<IElderlyIntakeResolver, FamilyElderlyIntakeResolver>();
        services.AddSingleton<IAdminUserSeedProtection, AdminUserSeedProtection>();
        services.AddScoped<IAdminUserStatusNoticePublisher, AdminUserStatusNoticePublisher>();

        services.AddOptions<LocalStorageOptions>()
            .Bind(
                configuration.GetSection(
                    LocalStorageOptions.SectionName));

        services.AddSingleton<IFileStorage,
            LocalDiskFileStorage>();

        JwtOptions jwtOptions =
            configuration
                .GetSection(
                    JwtOptions.SectionName)
                .Get<JwtOptions>()
            ?? throw new InvalidOperationException(
                "Identity:Jwt configuration is required.");

        if (string.IsNullOrWhiteSpace(
                jwtOptions.SigningKey) ||
            Encoding.UTF8.GetByteCount(
                jwtOptions.SigningKey) < 32)
        {
            throw new InvalidOperationException(
                "Identity:Jwt:SigningKey must contain " +
                "at least 32 UTF-8 bytes.");
        }

        services.AddAuthentication(
                JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims =
                    false;

                options.RequireHttpsMetadata =
                    !string.Equals(
                        Environment.GetEnvironmentVariable(
                            "ASPNETCORE_ENVIRONMENT"),
                        "Development",
                        StringComparison.OrdinalIgnoreCase);

                // Supported SignalR transport auth: browser WebSocket/SSE
                // clients cannot set headers, so this hub path alone accepts
                // the existing access token as ?access_token=. Everywhere
                // else the header stays mandatory; the token is redacted
                // from application/proxy/APM logs by configuration (see the
                // operating guide) and a connection token never replaces it.
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (context.Request.Path.StartsWithSegments("/hubs/chat") &&
                            context.Request.Query.TryGetValue("access_token", out var token))
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    }
                };

                options.TokenValidationParameters =
                    new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = jwtOptions.Issuer,

                        ValidateAudience = true,
                        ValidAudience = jwtOptions.Audience,

                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.Zero,

                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey =
                            new SymmetricSecurityKey(
                                Encoding.UTF8.GetBytes(
                                    jwtOptions.SigningKey)),

                        NameClaimType =
                            JwtRegisteredClaimNames.Sub,

                        RoleClaimType =
                            AuthClaimNames.AccountType
                    };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                AuthorizationPolicies.NormalAccess,
                policy =>
                {
                    policy.RequireAuthenticatedUser();

                    policy.RequireClaim(
                        AuthClaimNames.AccessType,
                        AuthAccessType.Normal
                            .ToString());
                });

            options.AddPolicy(
                AuthorizationPolicies.CmsContent,
                policy =>
                {
                    policy.RequireAuthenticatedUser();

                    policy.RequireClaim(
                        AuthClaimNames.AccessType,
                        AuthAccessType.Normal
                            .ToString());

                    policy.RequireClaim(
                        AuthClaimNames.AccountType,
                        AccountType.SuperAdmin.ToString(),
                        AccountType.ContentAdmin.ToString());
                });

            options.AddPolicy(AuthorizationPolicies.CommunityModeration, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(AuthClaimNames.AccessType, AuthAccessType.Normal.ToString());
                policy.RequireClaim(AuthClaimNames.AccountType,
                    AccountType.SuperAdmin.ToString(), AccountType.ContentAdmin.ToString());
            });

            options.AddPolicy(
                AuthorizationPolicies.CaregiversAdmin,
                policy =>
                {
                    policy.RequireAuthenticatedUser();

                    policy.RequireClaim(
                        AuthClaimNames.AccessType,
                        AuthAccessType.Normal
                            .ToString());

                    policy.RequireClaim(
                        AuthClaimNames.AccountType,
                        AccountType.SuperAdmin.ToString(),
                        AccountType.ContentAdmin.ToString());
                });

            options.AddPolicy(
                AuthorizationPolicies.CaregiverReviewAdmin,
                policy =>
                {
                    policy.RequireAuthenticatedUser();

                    policy.RequireClaim(
                        AuthClaimNames.AccessType,
                        AuthAccessType.Normal.ToString());

                    policy.RequireClaim(
                        AuthClaimNames.AccountType,
                        AccountType.SuperAdmin.ToString(),
                        AccountType.SupportAdmin.ToString());
                });

            options.AddPolicy(
                AuthorizationPolicies.CareHomesOperationalAdmin,
                policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim(AuthClaimNames.AccessType, AuthAccessType.Normal.ToString());
                    policy.RequireClaim(AuthClaimNames.AccountType,
                        AccountType.SuperAdmin.ToString(), AccountType.SupportAdmin.ToString());
                });

            options.AddPolicy(AuthorizationPolicies.FinanceOperationalAdmin, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(AuthClaimNames.AccessType, AuthAccessType.Normal.ToString());
                policy.RequireClaim(AuthClaimNames.AccountType, AccountType.SuperAdmin.ToString(), AccountType.SupportAdmin.ToString());
            });

            options.AddPolicy(AuthorizationPolicies.PayoutOperationalAdmin, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(AuthClaimNames.AccessType, AuthAccessType.Normal.ToString());
                policy.RequireClaim(AuthClaimNames.AccountType, AccountType.SuperAdmin.ToString(), AccountType.FinanceAdmin.ToString());
            });

            options.AddPolicy(
                AuthorizationPolicies.SubscriptionPlanAdmin,
                policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim(
                        AuthClaimNames.AccessType,
                        AuthAccessType.Normal.ToString());
                    policy.RequireClaim(
                        AuthClaimNames.AccountType,
                        AccountType.SuperAdmin.ToString());
                });

            options.AddPolicy(
                AuthorizationPolicies.CaregiverAccess,
                policy =>
                {
                    policy.RequireAuthenticatedUser();

                    policy.RequireClaim(
                        AuthClaimNames.AccessType,
                        AuthAccessType.Normal
                            .ToString());

                    policy.RequireClaim(
                        AuthClaimNames.AccountType,
                        AccountType.MedicalCaregiver.ToString(),
                        AccountType.CompanionCaregiver.ToString());
                });

            options.AddPolicy(
                AuthorizationPolicies.FamilyAccess,
                policy =>
                {
                    policy.RequireAuthenticatedUser();

                    policy.RequireClaim(
                        AuthClaimNames.AccessType,
                        AuthAccessType.Normal
                            .ToString());

                    policy.RequireClaim(
                        AuthClaimNames.AccountType,
                        AccountType.Family
                            .ToString());
                });

            options.AddPolicy(
                AuthorizationPolicies.CareHomeOwnerAccess,
                policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim(
                        AuthClaimNames.AccessType,
                        AuthAccessType.Normal.ToString());
                    policy.RequireClaim(
                        AuthClaimNames.AccountType,
                        AccountType.CareHomeOwner.ToString());
                });

            options.AddPolicy(
                AuthorizationPolicies.ElderlyAccess,
                policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim(
                        AuthClaimNames.AccessType,
                        AuthAccessType.Normal.ToString());
                    policy.RequireClaim(
                        AuthClaimNames.AccountType,
                        AccountType.Elderly.ToString());
                });

            options.AddPolicy(
                AuthorizationPolicies.ElderlyMedicationOperationalRead,
                policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim(AuthClaimNames.AccessType, AuthAccessType.Normal.ToString());
                    policy.RequireClaim(AuthClaimNames.AccountType,
                        AccountType.SuperAdmin.ToString(), AccountType.SupportAdmin.ToString());
                });
            options.AddPolicy(
                AuthorizationPolicies.ElderlyMedicationOperationalManage,
                policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim(AuthClaimNames.AccessType, AuthAccessType.Normal.ToString());
                    policy.RequireClaim(AuthClaimNames.AccountType,
                        AccountType.SuperAdmin.ToString(), AccountType.SupportAdmin.ToString());
                });
            options.AddPolicy(AuthorizationPolicies.ElderlyHelpRequestOperational, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(AuthClaimNames.AccessType, AuthAccessType.Normal.ToString());
                policy.RequireClaim(AuthClaimNames.AccountType, AccountType.SuperAdmin.ToString(), AccountType.SupportAdmin.ToString());
            });
            options.AddPolicy(AuthorizationPolicies.ElderlySosOperational, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(AuthClaimNames.AccessType, AuthAccessType.Normal.ToString());
                policy.RequireClaim(AuthClaimNames.AccountType, AccountType.SuperAdmin.ToString(), AccountType.SupportAdmin.ToString());
            });
            options.AddPolicy(AuthorizationPolicies.AdminNotificationOperationalRead, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(AuthClaimNames.AccessType, AuthAccessType.Normal.ToString());
                policy.RequireClaim(AuthClaimNames.AccountType, AccountType.SuperAdmin.ToString(), AccountType.SupportAdmin.ToString());
            });
            // Chat support routes (endpoints 12-15): verbatim shape of the
            // ElderlySosOperational precedent above — SuperAdmin + SupportAdmin
            // on a normal AccessType. No other role gains chat-admin rights.
            options.AddPolicy(AuthorizationPolicies.ChatSupportAdmin, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(AuthClaimNames.AccessType, AuthAccessType.Normal.ToString());
                policy.RequireClaim(AuthClaimNames.AccountType, AccountType.SuperAdmin.ToString(), AccountType.SupportAdmin.ToString());
            });

            options.AddPolicy(AuthorizationPolicies.SuperAdminOnly, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(AuthClaimNames.AccessType, AuthAccessType.Normal.ToString());
                policy.RequireClaim(AuthClaimNames.AccountType, AccountType.SuperAdmin.ToString());
            });
        });

        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(
                typeof(RegisterUserCommand).Assembly);

            configuration.RegisterServicesFromAssembly(
                typeof(CreateSplashScreenCommand).Assembly);

            configuration.RegisterServicesFromAssembly(
                typeof(CreateServiceCommand).Assembly);

            configuration.RegisterServicesFromAssembly(
                typeof(BootstrapFamilyCommand).Assembly);

            configuration.RegisterServicesFromAssembly(
                typeof(ICareHomesDbContext).Assembly);

            configuration.RegisterServicesFromAssembly(
                typeof(CreatePlatformChargeRuleCommand).Assembly);

            configuration.RegisterServicesFromAssembly(
                typeof(Sanad.Modules.Notifications.Application.Notifications.ListNotificationsQuery).Assembly);

            configuration.RegisterServicesFromAssembly(
                typeof(GetPostsQuery).Assembly);

            configuration.RegisterServicesFromAssembly(
                typeof(OpenConversationCommand).Assembly);
        });
        services.AddScoped<Sanad.Modules.Families.Application.Subscriptions.ISubscriptionInvoiceService,
            Sanad.Modules.Families.Application.Subscriptions.SubscriptionInvoiceService>();


        services.AddValidatorsFromAssembly(
            typeof(RegisterUserCommand).Assembly);

        services.AddValidatorsFromAssembly(
            typeof(CreateSplashScreenCommand).Assembly);

        services.AddValidatorsFromAssembly(
            typeof(CreateServiceCommand).Assembly);

        services.AddValidatorsFromAssembly(
            typeof(BootstrapFamilyCommand).Assembly);

        services.AddValidatorsFromAssembly(
            typeof(ICareHomesDbContext).Assembly);

        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(ValidationBehavior<,>));

        services.AddExceptionHandler<
            ValidationExceptionHandler>();

        services.AddScoped<
            IFamilyIdentityGateway, FamilyIdentityGateway>();
        services.AddScoped<
            Sanad.Modules.Families.Application.Abstractions.Caregivers.IMedicalCaregiverGateway,
            MedicalCaregiverGateway>();

        services.AddScoped<INotificationRecipientGateway, FamilyNotificationRecipientGateway>();
        services.AddScoped<Sanad.Modules.Families.Application.Abstractions.Sos.ISosNotificationGateway, SosNotificationGateway>();

        services.AddScoped<
            ICaregiverAccountGateway, CaregiverAccountGateway>();

        services.AddScoped<
            IFamilyAccountGateway, FamilyAccountGateway>();

        services.AddScoped<
            ICaregiverBookingPricing, CaregiverBookingPricingGateway>();
        services.AddScoped<
            ICaregiverCatalogLookup, CaregiverCatalogLookup>();

        // Chat outbound ports (interfaces live in Chat Application,
        // implementations here — same gateway pattern as FamilyIdentityGateway).
        services.AddScoped<IChatUserLookup, ApiChatUserLookup>();
        services.AddScoped<IChatEligibilityChecker, ApiChatEligibilityChecker>();
        services.AddScoped<IChatBookingGateway, ApiChatBookingGateway>();
        services.AddScoped<IChatFacilityLookup, ApiChatFacilityLookup>();
        services.AddScoped<ICareHomeOwnerFacilityLookup, ApiCareHomeOwnerFacilityLookup>();
        services.AddSingleton<IChatSyncCursorProtector, DataProtectionChatSyncCursorProtector>();
        services.AddHostedService<ChatPlus.ChatPlusDispatchService>();

        // Chat Plus realtime (single-instance topology): in-memory
        // connection/presence/typing registries behind seams, durable
        // dispatch behind the publisher seam. No Redis backplane is
        // assumed; multi-instance without one is unsupported.
        services.AddSignalR(options =>
        {
            // Hub inbound frame budget (16 KiB): oversized frames are
            // rejected/closed, never buffered unboundedly.
            options.MaximumReceiveMessageSize = 16 * 1024;
        });
        services.AddSingleton<
            Sanad.Modules.Chat.Application.Realtime.IChatConnectionRegistry,
            Realtime.ChatConnectionRegistry>();
        services.AddSingleton<
            Sanad.Modules.Chat.Application.Realtime.IPresenceTracker,
            Realtime.PresenceTracker>();
        services.AddSingleton<
            Sanad.Modules.Chat.Application.Realtime.ITypingTracker,
            Realtime.TypingTracker>();
        services.AddScoped<
            Sanad.Modules.Chat.Application.Realtime.IChatHubAudience,
            Realtime.ChatHubAudience>();
        services.AddScoped<
            Sanad.Modules.Chat.Application.Realtime.IChatRealtimePublisher,
            Realtime.ChatRealtimePublisher>();
        services.AddScoped<
            Sanad.Modules.Chat.Application.Realtime.IChatAuthorizationChangedNotifier,
            Realtime.ChatConnectionEvictor>();

        ChatPlus.ChatPlusDeploymentOptions deployment =
            configuration
                .GetSection(ChatPlus.ChatPlusDeploymentOptions.SectionName)
                .Get<ChatPlus.ChatPlusDeploymentOptions>()
            ?? new ChatPlus.ChatPlusDeploymentOptions();

        if (deployment.InstanceCount > 1 && !deployment.BackplaneConfigured)
        {
            throw new InvalidOperationException(
                "ChatPlus multi-instance deployment requires a verified backplane; " +
                "single-instance is the only supported topology without one.");
        }

        return services;
    }

    private static CareHomeBookingTiming ResolveCareHomeBookingTiming(IConfiguration configuration, IHostEnvironment environment)
    {
        const string prefix = "CareHomes:TestClock:";
        string[] keys = ["CheckoutHoldDuration", "DecisionHoldDuration", "ExpirySweepInterval"];
        bool enabled = configuration.GetValue<bool>($"{prefix}Enabled");
        bool hasOverrides = keys.Any(key => configuration[$"{prefix}{key}"] is not null);
        if (!enabled && hasOverrides)
            throw new InvalidOperationException("CareHomes:TestClock duration overrides require CareHomes:TestClock:Enabled=true.");
        if (!enabled)
            return CareHomeBookingTiming.Default;
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("CareHomes:TestClock is restricted to the Development environment.");

        TimeSpan Read(string key, TimeSpan fallback)
        {
            TimeSpan value = configuration.GetValue($"{prefix}{key}", fallback);
            if (value <= TimeSpan.Zero) throw new InvalidOperationException($"{prefix}{key} must be positive.");
            return value;
        }

        return new CareHomeBookingTiming(
            Read("CheckoutHoldDuration", CareHomeBookingTiming.Default.CheckoutHoldDuration),
            Read("DecisionHoldDuration", CareHomeBookingTiming.Default.DecisionHoldDuration),
            Read("ExpirySweepInterval", CareHomeBookingTiming.Default.ExpirySweepInterval));
    }
}

internal sealed class AdminUserSeedProtection(IOptions<AdminSeedOptions> options) : IAdminUserSeedProtection
{
    public bool IsSeeded(Sanad.Modules.Identity.Domain.Users.User user) =>
        !string.IsNullOrWhiteSpace(options.Value.Email) &&
        user.Email is not null &&
        string.Equals(user.Email.Value, options.Value.Email, StringComparison.OrdinalIgnoreCase);
}

internal sealed class AdminUserStatusNoticePublisher(
    INotificationsDbContext db,
    ILogger<AdminUserStatusNoticePublisher> logger) : IAdminUserStatusNoticePublisher
{
    public async Task PublishAsync(AdminUserStatusNotice notice, CancellationToken cancellationToken)
    {
        try
        {
            string title = notice.EventType == "PasswordReset" ? "Your account password was reset" : "Account status changed";
            string message = notice.EventType == "PasswordReset" ? "Your account password was reset by an administrator. Contact support if you did not expect this." : $"Your account status is now {notice.Status}.";
            string notificationType = notice.EventType == "PasswordReset" ? "AdminUserPasswordReset" : $"AdminUser{notice.Status}";
            db.Notifications.Add(Notification.Create(notice.UserId.Value, "Identity", notificationType, title, message, "IdentityUser", notice.UserId.Value, notice.CreatedOnUtc));
            if (!string.IsNullOrWhiteSpace(notice.Email))
                db.EmailOutboxMessages.Add(EmailOutboxMessage.Create(notice.Email!, title, message, $"admin-user-{notice.EventType.ToLowerInvariant()}:{notice.UserId.Value:N}:{notice.Status}:{notice.CreatedOnUtc.Ticks}", notice.CreatedOnUtc));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not enqueue the status-change notice for admin-managed user {UserId}.", notice.UserId.Value);
        }
    }
}
