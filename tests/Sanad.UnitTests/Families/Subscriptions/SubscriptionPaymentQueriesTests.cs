using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Subscriptions;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families.Subscriptions;

/// <summary>
/// W4 owner-only subscription payment history and payment detail.
/// <para>
/// Pinned contract: <c>amount</c> is EGP <b>major</b> units (not piastres),
/// <c>paidOnUtc</c> comes from the provider settlement instant only, the linked
/// invoice id is the stored invoice whose <c>PaymentAttemptId</c> points at the
/// attempt (null when none exists, e.g. a plan-change attempt), and the stable
/// order is paid settlement descending with unpaid attempts last, then creation
/// and id descending. Paging defaults to 20, caps at 100, and rejects local or
/// reversed date bounds. A non-owner is forbidden; a foreign or unknown payment
/// id is not found.
/// </para>
/// <para>NOT RUN: authored for the W4 gate; the Mastermind runs it.</para>
/// </summary>
public sealed class SubscriptionPaymentQueriesTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task List_orders_paid_attempts_by_settlement_descending_and_unpaid_last()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = Plan();
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);

        SubscriptionPaymentAttempt olderPaid = Attempt(family, plan, 299m, Now.AddHours(-8));
        olderPaid.TryMarkSucceeded("tx_older", Now.AddHours(-6));
        SubscriptionPaymentAttempt newerPaid = Attempt(family, plan, 299m, Now.AddHours(-7));
        newerPaid.TryMarkSucceeded("tx_newer", Now.AddHours(-1));
        SubscriptionPaymentAttempt failed = Attempt(family, plan, 299m, Now.AddHours(-2));
        failed.TryMarkFailed(null, Now.AddHours(-2));
        SubscriptionPaymentAttempt pending = Attempt(family, plan, 299m, Now.AddHours(-3));
        db.SubscriptionPaymentAttempts.AddRange(olderPaid, newerPaid, failed, pending);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetFamilySubscriptionPaymentsQuery(owner), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { newerPaid.Id, olderPaid.Id, failed.Id, pending.Id },
            result.Value.Items.Select(item => item.PaymentAttemptId).ToArray());
        Assert.Equal(4, result.Value.TotalCount);
        Assert.Equal(1, result.Value.TotalPages);
    }

    [Fact]
    public async Task List_reports_major_unit_amounts_and_settles_paid_on_only_from_the_provider()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = Plan();
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);

        SubscriptionPaymentAttempt paid = Attempt(family, plan, 340.86m, Now.AddDays(-3));
        paid.TryMarkSucceeded("tx_paid", Now.AddHours(-2));
        SubscriptionPaymentAttempt pending = Attempt(family, plan, 120.5m, Now.AddHours(-1));
        db.SubscriptionPaymentAttempts.AddRange(paid, pending);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetFamilySubscriptionPaymentsQuery(owner), default);

        Assert.True(result.IsSuccess);
        SubscriptionPaymentResponse paidItem = Assert.Single(
            result.Value.Items, item => item.PaymentAttemptId == paid.Id);
        Assert.Equal(340.86m, paidItem.Amount);
        Assert.Equal("EGP", paidItem.Currency);
        Assert.Equal(SubscriptionPaymentAttemptStatus.Succeeded, paidItem.Status);
        Assert.Equal(Now.AddHours(-2), paidItem.PaidOnUtc);
        Assert.Equal(SubscriptionPaymentMethod.Card, paidItem.Method);

        SubscriptionPaymentResponse pendingItem = Assert.Single(
            result.Value.Items, item => item.PaymentAttemptId == pending.Id);
        Assert.Equal(120.5m, pendingItem.Amount);
        Assert.Equal(SubscriptionPaymentAttemptStatus.Pending, pendingItem.Status);
        Assert.Null(pendingItem.PaidOnUtc);
    }

    [Fact]
    public async Task List_links_the_stored_invoice_and_leaves_an_uninvoiced_attempt_unlinked()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = Plan();
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);

        SubscriptionPaymentAttempt invoiced = Attempt(family, plan, 299m, Now.AddHours(-5));
        invoiced.TryMarkSucceeded("tx_invoiced", Now.AddHours(-4));
        SubscriptionPaymentAttempt unlinked = Attempt(family, plan, 150m, Now.AddHours(-3));
        unlinked.TryMarkSucceeded("tx_unlinked", Now.AddHours(-2));
        db.SubscriptionPaymentAttempts.AddRange(invoiced, unlinked);
        SubscriptionInvoice invoice = Invoice(family, attemptId: invoiced.Id, total: 299m);
        db.SubscriptionInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetFamilySubscriptionPaymentsQuery(owner), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            invoice.Id,
            Assert.Single(result.Value.Items, item => item.PaymentAttemptId == invoiced.Id).InvoiceId);
        Assert.Null(
            Assert.Single(result.Value.Items, item => item.PaymentAttemptId == unlinked.Id).InvoiceId);
    }

    [Fact]
    public async Task List_exposes_the_plan_and_payment_shape_flags()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion sourcePlan = Plan(SubscriptionPlan.Premium);
        SubscriptionPlanVersion targetPlan = Plan(SubscriptionPlan.PremiumPlus);
        var subscription = FamilySubscription.Create(family.Id, sourcePlan);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.AddRange(sourcePlan, targetPlan);
        db.FamilySubscriptions.Add(subscription);

        SubscriptionPaymentAttempt renewal = SubscriptionPaymentAttempt.CreateRenewal(
            subscription, sourcePlan, 0m, 0m, 0m, 0m, 299m, 1,
            SubscriptionPaymentMethod.Wallet, Now.AddHours(-4));
        renewal.TryMarkSucceeded("tx_renewal", Now.AddHours(-3));
        SubscriptionPaymentAttempt planChange = SubscriptionPaymentAttempt.CreatePlanChange(
            subscription, targetPlan, targetRemainingGross: 400m, sourcePeriodGross: 342m,
            proratedCredit: 100m, targetTaxRatePercentage: 0m, targetTaxAmount: 0m,
            SubscriptionPaymentMethod.Card, Now.AddHours(-2));
        planChange.TryMarkSucceeded("tx_plan_change", Now.AddHours(-1));
        db.SubscriptionPaymentAttempts.AddRange(renewal, planChange);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetFamilySubscriptionPaymentsQuery(owner), default);

        Assert.True(result.IsSuccess);
        SubscriptionPaymentResponse renewalItem = Assert.Single(
            result.Value.Items, item => item.PaymentAttemptId == renewal.Id);
        Assert.True(renewalItem.IsRenewal);
        Assert.False(renewalItem.IsPlanChange);
        Assert.Equal(sourcePlan.Key, renewalItem.PlanKey);
        Assert.Equal(sourcePlan.Version, renewalItem.PlanVersion);

        SubscriptionPaymentResponse planChangeItem = Assert.Single(
            result.Value.Items, item => item.PaymentAttemptId == planChange.Id);
        Assert.False(planChangeItem.IsRenewal);
        Assert.True(planChangeItem.IsPlanChange);
        Assert.Equal(targetPlan.Key, planChangeItem.PlanKey);
        // F3: the plan-change attempt's own payable is the incremental new money
        // (400 target gross minus the 100 credit), never the target gross.
        Assert.Equal(300m, planChangeItem.Amount);
    }

    [Fact]
    public async Task List_filters_by_status_method_and_inclusive_paid_window()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = Plan();
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);

        DateTime earlySettlement = Now.AddHours(-6);
        DateTime lateSettlement = Now.AddHours(-2);
        SubscriptionPaymentAttempt earlyCard = Attempt(
            family, plan, 299m, Now.AddHours(-8), SubscriptionPaymentMethod.Card);
        earlyCard.TryMarkSucceeded("tx_early", earlySettlement);
        SubscriptionPaymentAttempt lateWallet = Attempt(
            family, plan, 199m, Now.AddHours(-4), SubscriptionPaymentMethod.Wallet);
        lateWallet.TryMarkSucceeded("tx_late", lateSettlement);
        SubscriptionPaymentAttempt declinedCard = Attempt(
            family, plan, 299m, Now.AddHours(-1), SubscriptionPaymentMethod.Card);
        declinedCard.TryMarkFailed(null, Now.AddHours(-1));
        db.SubscriptionPaymentAttempts.AddRange(earlyCard, lateWallet, declinedCard);
        await db.SaveChangesAsync();

        var succeeded = await Handler(db).Handle(
            new GetFamilySubscriptionPaymentsQuery(owner, Status: SubscriptionPaymentAttemptStatus.Succeeded), default);
        Assert.Equal(2, succeeded.Value.TotalCount);

        var wallets = await Handler(db).Handle(
            new GetFamilySubscriptionPaymentsQuery(owner, Method: SubscriptionPaymentMethod.Wallet), default);
        Assert.Equal(lateWallet.Id, Assert.Single(wallets.Value.Items).PaymentAttemptId);

        // Both bounds are inclusive and apply to the settlement instant only, so
        // the failed attempt (no settlement) is never in a paid window.
        var window = await Handler(db).Handle(
            new GetFamilySubscriptionPaymentsQuery(
                owner, PaidFromUtc: lateSettlement, PaidToUtc: lateSettlement), default);
        Assert.Equal(lateWallet.Id, Assert.Single(window.Value.Items).PaymentAttemptId);

        var fromEarly = await Handler(db).Handle(
            new GetFamilySubscriptionPaymentsQuery(owner, PaidFromUtc: earlySettlement), default);
        Assert.Equal(
            new[] { lateWallet.Id, earlyCard.Id },
            fromEarly.Value.Items.Select(item => item.PaymentAttemptId).ToArray());
    }

    [Fact]
    public async Task List_pages_with_a_stable_total_and_page_count()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = Plan();
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);

        for (int index = 0; index < 3; index++)
        {
            SubscriptionPaymentAttempt attempt = Attempt(
                family, plan, 100m + index, Now.AddHours(-index - 1));
            attempt.TryMarkSucceeded($"tx_{index}", Now.AddHours(-index));
            db.SubscriptionPaymentAttempts.Add(attempt);
        }

        await db.SaveChangesAsync();

        var first = await Handler(db).Handle(
            new GetFamilySubscriptionPaymentsQuery(owner, Page: 1, PageSize: 2), default);
        var second = await Handler(db).Handle(
            new GetFamilySubscriptionPaymentsQuery(owner, Page: 2, PageSize: 2), default);

        Assert.Equal(2, first.Value.Items.Count);
        Assert.Single(second.Value.Items);
        Assert.Equal(3, first.Value.TotalCount);
        Assert.Equal(2, first.Value.TotalPages);
        Assert.Equal(1, first.Value.Page);
        Assert.Equal(2, first.Value.PageSize);
        Assert.Equal(2, second.Value.Page);
        Assert.Empty(first.Value.Items.Select(item => item.PaymentAttemptId)
            .Intersect(second.Value.Items.Select(item => item.PaymentAttemptId)));
    }

    [Fact]
    public async Task List_rejects_invalid_paging_enums_and_time_bounds()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        db.Families.Add(Family.Create(owner));
        await db.SaveChangesAsync();
        var handler = Handler(db);

        async Task AssertInvalid(GetFamilySubscriptionPaymentsQuery query)
        {
            var result = await handler.Handle(query, default);
            Assert.False(result.IsSuccess);
            Assert.Equal(SubscriptionPaymentErrors.InvalidQuery, result.Error);
        }

        await AssertInvalid(new GetFamilySubscriptionPaymentsQuery(owner, Page: 0));
        await AssertInvalid(new GetFamilySubscriptionPaymentsQuery(owner, PageSize: 0));
        await AssertInvalid(new GetFamilySubscriptionPaymentsQuery(owner, PageSize: 101));
        await AssertInvalid(new GetFamilySubscriptionPaymentsQuery(
            owner, Status: (SubscriptionPaymentAttemptStatus)99));
        await AssertInvalid(new GetFamilySubscriptionPaymentsQuery(
            owner, Method: (SubscriptionPaymentMethod)99));
        await AssertInvalid(new GetFamilySubscriptionPaymentsQuery(
            owner, PaidFromUtc: DateTime.SpecifyKind(Now, DateTimeKind.Local)));
        await AssertInvalid(new GetFamilySubscriptionPaymentsQuery(
            owner, PaidToUtc: DateTime.SpecifyKind(Now, DateTimeKind.Unspecified)));
        await AssertInvalid(new GetFamilySubscriptionPaymentsQuery(
            owner, PaidFromUtc: Now, PaidToUtc: Now.AddHours(-1)));
    }

    [Fact]
    public async Task List_is_owner_only_and_scoped_to_the_owners_family()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        UserId editor = UserId.New();
        UserId stranger = UserId.New();
        Family family = Family.Create(owner);
        family.AddMember(FamilyMember.Create(
            editor, owner, FamilyRelationshipType.Other, FamilyRole.Editor));
        Family otherFamily = Family.Create(UserId.New());
        db.Families.AddRange(family, otherFamily);
        SubscriptionPlanVersion plan = Plan();
        db.SubscriptionPlanVersions.Add(plan);

        SubscriptionPaymentAttempt own = Attempt(family, plan, 299m, Now.AddHours(-2));
        SubscriptionPaymentAttempt foreign = Attempt(otherFamily, plan, 111m, Now.AddHours(-1));
        db.SubscriptionPaymentAttempts.AddRange(own, foreign);
        await db.SaveChangesAsync();

        var editorResult = await Handler(db).Handle(new GetFamilySubscriptionPaymentsQuery(editor), default);
        var strangerResult = await Handler(db).Handle(new GetFamilySubscriptionPaymentsQuery(stranger), default);
        var ownerResult = await Handler(db).Handle(new GetFamilySubscriptionPaymentsQuery(owner), default);

        Assert.Equal(SubscriptionPaymentErrors.NotOwner, editorResult.Error);
        Assert.Equal(SubscriptionPaymentErrors.NotOwner, strangerResult.Error);
        Assert.Equal(own.Id, Assert.Single(ownerResult.Value.Items).PaymentAttemptId);
    }

    [Fact]
    public async Task Detail_returns_the_owners_attempt_with_its_linked_invoice()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = Plan();
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);

        SubscriptionPaymentAttempt attempt = Attempt(family, plan, 250.25m, Now.AddHours(-2));
        attempt.TryMarkSucceeded("tx_detail", Now.AddHours(-1));
        db.SubscriptionPaymentAttempts.Add(attempt);
        SubscriptionInvoice invoice = Invoice(family, attemptId: attempt.Id, total: 250.25m);
        db.SubscriptionInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var result = await DetailHandler(db).Handle(
            new GetFamilySubscriptionPaymentQuery(owner, attempt.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(attempt.Id, result.Value.PaymentAttemptId);
        Assert.Equal(250.25m, result.Value.Amount);
        Assert.Equal(Now.AddHours(-1), result.Value.PaidOnUtc);
        Assert.Equal(invoice.Id, result.Value.InvoiceId);
    }

    [Fact]
    public async Task Detail_returns_not_found_for_missing_foreign_and_empty_ids()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        Family otherFamily = Family.Create(UserId.New());
        db.Families.AddRange(family, otherFamily);
        SubscriptionPlanVersion plan = Plan();
        db.SubscriptionPlanVersions.Add(plan);

        SubscriptionPaymentAttempt foreign = Attempt(otherFamily, plan, 299m, Now.AddHours(-1));
        db.SubscriptionPaymentAttempts.Add(foreign);
        await db.SaveChangesAsync();
        var handler = Handler(db);

        var detailHandler = DetailHandler(db);
        var missing = await detailHandler.Handle(
            new GetFamilySubscriptionPaymentQuery(owner, Guid.NewGuid()), default);
        var crossFamily = await detailHandler.Handle(
            new GetFamilySubscriptionPaymentQuery(owner, foreign.Id), default);
        var empty = await detailHandler.Handle(
            new GetFamilySubscriptionPaymentQuery(owner, Guid.Empty), default);

        Assert.Equal(SubscriptionPaymentErrors.NotFound, missing.Error);
        Assert.Equal(SubscriptionPaymentErrors.NotFound, crossFamily.Error);
        Assert.Equal(SubscriptionPaymentErrors.NotFound, empty.Error);
    }

    [Fact]
    public async Task Detail_is_owner_only()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        UserId viewer = UserId.New();
        Family family = Family.Create(owner);
        family.AddMember(FamilyMember.Create(
            viewer, owner, FamilyRelationshipType.Other, FamilyRole.Viewer));
        db.Families.Add(family);
        SubscriptionPlanVersion plan = Plan();
        db.SubscriptionPlanVersions.Add(plan);
        SubscriptionPaymentAttempt attempt = Attempt(family, plan, 299m, Now.AddHours(-1));
        db.SubscriptionPaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var result = await DetailHandler(db).Handle(
            new GetFamilySubscriptionPaymentQuery(viewer, attempt.Id), default);

        Assert.Equal(SubscriptionPaymentErrors.NotOwner, result.Error);
    }

    [Fact]
    public async Task Invoice_list_and_detail_expose_the_linked_payment_attempt()
    {
        await using FamiliesDbContext db = CreateContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        SubscriptionPlanVersion plan = Plan();
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);

        SubscriptionPaymentAttempt attempt = Attempt(family, plan, 299m, Now.AddHours(-2));
        attempt.TryMarkSucceeded("tx_invoice_link", Now.AddHours(-1));
        db.SubscriptionPaymentAttempts.Add(attempt);
        SubscriptionInvoice invoice = Invoice(family, attemptId: attempt.Id, total: 299m);
        SubscriptionInvoice unlinked = Invoice(family, attemptId: null, total: 0m);
        db.SubscriptionInvoices.AddRange(invoice, unlinked);
        await db.SaveChangesAsync();

        var list = await new GetFamilySubscriptionInvoicesQueryHandler(db).Handle(
            new GetFamilySubscriptionInvoicesQuery(owner), default);
        var detail = await new GetFamilySubscriptionInvoiceQueryHandler(db).Handle(
            new GetFamilySubscriptionInvoiceQuery(owner, invoice.Id), default);

        Assert.True(list.IsSuccess);
        Assert.True(detail.IsSuccess);
        Assert.Equal(
            attempt.Id,
            Assert.Single(list.Value, item => item.Id == invoice.Id).PaymentAttemptId);
        Assert.Null(Assert.Single(list.Value, item => item.Id == unlinked.Id).PaymentAttemptId);
        Assert.Equal(attempt.Id, detail.Value.PaymentAttemptId);
    }

    private static GetFamilySubscriptionPaymentsQueryHandler Handler(FamiliesDbContext db) => new(db);

    private static GetFamilySubscriptionPaymentQueryHandler DetailHandler(FamiliesDbContext db) => new(db);

    private static SubscriptionPlanVersion Plan(SubscriptionPlan? plan = null) =>
        SubscriptionPlanVersion.Create(
            plan ?? SubscriptionPlan.Premium, true, true, Now, Now);

    private static SubscriptionPaymentAttempt Attempt(
        Family family,
        SubscriptionPlanVersion plan,
        decimal totalPayable,
        DateTime createdOnUtc,
        SubscriptionPaymentMethod method = SubscriptionPaymentMethod.Card) =>
        SubscriptionPaymentAttempt.Create(
            family.Id,
            plan,
            basePrice: totalPayable,
            discountPercentage: 0m,
            discountAmount: 0m,
            taxableAmount: totalPayable,
            taxRatePercentage: 0m,
            taxAmount: 0m,
            totalPayable: totalPayable,
            couponCode: null,
            method,
            createdOnUtc);

    private static SubscriptionInvoice Invoice(Family family, Guid? attemptId, decimal total) =>
        SubscriptionInvoice.Create(
            number: $"INV-{Guid.NewGuid():N}",
            familyId: family.Id.Value,
            attemptId: attemptId,
            subscriptionId: Guid.NewGuid(),
            SubscriptionInvoiceKind.InitialPurchase,
            planKey: SubscriptionPlan.PremiumKey,
            planVersion: 1,
            basePrice: total,
            discountAmount: 0m,
            feeRate: 0m,
            feeAmount: 0m,
            taxAmount: 0m,
            ruleVersion: 1,
            total: total,
            currency: "EGP",
            periodStart: Now.AddDays(-30),
            periodEnd: Now,
            issued: Now,
            pdfKey: $"subscription-invoices/{Guid.NewGuid():N}.pdf",
            providerEventId: null);

    private static FamiliesDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
