using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.API.Controllers.Requests;

public sealed record AccountChoiceRequest(AccountType AccountType);
