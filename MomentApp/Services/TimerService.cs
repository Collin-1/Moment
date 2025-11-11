using Microsoft.AspNetCore.SignalR;
using MomentApp.Hubs;

namespace MomentApp.Services;

/// <summary>
/// Background service that manages room timers and expiry
/// </summary>
public class TimerService : IHostedService, IDisposable
{
    private readonly ILogger<TimerService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<RoomHub> _hubContext;
    private Timer? _timer;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(10);

    public TimerService(
        ILogger<TimerService> logger,
        IServiceProvider serviceProvider,
        IHubContext<RoomHub> hubContext)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Timer Service is starting");
        _timer = new Timer(CheckRooms, null, TimeSpan.Zero, _checkInterval);
        return Task.CompletedTask;
    }

    private async void CheckRooms(object? state)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var roomService = scope.ServiceProvider.GetRequiredService<IRoomService>();
            var rooms = roomService.GetAllRooms().ToList();

            foreach (var room in rooms)
            {
                var timeRemaining = room.ExpiresAt - DateTime.UtcNow;
                var totalSeconds = (int)timeRemaining.TotalSeconds;

                // Send timer updates every 10 seconds
                await _hubContext.Clients.Group(room.Id).SendAsync("TimerUpdate", totalSeconds);

                // 5-minute warning
                if (timeRemaining.TotalMinutes <= 5 && timeRemaining.TotalMinutes > 4.9 && !room.IsInGracePeriod)
                {
                    await _hubContext.Clients.Group(room.Id).SendAsync("ExpiryWarning", 5);
                }

                // 1-minute warning
                if (timeRemaining.TotalMinutes <= 1 && timeRemaining.TotalMinutes > 0.9 && !room.IsInGracePeriod)
                {
                    await _hubContext.Clients.Group(room.Id).SendAsync("ExpiryWarning", 1);
                }

                // Room expired
                if (timeRemaining.TotalSeconds <= 0)
                {
                    _logger.LogInformation($"Room {room.Id} has expired");
                    await _hubContext.Clients.Group(room.Id).SendAsync("RoomClosed");
                    roomService.DeleteRoom(room.Id);
                }

                // Check for inactive participants
                CheckInactiveParticipants(room, roomService);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in timer service");
        }
    }

    private void CheckInactiveParticipants(Models.Room room, IRoomService roomService)
    {
        var now = DateTime.UtcNow;

        foreach (var participant in room.Participants.Where(p => !p.HasLeft))
        {
            var inactiveDuration = now - participant.LastActivity;

            // Mark as away after 5 minutes of inactivity
            if (inactiveDuration.TotalMinutes >= 5 && participant.Status == Models.ParticipantStatus.Online)
            {
                roomService.UpdateParticipantStatus(room.Id, participant.Id, Models.ParticipantStatus.Away);
            }

            // Remove after 10 minutes offline
            if (inactiveDuration.TotalMinutes >= 10 && participant.Status == Models.ParticipantStatus.Offline)
            {
                roomService.RemoveParticipant(room.Id, participant.Id);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Timer Service is stopping");
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
