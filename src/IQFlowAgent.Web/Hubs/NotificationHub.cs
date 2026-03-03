using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace IQFlowAgent.Web.Hubs;

/// <summary>
/// SignalR hub for real-time notifications (RAG job completion, analysis ready).
/// Clients subscribe to their own user group on connection.
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    // Clients call this to join their personal notification group
    public async Task JoinUserGroup()
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");
    }
}
