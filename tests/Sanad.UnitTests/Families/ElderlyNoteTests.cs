using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Activities;
using Sanad.Modules.Families.Domain.Notes;
using Xunit;

namespace Sanad.UnitTests.Families;

public sealed class ElderlyNoteTests
{
    [Fact]
    public void CreateNote_WithValidParameters_SetsProperties()
    {
        var elderlyId = ElderlyId.New();
        var authorUserId = UserId.New();

        var note = ElderlyNote.Create(
            elderlyId,
            authorUserId,
            "انخفاض الشهية اليوم",
            "لاحظت انخفاضاً في تناول الطعام أثناء الغداء",
            NoteCategory.Nutrition,
            NotePriority.Medium);

        Assert.Equal("انخفاض الشهية اليوم", note.Title);
        Assert.Equal("لاحظت انخفاضاً في تناول الطعام أثناء الغداء", note.Description);
        Assert.Equal(NoteCategory.Nutrition, note.Category);
        Assert.Equal(NotePriority.Medium, note.Priority);
        Assert.Equal(elderlyId, note.ElderlyId);
        Assert.Equal(authorUserId, note.AuthorUserId);
    }

    [Fact]
    public void CreateNote_WithEmptyTitle_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() =>
            ElderlyNote.Create(
                ElderlyId.New(),
                UserId.New(),
                "",
                "وصف الملاحظة",
                NoteCategory.General,
                NotePriority.Low));
    }

    [Fact]
    public void CreateActivityLog_WithValidParameters_SetsSummaryAndTimestamp()
    {
        var log = ElderlyActivityLog.Create(
            ElderlyId.New(),
            UserId.New(),
            ElderlyActivityType.ViewMedicalProfile,
            "عرض الملف الطبي");

        Assert.Equal(ElderlyActivityType.ViewMedicalProfile, log.ActivityType);
        Assert.Equal("عرض الملف الطبي", log.Summary);
    }
}