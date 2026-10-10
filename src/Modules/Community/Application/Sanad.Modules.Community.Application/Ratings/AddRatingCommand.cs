namespace Sanad.Modules.Community.Application.Ratings;

public sealed record AddRatingCommand : ICommand<bool>
{
    public Guid PostId { get; init; }
    public Guid UserId { get; init; }
    public int Value { get; init; } // 1-5 stars, range-validated by the handler
}
