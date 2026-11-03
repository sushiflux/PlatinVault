using Companion.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace Companion.Api.Hubs;

/// <summary>
/// SignalR hub for realtime < 400ms Steam game switches, trophy popups, and 100% Platinum celebrations. It only counts its
/// connections (for the log, see <see cref="LiveAnnouncer"/>); the events are sent through <see cref="IHubContext{THub}"/>.
/// </summary>
public sealed class AchievementHub(LiveConnectionCounter connections) : Hub
{
    public override Task OnConnectedAsync()
    {
        connections.Connected();
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Disconnected();
        return base.OnDisconnectedAsync(exception);
    }
}
