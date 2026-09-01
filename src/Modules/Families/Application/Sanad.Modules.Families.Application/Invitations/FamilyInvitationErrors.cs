using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Families.Application.Invitations;

public static class FamilyInvitationErrors
{
    public static readonly Error FamilyNotFound =
        new("Families.Invitation.FamilyNotFound",
            "The family was not found.");

    public static readonly Error AccessDenied =
        new("Families.Invitation.AccessDenied",
            "Your family role does not permit this action.");

    public static readonly Error RecipientNotRegistered =
        new("Families.Invitation.RecipientNotRegistered",
            "The recipient must register a Sanad Care account first.");

    public static readonly Error RecipientMissingFamilyAccount =
        new("Families.Invitation.RecipientMissingFamilyAccount",
            "The recipient must have a family account to be invited.");

    public static readonly Error CannotInviteYourself =
        new("Families.Invitation.CannotInviteYourself",
            "You cannot invite yourself to your own family.");

    public static readonly Error AlreadyMember =
        new("Families.Invitation.AlreadyMember",
            "This user is already a member of the family.");

    public static readonly Error PendingInvitationExists =
        new("Families.Invitation.PendingInvitationExists",
            "A pending invitation already exists for this recipient.");

    public static readonly Error InvalidRole =
        new("Families.Invitation.InvalidRole",
            "Invited members can only be Editors or Viewers.");

    public static readonly Error InvalidToken =
        new("Families.Invitation.InvalidToken",
            "The invitation token is invalid.");

    public static readonly Error InvitationNotFound =
        new("Families.Invitation.NotFound",
            "The invitation was not found.");

    public static readonly Error NotPending =
        new("Families.Invitation.NotPending",
            "This invitation has already been answered or revoked.");

    public static readonly Error Expired =
        new("Families.Invitation.Expired",
            "This invitation has expired.");

    public static readonly Error NotInvitee =
        new("Families.Invitation.NotInvitee",
            "Only the invited user can respond to this invitation.");
}