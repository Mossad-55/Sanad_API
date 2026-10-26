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
using Sanad.BuildingBlocks.Application.Idempotency;
using Sanad.API.Idempotency;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
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
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
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
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.Modules.Notifications.Application.Notifications;
using Sanad.Modules.Families.Infrastructure.Payments;

namespace Sanad.API;

public static class DependencyInjection
{
    public static IServiceCollection AddSanadApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddControllers();
        services.AddTransient<PrivateStorageRequestGuard>();

        // The reviewed nginx template connects over loopback HTTP after TLS
        // termination. Keep the framework's loopback-only proxy trust defaults.
        services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(
            Options.SanadProxyConfiguration.Configure);

        // Safe remote release evidence (revision + applied migrations).
        services.AddSingleton<
            Deployment.IDeploymentManifestReader,
            Deployment.FileDeploymentManifestReader>();
        services.AddScoped<
            Deployment.IDeploymentMigrationProbe,
            Deployment.EfDeploymentMigrationProbe>();

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

        services.AddOpenApi(options =>
        {
            options.AddSchemaTransformer<OpenApi.PortalApiSchemaTransformer>();
            options.AddSchemaTransformer<OpenApi.PortalNullableSchemaUnionTransformer>();
            options.AddOperationTransformer<OpenApi.PortalRequiredQueryTransformer>();
        });

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
        services.AddHostedService<FamiliesIntegration.CaregiverMedicalAccessAuditRetentionService>();
        services.AddHostedService<EmailOutboxProcessor>();
        services.AddHostedService<PushOutboxProcessor>();
        services.AddScoped<IElderlyCheckInAlertGateway, ElderlyCheckInAlertGateway>();
        services.AddScoped<ICaregiverBookingRatingEligibility, CaregiverBookingRatingEligibilityGateway>();
        services.AddScoped<IHelpRequestCatalogGateway, HelpRequestCatalogGateway>();
        services.AddScoped<IHelpRequestNotificationGateway, HelpRequestNotificationGateway>();
        services.AddScoped<ISupportReplyNotifier, SupportReplyNotificationGateway>();
        services.AddScoped<IMedicationLatenessSettingGateway, MedicationLatenessSettingGateway>();
        services.AddScoped<IMedicationLateAlertGateway, MedicationLateAlertGateway>();
        services.AddScoped<IElderlyIntakeResolver, FamilyElderlyIntakeResolver>();
        services.AddSingleton<IAdminUserSeedProtection, AdminUserSeedProtection>();
        services.AddScoped<IAdminUserStatusNoticePublisher, AdminUserStatusNoticePublisher>();

        // E04: whether Card/Wallet provider integrations are configured comes
        // from the existing Paymob options; the public status API advertises a
        // method only when it is both enabled and configured.
        services.AddSingleton<IPaymentMethodConfiguration, PaymobPaymentMethodConfiguration>();

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
                    },
                    OnTokenValidated = async context =>
                    {
                        // Shared session ownership/revocation check for
                        // session-bound Normal tokens. Legacy Normal tokens
                        // without sid pass here and are rejected only on
                        // sensitive Chat surfaces by SessionBoundAccess.
                        // Restricted tokens never carry sid and are preserved.
                        string? accessType =
                            context.Principal?.FindFirst(
                                AuthClaimNames.AccessType)?.Value;

                        if (!string.Equals(
                                accessType,
                                AuthAccessType.Normal.ToString(),
                                StringComparison.Ordinal))
                        {
                            return;
                        }

                        string? sessionClaim =
                            context.Principal?.FindFirst(
                                AuthClaimNames.SessionId)?.Value;

                        if (string.IsNullOrWhiteSpace(sessionClaim))
                        {
                            return;
                        }

                        string? subject =
                            context.Principal?.FindFirst(
                                JwtRegisteredClaimNames.Sub)?.Value;

                        if (!Guid.TryParse(subject, out Guid userGuid) ||
                            userGuid == Guid.Empty ||
                            !Guid.TryParse(sessionClaim, out Guid sessionGuid) ||
                            sessionGuid == Guid.Empty)
                        {
                            context.Fail("Invalid session binding.");
                            return;
                        }

                        try
                        {
                            var validator =
                                context.HttpContext.RequestServices
                                    .GetRequiredService<
                                        Security.IIdentitySessionValidator>();

                            var userId =
                                new Sanad.BuildingBlocks.Domain.Primitives.Ids.UserId(
                                    userGuid);
                            var sessionId =
                                new Sanad.BuildingBlocks.Domain.Primitives.Ids.DeviceSessionId(
                                    sessionGuid);

                            bool active =
                                await validator.IsSessionActiveAsync(
                                    userId,
                                    sessionId,
                                    context.HttpContext.RequestAborted);

                            if (!active)
                            {
                                context.Fail("Session revoked or expired.");
                            }
                        }
                        catch (Exception)
                        {
                            context.Fail("Session validation unavailable.");
                        }
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

            // The support contact pair is the one Help Center surface support
            // staff own: SupportAdmin is added here only, never to CmsContent.
            options.AddPolicy(
                AuthorizationPolicies.CmsHelpContact,
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
                        AccountType.SupportAdmin.ToString(),
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

            // Dedicated caregiver name+id lookup only: SuperAdmin + FinanceAdmin
            // on a Normal token, the supervisor/finance screen-to-role matrix.
            // The shared CaregiverReviewAdmin keeps SuperAdmin + SupportAdmin
            // for the caregiver review/ratings surfaces; SupportAdmin is
            // explicitly removed from the lookup only, and ContentAdmin and
            // every app audience are denied. No contact data is granted.
            options.AddPolicy(
                AuthorizationPolicies.CaregiverLookupAdmin,
                policy =>
                {
                    policy.RequireAuthenticatedUser();

                    policy.RequireClaim(
                        AuthClaimNames.AccessType,
                        AuthAccessType.Normal.ToString());

                    policy.RequireClaim(
                        AuthClaimNames.AccountType,
                        AccountType.SuperAdmin.ToString(),
                        AccountType.FinanceAdmin.ToString());
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

            options.AddPolicy(AuthorizationPolicies.SupportInboxAdmin, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(AuthClaimNames.AccessType, AuthAccessType.Normal.ToString());
                policy.RequireClaim(AuthClaimNames.AccountType,
                    AccountType.SuperAdmin.ToString(), AccountType.SupportAdmin.ToString());
            });

            options.AddPolicy(AuthorizationPolicies.OperationalReadAdmin, policy =>
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

            options.AddPolicy(AuthorizationPolicies.MedicalAccessAuditAdmin, policy =>
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

            // E04 platform controls: same SuperAdmin-only shape as
            // SuperAdminOnly, named for the control surface so role denial and
            // intent are unambiguous.
            options.AddPolicy(AuthorizationPolicies.PlatformControlsAdmin, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(AuthClaimNames.AccessType, AuthAccessType.Normal.ToString());
                policy.RequireClaim(AuthClaimNames.AccountType, AccountType.SuperAdmin.ToString());
            });

            // Sensitive Chat access: Normal plus a verified session-bound
            // sid claim. The JWT OnTokenValidated step above already
            // verifies session ownership/revocation; this policy rejects
            // legacy unbound tokens at the Chat boundary.
            options.AddPolicy(AuthorizationPolicies.SessionBoundAccess, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(AuthClaimNames.AccessType, AuthAccessType.Normal.ToString());
                policy.RequireClaim(AuthClaimNames.SessionId);
            });

            // W2-F0 refund workflow money core: SupportAdmin investigates and
            // SuperAdmin approves reviewed awards (refund contract #12). Same
            // shape as the other SuperAdmin+SupportAdmin operational policies;
            // FinanceAdmin is deliberately excluded and no app audience gains
            // access.
            options.AddPolicy(AuthorizationPolicies.RefundWorkflowAdmin, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(AuthClaimNames.AccessType, AuthAccessType.Normal.ToString());
                policy.RequireClaim(AuthClaimNames.AccountType, AccountType.SuperAdmin.ToString(), AccountType.SupportAdmin.ToString());
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
        // W2-F0: the family credit-balance reader/writer seams are registered by
        // the Finance module's own DependencyInjection (FamilyCreditBalanceStore);
        // no API-host duplicate is needed.

        services.AddScoped<INotificationRecipientGateway, FamilyNotificationRecipientGateway>();
        services.AddScoped<INotificationLanguageGateway, NotificationLanguageGateway>();
        services.AddScoped<Sanad.Modules.Families.Application.Abstractions.Sos.ISosNotificationGateway, SosNotificationGateway>();

        services.AddScoped<
            ICaregiverAccountGateway, CaregiverAccountGateway>();

        services.AddScoped<
            IFamilyAccountGateway, FamilyAccountGateway>();

        services.AddScoped<
            ICaregiverBookingPricing, CaregiverBookingPricingGateway>();
        services.AddScoped<
            ICaregiverCatalogLookup, CaregiverCatalogLookup>();
        services.AddScoped<
            ICaregiverAvailabilityRules, CaregiverAvailabilityRulesGateway>();
        services.AddScoped<
            ICaregiverBusyBookingLookup, ApiCaregiverBusyBookingLookup>();
        services.AddSingleton<
            IIdempotentResponseSnapshotter, MvcIdempotentResponseSnapshotter>();

        // Chat outbound ports (interfaces live in Chat Application,
        // implementations here — same gateway pattern as FamilyIdentityGateway).
        services.AddScoped<Security.IIdentitySessionValidator, Security.IdentitySessionValidator>();
        services.AddScoped<IChatUserLookup, ApiChatUserLookup>();
        services.AddScoped<IChatEligibilityChecker, ApiChatEligibilityChecker>();
        services.AddScoped<IChatBookingGateway, ApiChatBookingGateway>();
        services.AddScoped<IChatFacilityLookup, ApiChatFacilityLookup>();
        services.AddScoped<ICareHomeOwnerFacilityLookup, ApiCareHomeOwnerFacilityLookup>();
        services.AddSingleton<IChatSyncCursorProtector, DataProtectionChatSyncCursorProtector>();
        services.AddHostedService<ChatPlus.ChatPlusDispatchService>();
        services.AddHostedService<ChatPlus.ChatAttachmentCleanupHostedService>();
        services.Configure<Sanad.Modules.Chat.Application.Dispatch.ChatDispatchOptions>(
            configuration.GetSection("ChatPlus:Dispatch"));

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
        services.AddScoped<Realtime.ChatConnectionEvictor>();

        ChatPlus.ChatPlusDeploymentOptions deployment =
            configuration
                .GetSection(ChatPlus.ChatPlusDeploymentOptions.SectionName)
                .Get<ChatPlus.ChatPlusDeploymentOptions>()
            ?? new ChatPlus.ChatPlusDeploymentOptions();

        deployment.Validate();

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
    INotificationLanguageGateway languages,
    ILogger<AdminUserStatusNoticePublisher> logger) : IAdminUserStatusNoticePublisher
{
    public async Task PublishAsync(AdminUserStatusNotice notice, CancellationToken cancellationToken)
    {
        try
        {
            // F8: the account owner's persisted language decides the in-app
            // copy. The email keeps the English fields it has always carried -
            // its subject, body and idempotency key are unchanged.
            UiLanguage language = NotificationTextCatalog.ResolveLanguage(
                await languages.GetUiLanguagesAsync([notice.UserId.Value], cancellationToken),
                notice.UserId.Value);
            NotificationCopy inApp = Copy(language, notice);
            NotificationCopy email = Copy(UiLanguage.English, notice);
            string notificationType = notice.EventType == "PasswordReset" ? "AdminUserPasswordReset" : $"AdminUser{notice.Status}";
            db.Notifications.Add(Notification.Create(notice.UserId.Value, "Identity", notificationType, inApp.Title, inApp.Body, "IdentityUser", notice.UserId.Value, notice.CreatedOnUtc));
            if (!string.IsNullOrWhiteSpace(notice.Email))
                db.EmailOutboxMessages.Add(EmailOutboxMessage.Create(notice.Email!, email.Title, email.Body, $"admin-user-{notice.EventType.ToLowerInvariant()}:{notice.UserId.Value:N}:{notice.Status}:{notice.CreatedOnUtc.Ticks}", notice.CreatedOnUtc));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not enqueue the status-change notice for admin-managed user {UserId}.", notice.UserId.Value);
        }
    }

    private static NotificationCopy Copy(UiLanguage language, AdminUserStatusNotice notice) =>
        notice.EventType == "PasswordReset"
            ? NotificationTextCatalog.AdminUserPasswordReset(language)
            : NotificationTextCatalog.AdminUserStatus(language, notice.Status.ToString());
}

/// <summary>
/// E04 availability truth derived from the existing Paymob options: a method
/// is configured only when the gateway secret and that method's integration id
/// are present. The Card subscription-enrollment prerequisite is reported
/// separately because that single branch uses the dedicated 3DS enrollment
/// integration instead of the generic Card integration. No provider secret or
/// integration id is ever exposed; only the boolean outcomes reach the callers,
/// and only the generic method truth reaches the public status response.
/// </summary>
internal sealed class PaymobPaymentMethodConfiguration(IOptions<PaymobOptions> options) : IPaymentMethodConfiguration
{
    public bool CardConfigured =>
        !string.IsNullOrWhiteSpace(options.Value.SecretKey) &&
        !string.IsNullOrWhiteSpace(options.Value.CardIntegrationId);

    public bool WalletConfigured =>
        !string.IsNullOrWhiteSpace(options.Value.SecretKey) &&
        !string.IsNullOrWhiteSpace(options.Value.WalletIntegrationId);

    public bool CardSubscriptionEnrollmentConfigured =>
        !string.IsNullOrWhiteSpace(options.Value.SecretKey) &&
        !string.IsNullOrWhiteSpace(options.Value.Card3dsIntegrationId);
}
