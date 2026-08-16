namespace Sanad.Modules.Identity.Domain.Users;

public static class UserErrors
{
    public const string FullNameRequired =
        "Full name is required.";

    public const string EmailRequired =
        "Email is required.";

    public const string InvalidEmail =
        "Invalid email address.";

    public const string PhoneNumberRequired =
        "Phone number is required.";

    public const string InvalidPhoneNumber =
        "Invalid phone number.";

    public const string UserAlreadyHasAccount =
        "User already has this account type.";
    
    public const string EmailNotSet =
        "User does not have an email address.";
}