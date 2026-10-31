using System.Threading;
using System.Threading.Tasks;

namespace Ahlena.Modules.Identity.Application.Abstractions.Messaging;

public interface ISupportReplyNotifier
{
    Task TryNotifyAsync(
        Guid recipientUserId,
        Guid ticketId,
        string body,
        CancellationToken cancellationToken);
}