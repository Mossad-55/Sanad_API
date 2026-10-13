namespace Sanad.API.ChatPlus;

/// <summary>Chat Plus deployment topology (section `ChatPlus:Deployment`).
/// The initial supported topology is one API instance: connection/presence
/// registries are in-memory behind seams. Multi-instance without a verified
/// backplane is unsupported (realtime splits although REST stays safe) and
/// fails startup validation when declared.
/// </summary>
public sealed record ChatPlusDeploymentOptions
{
    public const string SectionName = "ChatPlus:Deployment";

    public int InstanceCount { get; init; } = 1;

    public bool BackplaneConfigured { get; init; } = false;
}
