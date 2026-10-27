using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sanad.API.Controllers;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Infrastructure.Storage;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Caregivers.Application.Lookups;
using Sanad.Modules.Caregivers.Application.Onboarding;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Cms.Application.Landing;
using Sanad.Modules.Cms.Application.Splash;
using Sanad.Modules.Families.Application.Assessments;
using Sanad.Modules.Families.Application.Elderlies;
using Sanad.Modules.Families.Application.Reports;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Identity.Application.Users;
using Sanad.UnitTests.CareHomes;

namespace Sanad.UnitTests.API;

/// <summary>
/// Bounded media correctness in the phase's assigned surfaces: declared MIME
/// types are verified against the actual bytes before an upload is accepted,
/// landing image paths must be existing public landing assets, and every
/// authenticated image/document read enables byte ranges so a client can seek
/// or resume instead of re-downloading.
/// </summary>
public sealed class MediaUploadHardeningTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 8, 0, 0, DateTimeKind.Utc);

    private static readonly byte[] PngBytes =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03];

    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    private static readonly byte[] SvgBytes =
        "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"u8.ToArray();

    private static readonly byte[] WebpBytes =
        [0x52, 0x49, 0x46, 0x46, 0x04, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0x01, 0x02];

    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37, 0x0A];

    private const string LandingSectionType = "hero";

    // ---- byte-signature hardening ---------------------------------------------

    [Theory]
    [InlineData("image/png", "cover.png")]
    [InlineData("image/svg+xml", "cover.svg")]
    [InlineData("text/html", "cover.png")]
    public async Task Splash_upload_refuses_a_declared_type_without_a_matching_signature(
        string contentType, string fileName)
    {
        var storage = new RecordingStorage();
        var controller = new AdminSplashScreensController(new RejectingSender(), storage)
        {
            ControllerContext = ControllerContextFor("splash-trace")
        };

        IActionResult result = await controller.Create(
            SplashRequest(), FormFile(SvgBytes, contentType, fileName), CancellationToken.None);

        AssertProblem(result, 400, "Storage.File.UnsupportedType", "splash-trace");
        Assert.Equal(0, storage.SaveCount);
    }

    [Fact]
    public async Task Splash_upload_refuses_empty_and_oversize_bodies()
    {
        var storage = new RecordingStorage();
        var controller = new AdminSplashScreensController(new RejectingSender(), storage)
        {
            ControllerContext = ControllerContextFor("splash-size-trace")
        };

        AssertProblem(
            await controller.Create(SplashRequest(), null!, CancellationToken.None),
            400, "Storage.File.Empty", "splash-size-trace");
        AssertProblem(
            await controller.Create(
                SplashRequest(), FormFile([], "image/png", "cover.png"), CancellationToken.None),
            400, "Storage.File.Empty", "splash-size-trace");
        AssertProblem(
            await controller.Create(
                SplashRequest(), OversizeFile(PngBytes, "image/png", "cover.png"), CancellationToken.None),
            400, "Storage.File.TooLarge", "splash-size-trace");

        Assert.Equal(0, storage.SaveCount);
    }

    [Fact]
    public async Task Splash_upload_accepts_a_real_png_and_cleans_it_up_when_the_handler_fails()
    {
        var storage = new RecordingStorage();
        var sender = new RejectingSender();
        var controller = new AdminSplashScreensController(sender, storage)
        {
            ControllerContext = ControllerContextFor("splash-accept-trace")
        };

        IActionResult result = await controller.Create(
            SplashRequest(), FormFile(PngBytes, "image/png", "cover.png"), CancellationToken.None);

        Assert.IsType<ObjectResult>(result);
        Assert.IsType<CreateSplashScreenCommand>(sender.LastRequest);
        string uploaded = storage.SavedKeys.Single();
        Assert.Equal([uploaded], storage.DeletedKeys);
    }

    [Theory]
    [InlineData("image/png", "cover.png")]
    [InlineData("image/svg+xml", "cover.svg")]
    public async Task Assessment_tier_upload_refuses_a_declared_type_without_a_matching_signature(
        string contentType, string fileName)
    {
        var storage = new RecordingStorage();
        var controller = new AdminAssessmentsController(new RejectingSender(), storage)
        {
            ControllerContext = ControllerContextFor("tier-trace")
        };

        IActionResult result = await controller.CreateTier(
            TierRequest(), FormFile(SvgBytes, contentType, fileName), CancellationToken.None);

        AssertProblem(result, 400, "Storage.File.UnsupportedType", "tier-trace");
        Assert.Equal(0, storage.SaveCount);
    }

    [Fact]
    public async Task Assessment_tier_upload_accepts_a_real_jpeg_and_refuses_oversize_bodies()
    {
        var storage = new RecordingStorage();
        var sender = new RejectingSender();
        var controller = new AdminAssessmentsController(sender, storage)
        {
            ControllerContext = ControllerContextFor("tier-accept-trace")
        };

        IActionResult accepted = await controller.CreateTier(
            TierRequest(), FormFile(JpegBytes, "image/jpeg", "tier.jpg"), CancellationToken.None);

        Assert.IsType<ObjectResult>(accepted);
        Assert.IsType<CreateAssessmentTierCommand>(sender.LastRequest);
        Assert.Equal([storage.SavedKeys.Single()], storage.DeletedKeys);

        IActionResult oversize = await controller.CreateTier(
            TierRequest(), OversizeFile(JpegBytes, "image/jpeg", "tier.jpg"), CancellationToken.None);

        AssertProblem(oversize, 400, "Storage.File.TooLarge", "tier-accept-trace");
        Assert.Equal(1, storage.SaveCount);
    }

    [Theory]
    [InlineData("image/png", "icon.png")]
    [InlineData("image/svg+xml", "icon.svg")]
    public async Task Service_icon_upload_refuses_a_declared_type_without_a_matching_signature(
        string contentType, string fileName)
    {
        var storage = new RecordingStorage();
        var controller = new AdminLookupsController(new RejectingSender(), storage)
        {
            ControllerContext = ControllerContextFor("icon-trace")
        };

        IActionResult result = await controller.CreateService(
            ServiceRequest(), FormFile(SvgBytes, contentType, fileName), CancellationToken.None);

        AssertProblem(result, 400, "Storage.File.UnsupportedType", "icon-trace");
        Assert.Equal(0, storage.SaveCount);
    }

    [Fact]
    public async Task Service_icon_upload_accepts_a_real_png_and_refuses_oversize_bodies()
    {
        var storage = new RecordingStorage();
        var sender = new RejectingSender();
        var controller = new AdminLookupsController(sender, storage)
        {
            ControllerContext = ControllerContextFor("icon-accept-trace")
        };

        IActionResult accepted = await controller.CreateService(
            ServiceRequest(), FormFile(PngBytes, "image/png", "icon.png"), CancellationToken.None);

        Assert.IsType<ObjectResult>(accepted);
        Assert.IsType<CreateServiceCommand>(sender.LastRequest);
        Assert.Equal([storage.SavedKeys.Single()], storage.DeletedKeys);

        IActionResult oversize = await controller.CreateService(
            ServiceRequest(), OversizeFile(PngBytes, "image/png", "icon.png"), CancellationToken.None);

        AssertProblem(oversize, 400, "Storage.File.TooLarge", "icon-accept-trace");
        Assert.Equal(1, storage.SaveCount);
    }

    // ---- landing image path ----------------------------------------------------

    [Theory]
    [InlineData("private/landing/hero.png")]
    [InlineData("library/cover.png")]
    [InlineData("landing/../private/secret.png")]
    [InlineData("https://cdn.example.org/hero.png")]
    public async Task Landing_section_creation_refuses_a_reference_that_is_not_a_public_landing_asset(
        string imagePath)
    {
        using TempStorageRoot root = TempStorageRoot.Create();
        var sender = new RejectingSender();
        AdminLandingController controller = CreateLandingController(sender, root);

        IActionResult result = await controller.CreateSection(
            LandingCreateRequest(imagePath), CancellationToken.None);

        AssertProblem(result, 400, "Cms.Landing.Media.InvalidKey", string.Empty, checkTrace: false);
        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public async Task Landing_section_creation_refuses_a_missing_public_key_and_accepts_an_existing_one()
    {
        using TempStorageRoot root = TempStorageRoot.Create();
        var sender = new RejectingSender();
        AdminLandingController controller = CreateLandingController(sender, root);

        IActionResult missing = await controller.CreateSection(
            LandingCreateRequest("landing/hero.png"), CancellationToken.None);
        AssertProblem(missing, 404, "Cms.Landing.Media.NotFound", string.Empty, checkTrace: false);
        Assert.Null(sender.LastRequest);

        root.Write("landing/hero.png", PngBytes);
        IActionResult accepted = await controller.CreateSection(
            LandingCreateRequest(" Landing/Hero.PNG "), CancellationToken.None);

        Assert.IsType<ObjectResult>(accepted);
        var command = Assert.IsType<CreateLandingSectionCommand>(sender.LastRequest);
        Assert.Equal("landing/hero.png", command.ImagePath);
    }

    [Fact]
    public async Task Landing_section_update_applies_the_same_image_path_rules()
    {
        using TempStorageRoot root = TempStorageRoot.Create();
        var sender = new RejectingSender();
        AdminLandingController controller = CreateLandingController(sender, root);
        root.Write("landing/about.png", PngBytes);

        IActionResult rejected = await controller.UpdateSection(
            Guid.NewGuid(),
            new UpdateLandingSectionRequest(null, null, null, null, null, "library/cover.png"),
            CancellationToken.None);
        AssertProblem(rejected, 400, "Cms.Landing.Media.InvalidKey", string.Empty, checkTrace: false);

        IActionResult accepted = await controller.UpdateSection(
            Guid.NewGuid(),
            new UpdateLandingSectionRequest(null, null, null, null, null, "landing/about.png"),
            CancellationToken.None);

        Assert.IsType<ObjectResult>(accepted);
        var command = Assert.IsType<UpdateLandingSectionCommand>(sender.LastRequest);
        Assert.Equal("landing/about.png", command.ImagePath);
    }

    // ---- range-enabled reads ---------------------------------------------------

    [Fact]
    public async Task Family_report_photo_serves_with_range_support()
    {
        var storage = new FixedStorage(JpegBytes, "image/jpeg");
        var controller = new FamilyReportsController(new PhotoSender(), storage)
        {
            ControllerContext = ClaimsContextFor("family-report-trace", UserId.New())
        };

        IActionResult result = await controller.ReadMedicalReportPhoto(Guid.NewGuid(), CancellationToken.None);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.True(file.EnableRangeProcessing);
        Assert.Equal("image/jpeg", file.ContentType);
    }

    [Fact]
    public async Task Caregiver_medical_report_photo_serves_with_range_support()
    {
        var storage = new FixedStorage(JpegBytes, "image/jpeg");
        var controller = new CaregiverBookingsController(
            new PhotoSender(), null!, new FixedClock(), storage)
        {
            ControllerContext = ClaimsContextFor("caregiver-report-trace", UserId.New())
        };

        IActionResult result = await controller.ReadMedicalReportPhoto(Guid.NewGuid(), CancellationToken.None);

        Assert.True(Assert.IsType<FileStreamResult>(result).EnableRangeProcessing);
    }

    [Fact]
    public async Task Care_home_room_media_serves_with_range_support()
    {
        await using CareHomesDbContext db = CreateCareHomesDb();
        (CareHomeFacility facility, _) = await AddApprovedFacilityAsync(db);
        CareHomeRoomType type = CareHomeRoomType.Create(
            facility.Id, "غرفة", "Room", 100m, CareHomeAllocationMode.Shared, Now);
        db.RoomTypes.Add(type);
        var media = CareHomeRoomTypeMedia.Create(
            type.Id, CareHomeRoomTypeMediaKind.Cover, 0, "private/care-home-media/cover.png",
            "image/png", 10, Now);
        db.RoomTypeMedia.Add(media);
        await db.SaveChangesAsync();
        var controller = new CareHomeRoomMediaController(
            db, new FixedStorage(PngBytes, "image/png"))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        IActionResult result = await controller.ReadRoomTypeFile(
            facility.Id.Value, type.Id, media.Id, CancellationToken.None);

        Assert.True(Assert.IsType<FileStreamResult>(result).EnableRangeProcessing);
    }

    [Fact]
    public async Task Care_home_profile_media_serves_with_range_support()
    {
        await using CareHomesDbContext db = CreateCareHomesDb();
        (CareHomeFacility facility, _) = await AddApprovedFacilityAsync(db);
        Guid revisionId = facility.ApprovedRevisionId!.Value;
        var media = CareHomeProfileMedia.Create(
            revisionId, CareHomeProfileMediaKind.Cover, 0, "private/care-home-media/cover.png",
            "image/png", 10, Now);
        db.ProfileMedia.Add(media);
        await db.SaveChangesAsync();
        var controller = new CareHomeProfileMediaController(
            new RejectingSender(), db, new FixedStorage(PngBytes, "image/png"))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        IActionResult result = await controller.ReadApproved(
            facility.Id.Value, media.Id, CancellationToken.None);

        Assert.True(Assert.IsType<FileStreamResult>(result).EnableRangeProcessing);
    }

    // ---- private upload signature hardening (avatar/identity/certificate/photo) ----

    [Theory]
    [InlineData("image/jpeg", "avatar.jpg")]
    [InlineData("image/png", "avatar.png")]
    [InlineData("image/webp", "avatar.webp")]
    [InlineData("application/pdf", "avatar.pdf")]
    [InlineData("image/svg+xml", "avatar.svg")]
    public async Task Avatar_upload_refuses_a_declared_type_without_a_matching_signature(
        string contentType, string fileName)
    {
        var storage = new RecordingPrivateStorage();
        var controller = new AuthController(new RejectingSender(), storage)
        {
            ControllerContext = ClaimsContextFor("avatar-signature-trace", UserId.New())
        };

        IActionResult result = await controller.UpsertAvatar(
            FormFile(SvgBytes, contentType, fileName), CancellationToken.None);

        AssertProblem(result, 400, "Storage.File.UnsupportedType", "avatar-signature-trace");
        Assert.Equal(0, storage.SaveCount);
        Assert.Empty(storage.DeletedKeys);
    }

    [Fact]
    public async Task Avatar_upload_refuses_empty_and_oversize_bodies()
    {
        var storage = new RecordingPrivateStorage();
        var controller = new AuthController(new RejectingSender(), storage)
        {
            ControllerContext = ClaimsContextFor("avatar-size-trace", UserId.New())
        };

        AssertProblem(
            await controller.UpsertAvatar(null!, CancellationToken.None),
            400, "Storage.File.Empty", "avatar-size-trace");
        AssertProblem(
            await controller.UpsertAvatar(
                FormFile([], "image/png", "avatar.png"), CancellationToken.None),
            400, "Storage.File.Empty", "avatar-size-trace");
        AssertProblem(
            await controller.UpsertAvatar(
                OversizePrivateFile(PngBytes, "image/png", "avatar.png"), CancellationToken.None),
            400, "Storage.File.TooLarge", "avatar-size-trace");

        Assert.Equal(0, storage.SaveCount);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public async Task Avatar_upload_accepts_each_documented_signature(string contentType)
    {
        var storage = new RecordingPrivateStorage();
        var sender = new RejectingSender();
        var controller = new AuthController(sender, storage)
        {
            ControllerContext = ClaimsContextFor("avatar-accept-trace", UserId.New())
        };

        IActionResult result = await controller.UpsertAvatar(
            FormFile(BytesFor(contentType), contentType, "avatar.bin"), CancellationToken.None);

        Assert.IsType<ObjectResult>(result);
        Assert.IsType<UpsertUserAvatarCommand>(sender.LastRequest);
        PrivateSave save = Assert.Single(storage.PrivateSaves);
        Assert.Equal("avatars", save.Folder);
        Assert.Equal(contentType, save.ContentType);
        Assert.Equal(BytesFor(contentType), save.Bytes);
        Assert.Equal([save.Key], storage.DeletedKeys);
    }

    [Theory]
    [InlineData("image/jpeg", "front.jpg")]
    [InlineData("image/png", "front.png")]
    [InlineData("image/webp", "front.webp")]
    [InlineData("application/pdf", "front.pdf")]
    [InlineData("image/svg+xml", "front.svg")]
    public async Task Identity_document_upload_refuses_a_declared_type_without_a_matching_signature(
        string contentType, string fileName)
    {
        var storage = new RecordingPrivateStorage();
        var sender = new RejectingSender();
        var controller = new AuthController(sender, storage)
        {
            ControllerContext = ClaimsContextFor("identity-signature-trace", UserId.New())
        };

        IActionResult result = await controller.UpsertIdentityDocument(
            FormFile(SvgBytes, contentType, fileName),
            FormFile(SvgBytes, contentType, fileName),
            CancellationToken.None);

        AssertProblem(result, 400, "Storage.File.UnsupportedType", "identity-signature-trace");
        Assert.Equal(0, storage.SaveCount);
        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public async Task Identity_document_upload_rejects_a_mismatched_back_image_and_compensates_the_front()
    {
        var storage = new RecordingPrivateStorage();
        var sender = new RejectingSender();
        var controller = new AuthController(sender, storage)
        {
            ControllerContext = ClaimsContextFor("identity-back-trace", UserId.New())
        };

        IActionResult result = await controller.UpsertIdentityDocument(
            FormFile(JpegBytes, "image/jpeg", "front.jpg"),
            FormFile(SvgBytes, "image/png", "back.png"),
            CancellationToken.None);

        AssertProblem(result, 400, "Storage.File.UnsupportedType", "identity-back-trace");
        PrivateSave front = Assert.Single(storage.PrivateSaves);
        Assert.Equal("identity-documents", front.Folder);
        Assert.Equal([front.Key], storage.DeletedKeys);
        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public async Task Identity_document_upload_accepts_matching_signatures_and_uses_the_private_folder()
    {
        var storage = new RecordingPrivateStorage();
        var sender = new RejectingSender();
        var controller = new AuthController(sender, storage)
        {
            ControllerContext = ClaimsContextFor("identity-accept-trace", UserId.New())
        };

        IActionResult result = await controller.UpsertIdentityDocument(
            FormFile(PngBytes, "image/png", "front.png"),
            FormFile(WebpBytes, "image/webp", "back.webp"),
            CancellationToken.None);

        Assert.IsType<ObjectResult>(result);
        var command = Assert.IsType<UpsertIdentityDocumentCommand>(sender.LastRequest);
        Assert.Equal(2, storage.PrivateSaves.Count);
        Assert.All(storage.PrivateSaves, save => Assert.Equal("identity-documents", save.Folder));
        Assert.Equal(
            [storage.PrivateSaves[0].Key, storage.PrivateSaves[1].Key],
            storage.DeletedKeys);
        Assert.Equal(command.FrontImagePath, storage.PrivateSaves[0].Key);
        Assert.Equal(command.BackImagePath, storage.PrivateSaves[1].Key);
    }

    [Theory]
    [InlineData("application/pdf", "scan.pdf")]
    [InlineData("image/jpeg", "scan.jpg")]
    [InlineData("image/png", "scan.png")]
    [InlineData("image/webp", "scan.webp")]
    [InlineData("image/svg+xml", "scan.svg")]
    [InlineData("video/mp4", "scan.mp4")]
    public async Task Certificate_upload_refuses_a_declared_type_without_a_matching_signature(
        string contentType, string fileName)
    {
        var storage = new RecordingPrivateStorage();
        var sender = new RejectingSender();
        var controller = new CaregiverController(sender, storage, new FixedClock())
        {
            ControllerContext = ClaimsContextFor("certificate-signature-trace", UserId.New())
        };

        IActionResult result = await controller.AddCertificate(
            new AddCertificateRequest(CaregiverCertificateType.PracticeLicense, null),
            FormFile(SvgBytes, contentType, fileName),
            CancellationToken.None);

        AssertProblem(result, 400, "Storage.File.UnsupportedType", "certificate-signature-trace");
        Assert.Equal(0, storage.SaveCount);
        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public async Task Certificate_upload_refuses_empty_and_oversize_bodies()
    {
        var storage = new RecordingPrivateStorage();
        var controller = new CaregiverController(new RejectingSender(), storage, new FixedClock())
        {
            ControllerContext = ClaimsContextFor("certificate-size-trace", UserId.New())
        };

        AssertProblem(
            await controller.AddCertificate(
                new AddCertificateRequest(CaregiverCertificateType.PracticeLicense, null),
                null!,
                CancellationToken.None),
            400, "Storage.File.Empty", "certificate-size-trace");
        AssertProblem(
            await controller.AddCertificate(
                new AddCertificateRequest(CaregiverCertificateType.PracticeLicense, null),
                OversizePrivateFile(PdfBytes, "application/pdf", "scan.pdf"),
                CancellationToken.None),
            400, "Storage.File.TooLarge", "certificate-size-trace");

        Assert.Equal(0, storage.SaveCount);
    }

    [Fact]
    public async Task Certificate_upload_accepts_a_pdf_and_a_real_image_and_cleans_up_on_failure()
    {
        var storage = new RecordingPrivateStorage();
        var sender = new RejectingSender();
        var controller = new CaregiverController(sender, storage, new FixedClock())
        {
            ControllerContext = ClaimsContextFor("certificate-accept-trace", UserId.New())
        };

        IActionResult added = await controller.AddCertificate(
            new AddCertificateRequest(CaregiverCertificateType.PracticeLicense, null),
            FormFile(PdfBytes, "application/pdf", "scan.pdf"),
            CancellationToken.None);

        Assert.IsType<ObjectResult>(added);
        Assert.IsType<AddCertificateCommand>(sender.LastRequest);
        PrivateSave pdfSave = Assert.Single(storage.PrivateSaves);
        Assert.Equal("caregiver-certificates", pdfSave.Folder);
        Assert.Equal("application/pdf", pdfSave.ContentType);
        Assert.Equal(PdfBytes, pdfSave.Bytes);
        Assert.Equal([pdfSave.Key], storage.DeletedKeys);

        IActionResult replaced = await controller.ReplaceCertificateFile(
            Guid.NewGuid(),
            new ReplaceCertificateFileRequest(null),
            FormFile(PngBytes, "image/png", "scan.png"),
            CancellationToken.None);

        Assert.IsType<ObjectResult>(replaced);
        Assert.IsType<ReplaceCertificateFileCommand>(sender.LastRequest);
        Assert.Equal(2, storage.PrivateSaves.Count);
        Assert.Equal("image/png", storage.PrivateSaves[1].ContentType);
        Assert.Equal([pdfSave.Key, storage.PrivateSaves[1].Key], storage.DeletedKeys);
    }

    [Theory]
    [InlineData("image/jpeg", "photo.jpg")]
    [InlineData("image/png", "photo.png")]
    [InlineData("image/webp", "photo.webp")]
    [InlineData("application/pdf", "photo.pdf")]
    [InlineData("image/svg+xml", "photo.svg")]
    public async Task Dependent_photo_upload_refuses_a_declared_type_without_a_matching_signature(
        string contentType, string fileName)
    {
        var storage = new RecordingPrivateStorage();
        var sender = new RejectingSender();
        FamilyController controller = CreateFamilyController(sender, storage);

        IActionResult result = await controller.SetDependentPhoto(
            Guid.NewGuid(),
            FormFile(SvgBytes, contentType, fileName),
            CancellationToken.None);

        AssertProblem(result, 400, "Storage.File.UnsupportedType", "dependent-photo-trace");
        Assert.Equal(0, storage.SaveCount);
        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public async Task Dependent_creation_refuses_a_mismatched_photo()
    {
        var storage = new RecordingPrivateStorage();
        var sender = new RejectingSender();
        FamilyController controller = CreateFamilyController(sender, storage);

        IActionResult result = await controller.AddDependent(
            DependentRequest(),
            FormFile(SvgBytes, "image/png", "photo.png"),
            CancellationToken.None);

        AssertProblem(result, 400, "Storage.File.UnsupportedType", "dependent-photo-trace");
        Assert.Equal(0, storage.SaveCount);
        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public async Task Dependent_photo_upload_accepts_a_real_image_and_cleans_up_on_failure()
    {
        var storage = new RecordingPrivateStorage();
        var sender = new RejectingSender();
        FamilyController controller = CreateFamilyController(sender, storage);

        IActionResult result = await controller.SetDependentPhoto(
            Guid.NewGuid(),
            FormFile(JpegBytes, "image/jpeg", "photo.jpg"),
            CancellationToken.None);

        Assert.IsType<ObjectResult>(result);
        Assert.IsType<SetDependentPhotoCommand>(sender.LastRequest);
        PrivateSave save = Assert.Single(storage.PrivateSaves);
        Assert.Equal("elderly-photos", save.Folder);
        Assert.Equal("image/jpeg", save.ContentType);
        Assert.Equal(JpegBytes, save.Bytes);
        Assert.Equal([save.Key], storage.DeletedKeys);
    }

    // ---- helpers ---------------------------------------------------------------

    private static CreateSplashScreenRequest SplashRequest() => new(
        "launch", "عنوان", "Title", "وصف", "Description", "ابدأ", "Start", "#FFFFFF", 1);

    private static CreateAssessmentTierRequest TierRequest() => new(
        1, "عنوان", "Title", "فرعي", "Subtitle", "#FFFFFF", "التالي", "Next", 0, 10,
        ["توصية"], ["recommendation"], true);

    private static CreateServiceRequest ServiceRequest() => new(
        "خدمة", "Service", CaregiverType.Medical, true);

    private static CreateLandingSectionRequest LandingCreateRequest(string? imagePath) => new(
        LandingSectionType, "عنوان", "Title", "نص", "Body", null, imagePath);

    private static IFormFile FormFile(byte[] bytes, string contentType, string fileName)
    {
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName);
        file.Headers = new HeaderDictionary { ["Content-Type"] = contentType };
        return file;
    }

    private static IFormFile OversizeFile(byte[] bytes, string contentType, string fileName)
    {
        var file = new FormFile(new MemoryStream(bytes), 0, 2_097_153, "file", fileName);
        file.Headers = new HeaderDictionary { ["Content-Type"] = contentType };
        return file;
    }

    /// <summary>
    /// Declares one byte past the 5 MB private-storage ceiling without holding
    /// that many bytes, so the size guard is exercised before any buffering.
    /// </summary>
    private static IFormFile OversizePrivateFile(byte[] bytes, string contentType, string fileName)
    {
        var file = new FormFile(new MemoryStream(bytes), 0, 5_242_881, "file", fileName);
        file.Headers = new HeaderDictionary { ["Content-Type"] = contentType };
        return file;
    }

    private static byte[] BytesFor(string contentType) => contentType switch
    {
        "image/jpeg" => JpegBytes,
        "image/png" => PngBytes,
        "image/webp" => WebpBytes,
        "application/pdf" => PdfBytes,
        _ => SvgBytes
    };

    private static FamilyController CreateFamilyController(ISender sender, IFileStorage storage) =>
        new(sender, storage)
        {
            ControllerContext = ClaimsContextFor("dependent-photo-trace", UserId.New())
        };

    private static AddDependentRequest DependentRequest() => new(
        "اسم", "Dependent", "01000000001", Gender.Male, FamilyRelationshipType.Son,
        new DateOnly(1950, 1, 1), null, null);

    private static ControllerContext ControllerContextFor(string traceId) =>
        new() { HttpContext = new DefaultHttpContext { TraceIdentifier = traceId } };

    private static ControllerContext ClaimsContextFor(string traceId, UserId subject)
    {
        var httpContext = new DefaultHttpContext { TraceIdentifier = traceId };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, subject.Value.ToString("D"))], "test"));
        return new ControllerContext { HttpContext = httpContext };
    }

    private static AdminLandingController CreateLandingController(
        ISender sender, TempStorageRoot root) =>
        new(sender, new RecordingStorage(), Options.Create(new LocalStorageOptions { RootPath = root.Path }))
        {
            ControllerContext = ControllerContextFor("landing-trace")
        };

    private static void AssertProblem(
        IActionResult result, int status, string code, string traceId, bool checkTrace = true)
    {
        var response = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(status, response.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal(status, problem.Status);
        Assert.Equal(code, problem.Extensions["code"]);
        if (checkTrace)
            Assert.Equal(traceId, problem.Extensions["traceId"]);
        else
            Assert.True(problem.Extensions.ContainsKey("traceId"));
    }

    private static CareHomesDbContext CreateCareHomesDb()
    {
        var db = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static async Task<(CareHomeFacility Facility, UserId Owner)> AddApprovedFacilityAsync(
        CareHomesDbContext db)
    {
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, Now);
        facility.SaveDraft(owner, facility.Version, CareHomeFacilityTests.Draft(), Now);
        foreach (CareHomeDocumentType documentType in Enum.GetValues<CareHomeDocumentType>())
        {
            facility.UploadDocument(
                owner, documentType, $"private/{documentType}.pdf", "application/pdf", 10, null, Now);
        }

        facility.Submit(owner, facility.Version, Now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
            facility.VerifyDocument(admin, facility.Version, document.Id, null, true, Now);
        facility.Review(admin, facility.Version, CareHomeReviewAction.Approved, null, Now, new DateOnly(2026, 10, 3));
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        return (facility, owner);
    }

    private sealed class TempStorageRoot : IDisposable
    {
        private TempStorageRoot(string path) => Path = path;

        public string Path { get; }

        public static TempStorageRoot Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "sanad-media-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempStorageRoot(path);
        }

        public void Write(string key, byte[] bytes)
        {
            string fullPath = System.IO.Path.Combine(
                Path, key.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            System.IO.File.WriteAllBytes(fullPath, bytes);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTime UtcNow => Now;
    }

    /// <summary>
    /// Rejects every dispatched request while recording it, so an upload that
    /// reaches dispatch proves the bytes were accepted without needing the
    /// downstream response shape. A non-generic <see cref="Result"/> command
    /// (avatar, for example) is failed the same way instead of throwing.
    /// </summary>
    private sealed class RejectingSender : ISender
    {
        private static readonly Error Rejected = new("Test.Rejected", "rejected by the test double");

        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;

            if (typeof(TResponse) == typeof(Result))
                return Task.FromResult((TResponse)(object)Result.Failure(Rejected));

            Type valueType = typeof(TResponse).GetGenericArguments()[0];
            MethodInfo failure = typeof(Result<>)
                .MakeGenericType(valueType)
                .GetMethod(nameof(Result<object>.Failure), [typeof(Error)])!;
            return Task.FromResult((TResponse)failure.Invoke(null, [Rejected])!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(
            object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class PhotoSender : ISender
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object response = request switch
            {
                GetMedicalReportPhotoQuery query =>
                    (object)Result<MedicalReportPhotoResult>.Success(new("private/reports/photo.jpg")),
                _ => throw new NotSupportedException(request.GetType().Name)
            };
            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(
            object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingStorage : IFileStorage
    {
        private int _counter;

        public int SaveCount { get; private set; }
        public List<string> SavedKeys { get; } = [];
        public List<string> DeletedKeys { get; } = [];

        public Task<Result<StoredFile>> SaveAsync(
            Stream content, string contentType, long contentLength, string folder,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            string key = $"{folder}/test-{++_counter}.png";
            SavedKeys.Add(key);
            return Task.FromResult<Result<StoredFile>>(new StoredFile(key));
        }

        public Task<Result<StoredFile>> SavePrivateAsync(
            Stream content, string contentType, long contentLength, string folder,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<StoredFile>> SavePrivateAsync(
            Stream content, string contentType, long contentLength, string folder, long maximumBytes,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<PrivateFileContent>> OpenReadAsync(
            string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            DeletedKeys.Add(key);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class FixedStorage(byte[] bytes, string contentType) : IFileStorage
    {
        public Task<Result<StoredFile>> SaveAsync(
            Stream content, string contentType, long contentLength, string folder,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<StoredFile>> SavePrivateAsync(
            Stream content, string contentType, long contentLength, string folder,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<StoredFile>> SavePrivateAsync(
            Stream content, string contentType, long contentLength, string folder, long maximumBytes,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<PrivateFileContent>> OpenReadAsync(
            string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<Result<PrivateFileContent>>(
                new PrivateFileContent(key, contentType, new MemoryStream(bytes)));

        public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Records every private write so a test can prove that only bytes carrying
    /// the matching signature reach private storage, that the rounded content
    /// type and folder are the documented ones, and that a rejected upload
    /// performs no write at all.
    /// </summary>
    private sealed class RecordingPrivateStorage : IFileStorage
    {
        private int _counter;

        public int SaveCount { get; private set; }
        public List<PrivateSave> PrivateSaves { get; } = [];
        public List<string> DeletedKeys { get; } = [];

        public Task<Result<StoredFile>> SaveAsync(
            Stream content, string contentType, long contentLength, string folder,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<Result<StoredFile>> SavePrivateAsync(
            Stream content, string contentType, long contentLength, string folder,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            string key = $"private/{folder}/test-{++_counter}.bin";
            PrivateSaves.Add(
                new PrivateSave(key, contentType, contentLength, folder, buffer.ToArray()));
            return new StoredFile(key);
        }

        public Task<Result<StoredFile>> SavePrivateAsync(
            Stream content, string contentType, long contentLength, string folder, long maximumBytes,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<PrivateFileContent>> OpenReadAsync(
            string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            DeletedKeys.Add(key);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed record PrivateSave(
        string Key, string ContentType, long ContentLength, string Folder, byte[] Bytes);
}
