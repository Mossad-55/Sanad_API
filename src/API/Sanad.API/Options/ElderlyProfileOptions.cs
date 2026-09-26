using Sanad.Modules.Families.Domain.Elderlies;

namespace Sanad.API.Options;

public sealed class ElderlyProfileOptions
{
    public const string SectionName = "ElderlyProfile";

    public string DefaultTimeZoneId { get; set; } = ElderlyTimeZone.InitialDefaultId;
}
