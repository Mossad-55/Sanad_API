namespace Sanad.Modules.Community.Application.Ratings;

public sealed record AddRatingCommand : ICommand<bool>
{
    public Guid PostId { get; init; }
    public Guid UserId { get; init; }
    public string RatingValue { get; init; } = string.Empty; // 1-5 stars
}
