using System.Reflection;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Bookings;
using Xunit;

namespace Sanad.UnitTests.Families;

/// <summary>
/// Invariants of <see cref="BookingCancellationFeedback"/>, <see cref="BookingCancellationFact"/> and
/// <see cref="BookingCancellationReasonCategories"/>: the two legal feedback shapes, the 500 character
/// bound, the mandatory category for an accepted booking, the closed read-only category set, and the
/// recording boundary that takes its reason only from the validated decision.
/// </summary>
public sealed class BookingCancellationFeedbackAndFactTests
{
    private static readonly DateTime AcceptanceOnUtc =
        new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

    private const int MaximumReasonLength = 500;

    private static DateTime CancelAfter(string elapsed) =>
        AcceptanceOnUtc + TimeSpan.Parse(elapsed, System.Globalization.CultureInfo.InvariantCulture);

    private static BookingCancellationPolicyInput ConfirmedFamilyCancellation(
        BookingCancellationFeedback? feedback) =>
        BookingCancellationPolicyInput.ForFamilyCancellation(
            BookingStatus.Confirmed,
            AcceptanceOnUtc,
            startedOnUtc: null,
            CancelAfter("00:10:00"),
            BookingCaptureEvidence.Captured,
            feedback);

    private static BookingCancellationPolicyInput ConfirmedCaregiverCancellation(
        BookingCancellationFeedback? feedback) =>
        BookingCancellationPolicyInput.ForCaregiverCancellation(
            BookingStatus.Confirmed,
            AcceptanceOnUtc,
            startedOnUtc: null,
            CancelAfter("00:10:00"),
            BookingCaptureEvidence.Captured,
            feedback);

    // ---------------------------------------------------------------------------------------------
    // Create: accepted-booking feedback = a known category plus a non-blank bounded note.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(BookingCancellationReasonCategory.Emergency)]
    [InlineData(BookingCancellationReasonCategory.MedicalIssues)]
    [InlineData(BookingCancellationReasonCategory.TransportationIssues)]
    [InlineData(BookingCancellationReasonCategory.AccountDeletion)]
    [InlineData(BookingCancellationReasonCategory.Other)]
    public void Create_AcceptsEveryApprovedCategoryWithANote(BookingCancellationReasonCategory category)
    {
        BookingCancellationFeedback feedback = BookingCancellationFeedback.Create(category, "a real reason");

        Assert.Equal(category, feedback.Category);
        Assert.True(feedback.HasCategory);
        Assert.Equal("a real reason", feedback.Note);
    }

    [Fact]
    public void Create_TrimsTheNoteAndKeepsInnerSpacing()
    {
        BookingCancellationFeedback feedback = BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.Emergency,
            "   urgent   family matter\t\n");

        Assert.Equal("urgent   family matter", feedback.Note);
    }

    [Fact]
    public void Create_AcceptsExactlyTheLimitAndRejectsOneCharacterMore()
    {
        BookingCancellationFeedback atLimit = BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.Other,
            new string('x', MaximumReasonLength));
        Assert.Equal(MaximumReasonLength, atLimit.Note.Length);

        Assert.Throws<DomainException>(() => BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.Other,
            new string('x', MaximumReasonLength + 1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    public void Create_RejectsAnUnknownCategory(int rawCategory)
    {
        Assert.Throws<DomainException>(() => BookingCancellationFeedback.Create(
            (BookingCancellationReasonCategory)rawCategory,
            "some note"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Create_RejectsAMissingOrBlankNoteForAcceptedFeedback(string? note)
    {
        Assert.Throws<DomainException>(() => BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.Emergency,
            note));
    }

    // ---------------------------------------------------------------------------------------------
    // CreateOptionalNote: bounded free text with no category, blank input normalizes away.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n \t")]
    public void CreateOptionalNote_BlankInputNormalizesToNull(string? note)
    {
        Assert.Null(BookingCancellationFeedback.CreateOptionalNote(note));
    }

    [Fact]
    public void CreateOptionalNote_TrimsTextAndNeverFabricatesACategory()
    {
        BookingCancellationFeedback? feedback = BookingCancellationFeedback.CreateOptionalNote(
            "  the family will reschedule  ");

        Assert.NotNull(feedback);
        Assert.Equal("the family will reschedule", feedback!.Note);
        Assert.Null(feedback.Category);
        Assert.False(feedback.HasCategory);
    }

    [Fact]
    public void CreateOptionalNote_AppliesTheSameLengthBound()
    {
        Assert.NotNull(BookingCancellationFeedback.CreateOptionalNote(new string('y', MaximumReasonLength)));

        Assert.Throws<DomainException>(() =>
            BookingCancellationFeedback.CreateOptionalNote(new string('y', MaximumReasonLength + 1)));
    }

    // ---------------------------------------------------------------------------------------------
    // Regression: a non-null note-only reason is NOT sufficient for an accepted booking, either side.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ConfirmedFamilyCancellation_NoteOnlyReasonIsRejected()
    {
        BookingCancellationFeedback? noteOnly = BookingCancellationFeedback.CreateOptionalNote("reason");
        Assert.NotNull(noteOnly);
        Assert.False(noteOnly!.HasCategory);

        DomainException error = Assert.Throws<DomainException>(
            () => BookingCancellationPolicy.Decide(ConfirmedFamilyCancellation(noteOnly)));

        Assert.Contains("note-only reason is not enough", error.Message);
    }

    [Fact]
    public void ConfirmedCaregiverCancellation_NoteOnlyReasonIsRejected()
    {
        BookingCancellationFeedback? noteOnly = BookingCancellationFeedback.CreateOptionalNote("reason");

        DomainException error = Assert.Throws<DomainException>(
            () => BookingCancellationPolicy.Decide(ConfirmedCaregiverCancellation(noteOnly)));

        Assert.Contains("note-only reason is not enough", error.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConfirmedCancellation_CategoryWithNoteIsAcceptedOnEitherSide(bool isCaregiver)
    {
        BookingCancellationFeedback feedback = BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.TransportationIssues,
            "caregiver could not reach the area");

        BookingCancellationPolicyInput input = isCaregiver
            ? ConfirmedCaregiverCancellation(feedback)
            : ConfirmedFamilyCancellation(feedback);

        BookingCancellationDecision decision = BookingCancellationPolicy.Decide(input);

        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, decision.RefundEntitlement);
        Assert.True(decision.IsReasonFeedbackRequired);
        Assert.Same(feedback, decision.Feedback);
    }

    [Fact]
    public void PreAcceptance_NoteOnlyReasonIsAllowedAndSurvivesFactCreationWithNullCategory()
    {
        BookingCancellationFeedback? noteOnly = BookingCancellationFeedback.CreateOptionalNote(
            " will arrange someone else ");

        BookingCancellationDecision decision = BookingCancellationPolicy.Decide(
            BookingCancellationPolicyInput.ForFamilyCancellation(
                BookingStatus.PendingCaregiverApproval,
                confirmedOnUtc: null,
                startedOnUtc: null,
                CancelAfter("00:01:00"),
                BookingCaptureEvidence.Captured,
                noteOnly));

        BookingCancellationFact fact = BookingCancellationFact.Create(
            BookingId.New(), UserId.New(), decision);

        Assert.False(decision.IsReasonFeedbackRequired);
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, decision.RefundEntitlement);
        Assert.Null(fact.ReasonCategory);
        Assert.Equal("will arrange someone else", fact.ReasonNote);
    }

    // ---------------------------------------------------------------------------------------------
    // Fact recording: every significant field comes from the decision, nothing else.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void FactCreate_FromCaregiverDecision_PreservesEveryRecordedField()
    {
        BookingCancellationFeedback feedback = BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.AccountDeletion,
            "account is being closed");
        DateTime cancelledOnUtc = CancelAfter("03:00:00");

        BookingCancellationDecision decision = BookingCancellationPolicy.Decide(
            BookingCancellationPolicyInput.ForCaregiverCancellation(
                BookingStatus.Confirmed, AcceptanceOnUtc, null, cancelledOnUtc,
                BookingCaptureEvidence.Captured, feedback));

        BookingId bookingId = BookingId.New();
        UserId actorUserId = UserId.New();

        BookingCancellationFact fact = BookingCancellationFact.Create(bookingId, actorUserId, decision);

        Assert.Equal(bookingId, fact.BookingId);
        Assert.Equal(actorUserId, fact.ActorUserId);
        Assert.Equal(BookingCancellationActorSide.Caregiver, fact.ActorSide);
        Assert.Equal(BookingCancellationAction.Cancel, fact.Action);
        Assert.Equal(BookingStatus.Confirmed, fact.StatusAtCancellation);
        Assert.Equal(cancelledOnUtc, fact.CancelledOnUtc);
        Assert.Equal(AcceptanceOnUtc, fact.ConfirmedOnUtcUsed);
        Assert.Equal(1, fact.PolicyVersion);
        Assert.Equal(BookingCancellationReasonCategory.AccountDeletion, fact.ReasonCategory);
        Assert.Equal("account is being closed", fact.ReasonNote);
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, fact.RefundEntitlement);
        Assert.Equal(
            BookingRefundDecisionReason.CaregiverCancellationAfterAcceptance,
            fact.RefundDecisionReason);
        Assert.True(fact.IsCaregiverIncident);
        Assert.NotEqual(BookingCancellationFactId.Empty, fact.Id);
    }

    [Fact]
    public void FactCreate_PreservesThePreAcceptanceShapeWithoutACategory()
    {
        BookingCancellationDecision decision = BookingCancellationPolicy.Decide(
            BookingCancellationPolicyInput.ForCaregiverRejection(
                BookingStatus.PendingCaregiverApproval,
                AcceptanceOnUtc,
                BookingCaptureEvidence.Captured));

        BookingCancellationFact fact = BookingCancellationFact.Create(BookingId.New(), UserId.New(), decision);

        Assert.Equal(BookingStatus.PendingCaregiverApproval, fact.StatusAtCancellation);
        Assert.Equal(BookingCancellationAction.Reject, fact.Action);
        Assert.Null(fact.ConfirmedOnUtcUsed);
        Assert.Null(fact.ReasonCategory);
        Assert.Null(fact.ReasonNote);
        Assert.False(fact.IsCaregiverIncident);
        Assert.Equal(
            BookingRefundDecisionReason.CaregiverRejectionBeforeAcceptance,
            fact.RefundDecisionReason);
    }

    [Fact]
    public void FactCreate_RejectsEmptyIdsAndAMissingDecision()
    {
        BookingCancellationDecision decision = BookingCancellationPolicy.Decide(
            ConfirmedFamilyCancellation(BookingCancellationFeedback.Create(
                BookingCancellationReasonCategory.Other, "note")));

        Assert.Throws<DomainException>(() => BookingCancellationFact.Create(
            BookingId.Empty, UserId.New(), decision));

        Assert.Throws<DomainException>(() => BookingCancellationFact.Create(
            BookingId.New(), UserId.Empty, decision));

        Assert.Throws<ArgumentNullException>(() => BookingCancellationFact.Create(
            BookingId.New(), UserId.New(), null!));
    }

    // ---------------------------------------------------------------------------------------------
    // Public API shape: the reason cannot be substituted and a decision cannot be forged.
    // Only public declarations are inspected; internals are deliberately left alone.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void FactCreate_HasNoFeedbackParameterAndIsTheOnlyPublicFactory()
    {
        MethodInfo[] publicFactMethods = typeof(BookingCancellationFact)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        MethodInfo[] createOverloads = publicFactMethods
            .Where(method => method.Name == nameof(BookingCancellationFact.Create))
            .ToArray();

        MethodInfo create = Assert.Single(createOverloads);
        ParameterInfo[] parameters = create.GetParameters();

        Assert.Equal(
            "BookingId,UserId,BookingCancellationDecision",
            string.Join(",", parameters.Select(parameter => parameter.ParameterType.Name)));

        // No overload can carry a separate feedback/reason argument to swap in.
        Assert.DoesNotContain(
            parameters,
            parameter => parameter.ParameterType.Name.Contains("Feedback", StringComparison.Ordinal));

        Assert.Empty(typeof(BookingCancellationFact).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void Decision_CannotBeConstructedClonedOrMutatedByCallers()
    {
        Type decisionType = typeof(BookingCancellationDecision);

        // No public constructor of any kind, and the internal factory is not publicly reachable.
        Assert.Empty(decisionType.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(decisionType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance).Where(c => !c.IsPrivate));
        Assert.DoesNotContain(
            decisionType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly),
            method => method.Name == "Create");

        // Get-only properties: no setter and no init-only setter. A decision is never materialized by
        // EF (the fact is), so even a private setter would be unnecessary here and can be ruled out.
        PropertyInfo[] properties = decisionType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.NotEmpty(properties);
        Assert.All(properties, property => Assert.False(property.CanWrite, $"\"{property.Name}\" must be read-only."));

        // Records expose a compiler-generated clone used by `with`; a plain sealed class does not.
        Assert.DoesNotContain(
            decisionType.GetMethods(BindingFlags.Public | BindingFlags.Instance),
            method => method.Name == "<Clone>$");
    }

    // ---------------------------------------------------------------------------------------------
    // Durability and reproducibility of a decision.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void RecordedFact_IsUnchangedByLaterTransitionsOfTheOriginalBooking()
    {
        // Demonstrated only through existing public Booking transitions. This says nothing about how
        // a database stores the row: persistence-level append-only behaviour is a later stage.
        Booking booking = BookingCancellationBookingFactory.CreatePaidAndAcceptedBooking();

        BookingCancellationDecision decision = BookingCancellationPolicy.Decide(
            BookingCancellationPolicyInput.FromBooking(
                booking,
                BookingCancellationActorSide.Caregiver,
                BookingCancellationAction.Cancel,
                booking.ConfirmedOnUtc!.Value.AddMinutes(200),
                BookingCancellationFeedback.Create(
                    BookingCancellationReasonCategory.Emergency,
                    "family emergency at home")));

        BookingCancellationFact fact = BookingCancellationFact.Create(booking.Id, UserId.New(), decision);

        booking.CancelByCaregiver("family emergency at home", booking.ConfirmedOnUtc.Value.AddMinutes(201));
        booking.MarkRefunded("PM-REFUND-1", booking.ConfirmedOnUtc.Value.AddMinutes(202));

        Assert.Equal(BookingStatus.Refunded, booking.Status);
        Assert.Equal(BookingStatus.Confirmed, fact.StatusAtCancellation);
        Assert.Equal(BookingCancellationActorSide.Caregiver, fact.ActorSide);
        Assert.Equal(BookingCancellationAction.Cancel, fact.Action);
        Assert.True(fact.IsCaregiverIncident);
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, fact.RefundEntitlement);
        Assert.Equal(
            BookingRefundDecisionReason.CaregiverCancellationAfterAcceptance,
            fact.RefundDecisionReason);
        Assert.Equal(BookingCancellationReasonCategory.Emergency, fact.ReasonCategory);
        Assert.Equal(1, fact.PolicyVersion);
    }

    [Fact]
    public void RepeatedEqualInputs_ProduceEqualDecisionsAndLeaveTheInputUntouched()
    {
        BookingCancellationFeedback feedback = BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.MedicalIssues,
            "health reasons");

        BookingCancellationPolicyInput input = ConfirmedFamilyCancellation(feedback);
        BookingCancellationPolicyInput identical = ConfirmedFamilyCancellation(
            BookingCancellationFeedback.Create(BookingCancellationReasonCategory.MedicalIssues, "health reasons"));

        BookingCancellationDecision first = BookingCancellationPolicy.Decide(input);
        BookingCancellationDecision second = BookingCancellationPolicy.Decide(input);

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal(first, BookingCancellationPolicy.Decide(identical));

        // Decide is side-effect free: the input record and the reason it carries are unchanged.
        Assert.Equal(input, identical);
        Assert.Same(feedback, input.Feedback);
        Assert.Equal(BookingStatus.Confirmed, input.Status);
        Assert.Equal(BookingCaptureEvidence.Captured, input.CaptureEvidence);
        Assert.False(BookingCancellationPolicy.Decide(input).IsCaregiverIncident);
        Assert.Equal(feedback, input.Feedback);
    }

    [Fact]
    public void FeedbackWithADifferentNoteIsNotEqual_AndEqualityIgnoresInstanceIdentity()
    {
        BookingCancellationFeedback left = BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.Emergency, "same text");
        BookingCancellationFeedback sameAgain = BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.Emergency, "same text");
        BookingCancellationFeedback otherNote = BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.Emergency, "other text");
        BookingCancellationFeedback otherCategory = BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.Other, "same text");

        Assert.NotSame(left, sameAgain);
        Assert.Equal(left, sameAgain);
        Assert.Equal(left.GetHashCode(), sameAgain.GetHashCode());
        Assert.NotEqual(left, otherNote);
        Assert.NotEqual(left, otherCategory);

        BookingCancellationFeedback noteOnly = BookingCancellationFeedback.CreateOptionalNote("same text")!;
        Assert.NotEqual(left, noteOnly);
    }

    // ---------------------------------------------------------------------------------------------
    // Category set: exactly the five approved values, their Arabic labels, and read-only exposure.
    // ---------------------------------------------------------------------------------------------

    private static readonly BookingCancellationReasonCategory[] ApprovedCategories =
    [
        BookingCancellationReasonCategory.Emergency,
        BookingCancellationReasonCategory.MedicalIssues,
        BookingCancellationReasonCategory.TransportationIssues,
        BookingCancellationReasonCategory.AccountDeletion,
        BookingCancellationReasonCategory.Other
    ];

    private static readonly string[] ApprovedArabicLabels =
    [
        "حالة طارئة",
        "أسباب صحية",
        "مشكلات المواصلات",
        "حذف الحساب",
        "أسباب أخرى"
    ];

    [Fact]
    public void All_ContainsExactlyTheFiveApprovedCategoriesInPresentationOrder()
    {
        Assert.Equal(5, BookingCancellationReasonCategories.All.Count);

        for (int index = 0; index < ApprovedCategories.Length; index++)
        {
            Assert.Equal(ApprovedCategories[index], BookingCancellationReasonCategories.All[index]);
        }

        // No duplicate and no extra value beyond the approved five.
        Assert.Equal(5, BookingCancellationReasonCategories.All.Distinct().Count());
    }

    [Fact]
    public void GetArabicLabel_ReturnsTheApprovedLabelForEveryCategory()
    {
        for (int index = 0; index < ApprovedCategories.Length; index++)
        {
            Assert.Equal(
                ApprovedArabicLabels[index],
                BookingCancellationReasonCategories.GetArabicLabel(ApprovedCategories[index]));
        }

        // Every category has its own label; none is shared or defaulted.
        string[] labels = ApprovedCategories
            .Select(BookingCancellationReasonCategories.GetArabicLabel)
            .ToArray();
        Assert.Equal(ApprovedCategories.Length, labels.Distinct().Count());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    public void GetArabicLabel_And_Parse_RejectACategoryOutsideTheClosedSet(int raw)
    {
        BookingCancellationReasonCategory undefined = (BookingCancellationReasonCategory)raw;

        Assert.Throws<DomainException>(() => BookingCancellationReasonCategories.GetArabicLabel(undefined));
        Assert.Throws<DomainException>(() => BookingCancellationReasonCategories.Parse(raw));
        Assert.False(BookingCancellationReasonCategories.TryParse(raw, out _));
    }

    [Theory]
    [InlineData(1, BookingCancellationReasonCategory.Emergency)]
    [InlineData(2, BookingCancellationReasonCategory.MedicalIssues)]
    [InlineData(3, BookingCancellationReasonCategory.TransportationIssues)]
    [InlineData(4, BookingCancellationReasonCategory.AccountDeletion)]
    [InlineData(5, BookingCancellationReasonCategory.Other)]
    public void Parse_And_TryParse_MapDefinedNumbersToTheirCategory(
        int value,
        BookingCancellationReasonCategory expected)
    {
        Assert.Equal(expected, BookingCancellationReasonCategories.Parse(value));

        Assert.True(BookingCancellationReasonCategories.TryParse(value, out BookingCancellationReasonCategory parsed));
        Assert.Equal(expected, parsed);
    }

    [Fact]
    public void TryParse_OnInvalidValue_ReportsFailureWithoutProducingACategory()
    {
        Assert.False(BookingCancellationReasonCategories.TryParse(0, out BookingCancellationReasonCategory category));
        Assert.Equal((BookingCancellationReasonCategory)0, category);
        Assert.False(Enum.IsDefined(category));
    }

    [Fact]
    public void ExposedCategoryCollectionsCannotBeMutatedThroughTheAvailableInterfaces()
    {
        IReadOnlyList<BookingCancellationReasonCategory> exposed = BookingCancellationReasonCategories.All;

        IList<BookingCancellationReasonCategory> asList =
            Assert.IsAssignableFrom<IList<BookingCancellationReasonCategory>>(exposed);
        Assert.True(asList.IsReadOnly);

        Assert.Throws<NotSupportedException>(() => asList.Add(BookingCancellationReasonCategory.Emergency));
        Assert.Throws<NotSupportedException>(() => asList.Insert(0, BookingCancellationReasonCategory.Other));
        Assert.Throws<NotSupportedException>(() => asList[0] = BookingCancellationReasonCategory.Other);
        Assert.Throws<NotSupportedException>(() => asList.Remove(BookingCancellationReasonCategory.Emergency));
        Assert.Throws<NotSupportedException>(
            () => ((ICollection<BookingCancellationReasonCategory>)asList).Clear());

        // Later reads through the same property are unchanged. No private backing field is touched:
        // the guarantee under test is that the public surface cannot be bent through it.
        Assert.Equal(5, BookingCancellationReasonCategories.All.Count);
        for (int index = 0; index < ApprovedCategories.Length; index++)
        {
            Assert.Equal(ApprovedCategories[index], BookingCancellationReasonCategories.All[index]);
            Assert.Equal(
                ApprovedArabicLabels[index],
                BookingCancellationReasonCategories.GetArabicLabel(ApprovedCategories[index]));
        }
    }
}
