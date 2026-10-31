using Ahlena.Modules.Families.Domain.Elderlies;

namespace Ahlena.API.Options;

public sealed class ElderlyProfileOptions
{
    public const string SectionName = "ElderlyProfile";

    public string DefaultTimeZoneId { get; set; } = ElderlyTimeZone.InitialDefaultId;
}
