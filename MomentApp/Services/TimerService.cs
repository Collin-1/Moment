using Microsoft.AspNetCore.SignalR;
using MomentApp.Hubs;
using MomentApp.Models;

namespace MomentApp.Services;

/// <summary>
/// Background service that manages room timers and expiry.
/// </summary>
/// <remarks>
/// A <see cref="BackgroundService"/> with a <see cref="PeriodicTimer"/> rather than a
/// <see cref="System.Threading.Timer"/> with an <c>async void</c> callback. The old shape had
/// two problems: an exception escaping the callback was unobserved and took the process down,
/// and because the callback awaited a serial broadcast to every room, a tick slower than the
/// interval would re-enter itself. Awaiting the timer makes both impossible by construction.
/// </remarks>
public class TimerService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long a disconnected participant keeps their place in the call before their peers
    /// are told to tear down. Long enough to cover a Wi-Fi roam or a cellular handover.
    /// </summary>
    private static readonly TimeSpan CallReconnectGrace = TimeSpan.FromSeconds(30);

    private readonly ILogger<TimerService> _logger;
    private readonly IRoomService _roomService;
    private readonly IVotingService _votingService;
    private readonly IHubContext<RoomHub> _hubContext;

    public TimerService(
        ILogger<TimerService> logger,
        IRoomService roomService,
        IVotingService votingService,
        IHubContext<RoomHub> hubContext)
    {
        _logger = logger;
        _roomService = roomService;
        _votingService = votingService;
        _hubContext = hubContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Timer service is starting");

        using var timer = new PeriodicTimer(CheckInterval);

        try
        {
            do
            {
                try
                {
                    await CheckRoomsAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One bad room must not stop the ticking for every other room.
                    _logger.LogError(ex, "Error in timer service tick");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }

        _logger.LogInformation("Timer service is stopping");
    }

    private async Task CheckRoomsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        foreach (var room in _roomService.GetAllRooms().ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var timeRemaining = room.ExpiresAt - now;
            var totalSeconds = (int)timeRemaining.TotalSeconds;

            await _hubContext.Clients.Group(room.Id).SendAsync("TimerUpdate", totalSeconds, cancellationToken);

            if (!room.IsInGracePeriod)
            {
                if (timeRemaining.TotalMinutes is <= 5 and > 4.9)
                {
                    await _hubContext.Clients.Group(room.Id).SendAsync("ExpiryWarning", 5, cancellationToken);
                }

                if (timeRemaining.TotalMinutes is <= 1 and > 0.9)
                {
                    await _hubContext.Clients.Group(room.Id).SendAsync("ExpiryWarning", 1, cancellationToken);
                }
            }

            if (timeRemaining.TotalSeconds <= 0)
            {
                _logger.LogInformation("Room {RoomId} has expired", room.Id);
                await _hubContext.Clients.Group(room.Id).SendAsync("RoomClosed", cancellationToken);
                _roomService.DeleteRoom(room.Id);
                continue;
            }

            await ExpireLapsedVoteAsync(room, now, cancellationToken);
            await EvictAbandonedCallParticipantsAsync(room, now, cancellationToken);
            CheckInactiveParticipants(room, now);
        }
    }

    /// <summary>
    /// Drops participants out of the call once they have been gone longer than the reconnect
    /// grace period.
    /// </summary>
    /// <remarks>
    /// The hub deliberately leaves call membership intact when a connection drops, because a
    /// reconnect is usually seconds away and the media never stopped flowing. This is the
    /// other half of that bargain: someone who does not come back has to be cleared out, or
    /// their peers keep a dead connection and the participant count stays wrong.
    /// </remarks>
    private async Task EvictAbandonedCallParticipantsAsync(Room room, DateTime now, CancellationToken cancellationToken)
    {
        foreach (var participant in room.Participants)
        {
            if (participant.HasLeft || !participant.IsInVoice) continue;
            if (participant.DisconnectedAt is not { } droppedAt) continue;
            if (now - droppedAt <= CallReconnectGrace) continue;

            participant.IsInVoice = false;
            participant.IsInVideo = false;

            await _hubContext.Clients.Group(room.Id)
                .SendAsync("VoiceParticipantLeft", participant.Id, participant.DisplayName, cancellationToken);

            _logger.LogInformation(
                "Participant {ParticipantId} did not reconnect within the grace period and left the call in room {RoomId}",
                participant.Id, room.Id);
        }
    }

    /// <summary>
    /// Clears a vote nobody carried, so the room isn't blocked from ever voting again.
    /// </summary>
    private async Task ExpireLapsedVoteAsync(Room room, DateTime now, CancellationToken cancellationToken)
    {
        if (room.ActiveVote?.HasLapsed(now) != true)
        {
            return;
        }

        room.ActiveVote = null;
        await _hubContext.Clients.Group(room.Id).SendAsync("VoteUpdated", null, cancellationToken);
        _logger.LogInformation("Vote in room {RoomId} lapsed without passing", room.Id);
    }

    private void CheckInactiveParticipants(Room room, DateTime now)
    {
        foreach (var participant in room.Participants.Where(p => !p.HasLeft))
        {
            var inactiveDuration = now - participant.LastActivity;

            // Away after 5 minutes of silence.
            if (inactiveDuration.TotalMinutes >= 5 && participant.Status == ParticipantStatus.Online)
            {
                _roomService.UpdateParticipantStatus(room.Id, participant.Id, ParticipantStatus.Away);
            }

            // Gone after 10 minutes offline.
            if (inactiveDuration.TotalMinutes >= 10 && participant.Status == ParticipantStatus.Offline)
            {
                _roomService.RemoveParticipant(room.Id, participant.Id);
                _votingService.RecalculateVote(room.Id);
            }
        }
    }
}
