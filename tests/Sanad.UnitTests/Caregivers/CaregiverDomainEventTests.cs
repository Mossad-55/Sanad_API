using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Events;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverDomainEventTests
{
    [Fact]
    public void SubmitForReview_ShouldRaiseSubmissionEvent()
    {
        Caregiver caregiver =
            CreateReadyCompanion();

        caregiver.ClearDomainEvents();

        caregiver.SubmitForReview(
            CreateUtcDateTime(),
            CaregiverTestData.CurrentDate);

        CaregiverSubmittedForReviewDomainEvent domainEvent =
            Assert.Single(
                caregiver.DomainEvents
                    .OfType<
                        CaregiverSubmittedForReviewDomainEvent>());

        Assert.Equal(
            caregiver.Id,
            domainEvent.CaregiverId);

        Assert.False(
            domainEvent.IsResubmission);
    }

    [Fact]
    public void ResubmitForReview_ShouldRaiseResubmissionEvent()
    {
        Caregiver caregiver =
            CreatePendingReviewCompanion();

        caregiver.RequestCorrection(
            "Correction required.",
            CreateUtcDateTime()
                .AddMinutes(1));

        caregiver.ClearDomainEvents();

        caregiver.ResubmitForReview(
            CreateUtcDateTime()
                .AddMinutes(2),
            CaregiverTestData.CurrentDate);

        CaregiverSubmittedForReviewDomainEvent domainEvent =
            Assert.Single(
                caregiver.DomainEvents
                    .OfType<
                        CaregiverSubmittedForReviewDomainEvent>());

        Assert.Equal(
            caregiver.Id,
            domainEvent.CaregiverId);

        Assert.True(
            domainEvent.IsResubmission);
    }

    [Fact]
    public void RequestCorrection_ShouldRaiseCorrectionEvent()
    {
        Caregiver caregiver =
            CreatePendingReviewCompanion();

        caregiver.ClearDomainEvents();

        caregiver.RequestCorrection(
            "  Update the profile image.  ",
            CreateUtcDateTime()
                .AddMinutes(1));

        CaregiverCorrectionRequestedDomainEvent domainEvent =
            Assert.Single(
                caregiver.DomainEvents
                    .OfType<
                        CaregiverCorrectionRequestedDomainEvent>());

        Assert.Equal(
            caregiver.Id,
            domainEvent.CaregiverId);

        Assert.Equal(
            "Update the profile image.",
            domainEvent.Reason);
    }

    [Fact]
    public void Approve_ShouldRaiseApprovedEvent()
    {
        Caregiver caregiver =
            CreatePendingReviewCompanion();

        CaregiverTestData
            .EnsureReadyForActivation(
                caregiver);

        caregiver.ClearDomainEvents();

        caregiver.Approve(
            CreateUtcDateTime()
                .AddMinutes(1),
            CaregiverTestData.CurrentDate);

        CaregiverApprovedDomainEvent domainEvent =
            Assert.Single(
                caregiver.DomainEvents
                    .OfType<
                        CaregiverApprovedDomainEvent>());

        Assert.Equal(
            caregiver.Id,
            domainEvent.CaregiverId);
    }

    [Fact]
    public void RejectApplication_ShouldRaiseRejectedEvent()
    {
        Caregiver caregiver =
            CreatePendingReviewCompanion();

        caregiver.ClearDomainEvents();

        caregiver.RejectApplication(
            "  Final rejection reason.  ",
            CreateUtcDateTime()
                .AddMinutes(1));

        CaregiverRejectedDomainEvent domainEvent =
            Assert.Single(
                caregiver.DomainEvents
                    .OfType<
                        CaregiverRejectedDomainEvent>());

        Assert.Equal(
            caregiver.Id,
            domainEvent.CaregiverId);

        Assert.Equal(
            "Final rejection reason.",
            domainEvent.Reason);
    }

    [Fact]
    public void Suspend_ShouldRaiseSuspendedEvent()
    {
        Caregiver caregiver =
            CreateActiveCompanion();

        caregiver.ClearDomainEvents();

        caregiver.Suspend(
            "  Compliance review.  ",
            CreateUtcDateTime()
                .AddMinutes(2));

        CaregiverSuspendedDomainEvent domainEvent =
            Assert.Single(
                caregiver.DomainEvents
                    .OfType<
                        CaregiverSuspendedDomainEvent>());

        Assert.Equal(
            caregiver.Id,
            domainEvent.CaregiverId);

        Assert.Equal(
            "Compliance review.",
            domainEvent.Reason);
    }

    [Fact]
    public void Reactivate_ShouldRaiseReactivatedEvent()
    {
        Caregiver caregiver =
            CreateActiveCompanion();

        caregiver.Suspend(
            "Temporary suspension.",
            CreateUtcDateTime()
                .AddMinutes(2));

        caregiver.ClearDomainEvents();

        CaregiverTestData
            .EnsureReadyForActivation(
                caregiver);

        caregiver.Reactivate(
            CreateUtcDateTime()
                .AddMinutes(3),
            CaregiverTestData.CurrentDate);

        CaregiverReactivatedDomainEvent domainEvent =
            Assert.Single(
                caregiver.DomainEvents
                    .OfType<
                        CaregiverReactivatedDomainEvent>());

        Assert.Equal(
            caregiver.Id,
            domainEvent.CaregiverId);
    }

    [Fact]
    public void ActiveMedicalProfileChange_ShouldRaiseReviewRequiredEvent()
    {
        Caregiver caregiver =
            CreateActiveMedical();

        caregiver.ClearDomainEvents();

        caregiver.UpdateMedicalProfile(
            ProfessionalTitle.Create(
                "ممرض أول",
                "Senior Nurse",
                true),
            yearsOfExperience: 10,
            Specialization.Create(
                "تمريض منزلي",
                "Home Nursing",
                true,
                CaregiverType.Medical),
            AcademicDegree.Create(
                "ماجستير تمريض",
                "Master of Nursing",
                true),
            currentWorkplace: null,
            biography: null,
            CaregiverTestData.CurrentUtc
                .AddHours(1));

        CaregiverReviewRequiredDomainEvent domainEvent =
            Assert.Single(
                caregiver.DomainEvents
                    .OfType<
                        CaregiverReviewRequiredDomainEvent>());

        Assert.Equal(
            caregiver.Id,
            domainEvent.CaregiverId);

        Assert.Equal(
            CaregiverReviewTrigger
                .MedicalProfessionalProfileChanged,
            domainEvent.Trigger);
    }

    [Fact]
    public void ActiveMandatoryCertificateReplacement_ShouldRaiseReviewRequiredEvent()
    {
        Caregiver caregiver =
            CreateActiveMedical();

        CaregiverCertificate practiceLicense =
            GetCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        caregiver.ClearDomainEvents();

        caregiver.UpdateCertificateFile(
            practiceLicense.Id,
            "certificates/new-practice-license.jpg",
            expiryDate: null,
            CaregiverTestData.CurrentDate,
            CaregiverTestData.CurrentUtc
                .AddHours(1));

        CaregiverReviewRequiredDomainEvent domainEvent =
            Assert.Single(
                caregiver.DomainEvents
                    .OfType<
                        CaregiverReviewRequiredDomainEvent>());

        Assert.Equal(
            caregiver.Id,
            domainEvent.CaregiverId);

        Assert.Equal(
            CaregiverReviewTrigger
                .MandatoryCertificateReplaced,
            domainEvent.Trigger);
    }

    [Fact]
    public void ActiveMandatoryCertificateRevocation_ShouldRaiseSuspendedEvent()
    {
        Caregiver caregiver =
            CreateActiveMedical();

        CaregiverCertificate practiceLicense =
            GetCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        caregiver.ClearDomainEvents();

        caregiver.RevokeCertificate(
            practiceLicense.Id,
            "License approval withdrawn.",
            CaregiverTestData.CurrentUtc
                .AddHours(1));

        CaregiverSuspendedDomainEvent domainEvent =
            Assert.Single(
                caregiver.DomainEvents
                    .OfType<
                        CaregiverSuspendedDomainEvent>());

        Assert.Equal(
            caregiver.Id,
            domainEvent.CaregiverId);

        Assert.Equal(
            "License approval withdrawn.",
            domainEvent.Reason);
    }

    [Fact]
    public void MandatoryCertificateExpiry_ShouldRaiseSuspendedEvent()
    {
        DateOnly currentDate =
            CaregiverTestData.CurrentDate;

        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                CaregiverType.Medical);

        caregiver.AddCertificate(
            CaregiverCertificateType.PracticeLicense,
            "certificates/practice-license.jpg",
            expiryDate: currentDate,
            currentDate);

        CaregiverTestData
            .EnsureReadyForActivation(
                caregiver);

        caregiver.TransitionToActive();

        CaregiverCertificate practiceLicense =
            GetCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        caregiver.ClearDomainEvents();

        caregiver.SuspendForExpiredMandatoryCertificate(
            practiceLicense.Id,
            currentDate.AddDays(1),
            CaregiverTestData.CurrentUtc
                .AddDays(1));

        CaregiverSuspendedDomainEvent domainEvent =
            Assert.Single(
                caregiver.DomainEvents
                    .OfType<
                        CaregiverSuspendedDomainEvent>());

        Assert.Equal(
            caregiver.Id,
            domainEvent.CaregiverId);

        Assert.Equal(
            "PracticeLicense has expired.",
            domainEvent.Reason);
    }

    [Fact]
    public void FailedTransition_ShouldNotRaiseDomainEvent()
    {
        Caregiver caregiver =
            CreateReadyCompanion();

        caregiver.ClearDomainEvents();

        Assert.Throws<DomainException>(
            () => caregiver.RequestCorrection(
                "Invalid transition.",
                CreateUtcDateTime()));

        Assert.Empty(caregiver.DomainEvents);
    }

    [Fact]
    public void FailedReviewSensitiveUpdate_ShouldNotRaiseDomainEvent()
    {
        Caregiver caregiver =
            CreateActiveMedical();

        caregiver.ClearDomainEvents();

        Assert.Throws<DomainException>(
            () => caregiver.UpdateMedicalProfile(
                ProfessionalTitle.Create(
                    "ممرض أول",
                    "Senior Nurse",
                    true),
                yearsOfExperience: -1,
                Specialization.Create(
                    "تمريض منزلي",
                    "Home Nursing",
                    true,
                    CaregiverType.Medical),
                AcademicDegree.Create(
                    "ماجستير تمريض",
                    "Master of Nursing", true),
                currentWorkplace: null,
                biography: null,
                CaregiverTestData.CurrentUtc
                    .AddHours(1)));

        Assert.Empty(caregiver.DomainEvents);
    }

    [Fact]
    public void OnboardingMedicalProfileChange_ShouldNotRaiseReviewRequiredEvent()
    {
        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                CaregiverType.Medical);

        caregiver.ClearDomainEvents();

        caregiver.UpdateMedicalProfile(
            ProfessionalTitle.Create(
                "ممرض مسجل",
                "Registered Nurse",
                true),
            yearsOfExperience: 5,
            Specialization.Create(
                "تمريض كبار السن",
                "Elderly Nursing",
                true,
                CaregiverType.Medical),
            AcademicDegree.Create(
                "بكالوريوس تمريض",
                "Bachelor of Nursing",
                true),
            currentWorkplace: null,
            biography: null,
            CaregiverTestData.CurrentUtc);

        Assert.Empty(
            caregiver.DomainEvents
                .OfType<
                    CaregiverReviewRequiredDomainEvent>());
    }

    [Fact]
    public void AdditionalCertificateReplacement_ShouldNotRaiseReviewRequiredEvent()
    {
        Caregiver caregiver =
            CreateActiveMedical();

        caregiver.AddCertificate(
            CaregiverCertificateType.AdditionalCertificate,
            "certificates/additional.jpg",
            expiryDate: null,
            CaregiverTestData.CurrentDate);

        CaregiverCertificate certificate =
            GetCertificate(
                caregiver,
                CaregiverCertificateType.AdditionalCertificate);

        caregiver.ClearDomainEvents();

        caregiver.UpdateCertificateFile(
            certificate.Id,
            "certificates/new-additional.jpg",
            expiryDate: null,
            CaregiverTestData.CurrentDate,
            CaregiverTestData.CurrentUtc
                .AddHours(1));

        Assert.Empty(
            caregiver.DomainEvents
                .OfType<
                    CaregiverReviewRequiredDomainEvent>());

        Assert.Equal(
            CaregiverStatus.Active,
            caregiver.Status);
    }

    [Fact]
    public void AdditionalCertificateRevocation_ShouldNotRaiseSuspendedEvent()
    {
        Caregiver caregiver =
            CreateActiveMedical();

        caregiver.AddCertificate(
            CaregiverCertificateType.AdditionalCertificate,
            "certificates/additional.jpg",
            expiryDate: null,
            CaregiverTestData.CurrentDate);

        CaregiverCertificate certificate =
            GetCertificate(
                caregiver,
                CaregiverCertificateType.AdditionalCertificate);

        caregiver.VerifyCertificate(
            certificate.Id);

        caregiver.ClearDomainEvents();

        caregiver.RevokeCertificate(
            certificate.Id,
            "Additional Certificate revoked.",
            CaregiverTestData.CurrentUtc
                .AddHours(1));

        Assert.Empty(
            caregiver.DomainEvents
                .OfType<
                    CaregiverSuspendedDomainEvent>());

        Assert.Equal(
            CaregiverStatus.Active,
            caregiver.Status);
    }

    [Fact]
    public void OnboardingMandatoryCertificateReplacement_ShouldNotRaiseReviewRequiredEvent()
    {
        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                CaregiverType.Medical);

        caregiver.AddCertificate(
            CaregiverCertificateType.PracticeLicense,
            "certificates/practice-license.jpg",
            expiryDate: null,
            CaregiverTestData.CurrentDate);

        CaregiverCertificate certificate =
            GetCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        caregiver.ClearDomainEvents();

        caregiver.UpdateCertificateFile(
            certificate.Id,
            "certificates/new-practice-license.jpg",
            expiryDate: null,
            CaregiverTestData.CurrentDate,
            CaregiverTestData.CurrentUtc);

        Assert.Empty(
            caregiver.DomainEvents
                .OfType<
                    CaregiverReviewRequiredDomainEvent>());

        Assert.Equal(
            CaregiverStatus.Onboarding,
            caregiver.Status);
    }

    private static Caregiver CreateReadyCompanion()
    {
        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                CaregiverType.Companion);

        CaregiverTestData
            .EnsureReadyForSubmission(
                caregiver);

        return caregiver;
    }

    private static Caregiver CreatePendingReviewCompanion()
    {
        Caregiver caregiver =
            CreateReadyCompanion();

        caregiver.SubmitForReview(
            CreateUtcDateTime(),
            CaregiverTestData.CurrentDate);

        return caregiver;
    }

    private static Caregiver CreateActiveCompanion()
    {
        Caregiver caregiver =
            CreatePendingReviewCompanion();

        CaregiverTestData
            .EnsureReadyForActivation(
                caregiver);

        caregiver.Approve(
            CreateUtcDateTime()
                .AddMinutes(1),
            CaregiverTestData.CurrentDate);

        return caregiver;
    }

    private static Caregiver CreateActiveMedical()
    {
        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                CaregiverType.Medical);

        CaregiverTestData
            .EnsureReadyForActivation(
                caregiver);

        caregiver.TransitionToActive();

        return caregiver;
    }

    private static CaregiverCertificate GetCertificate(
        Caregiver caregiver,
        CaregiverCertificateType certificateType)
    {
        return caregiver.Certificates.Single(
            certificate =>
                certificate.Type ==
                certificateType);
    }

    private static DateTime CreateUtcDateTime()
    {
        return new DateTime(
            2026,
            8,
            20,
            10,
            0,
            0,
            DateTimeKind.Utc);
    }
}