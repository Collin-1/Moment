using Microsoft.AspNetCore.SignalR;
using MomentApp.Models;
using MomentApp.Services;

namespace MomentApp.Hubs;

/// <summary>
/// SignalR hub for room and participant management
/// </summary>
public class RoomHub : Hub
{
    private readonly IRoomService _roomService;
    private readonly IMessageService _messageService;
    private readonly IVotingService _votingService;
    private readonly ILogger<RoomHub> _logger;
    private const int MaxVoiceParticipants = 10;

    public RoomHub(
        IRoomService roomService,
        IMessageService messageService,
        IVotingService votingService,
        ILogger<RoomHub> logger)
    {
        _roomService = roomService;
        _messageService = messageService;
        _votingService = votingService;
        _logger = logger;
    }

    /// <summary>
    /// Join a room group
    /// </summary>
    public async Task JoinRoom(string roomId, string participantId)
    {
        try
        {
            var room = _roomService.GetRoom(roomId);
            if (room == null)
            {
                await Clients.Caller.SendAsync("Error", "Room not found");
                return;
            }

            var participant = room.Participants.FirstOrDefault(p => p.Id == participantId);
            if (participant == null)
            {
                await Clients.Caller.SendAsync("Error", "Participant not found");
                return;
            }

            // Check if this is a reconnection or first join
            bool isFirstConnection = participant.IsFirstConnection;

            // Update connection ID
            participant.ConnectionId = Context.ConnectionId;
            participant.Status = ParticipantStatus.Online;
            participant.LastActivity = DateTime.UtcNow;
            participant.IsFirstConnection = false; // Mark as connected

            // Add to SignalR group
            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);

            // Only send join message if this is the first connection
            if (isFirstConnection)
            {
                var systemMessage = _messageService.CreateSystemMessage($"{participant.DisplayName} joined the room");
                _messageService.AddMessage(roomId, systemMessage);
                await Clients.Group(roomId).SendAsync("UserJoined", participant);
                await Clients.Group(roomId).SendAsync("ReceiveMessage", systemMessage);
            }
            else
            {
                // Just notify about reconnection without system message
                await Clients.OthersInGroup(roomId).SendAsync("UserJoined", participant);
            }

            // Send current room state to the new participant
            await Clients.Caller.SendAsync("RoomState", new
            {
                participants = room.Participants.Where(p => !p.HasLeft).ToList(),
                messages = room.Messages,
                voteStatus = _votingService.GetVoteStatus(roomId),
                voiceParticipants = room.Participants
                    .Where(p => !p.HasLeft && p.IsInVoice)
                    .Select(p => new { p.Id, p.DisplayName }),
                videoParticipants = room.Participants
                    .Where(p => !p.HasLeft && p.IsInVideo)
                    .Select(p => new { p.Id, p.DisplayName })
            });

            _logger.LogInformation($"Participant {participant.DisplayName} joined room {roomId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining room");
            await Clients.Caller.SendAsync("Error", "Failed to join room");
        }
    }

    /// <summary>
    /// Leave a room permanently
    /// </summary>
    public async Task LeaveRoom(string roomId)
    {
        try
        {
            var participant = _roomService.GetParticipantByConnectionId(roomId, Context.ConnectionId);
            if (participant == null)
            {
                return;
            }

            if (participant.IsInVoice)
            {
                participant.IsInVoice = false;
                if (participant.IsInVideo)
                {
                    participant.IsInVideo = false;
                    await Clients.Group(roomId).SendAsync("VideoParticipantLeft", participant.Id, participant.DisplayName);
                }
                await Clients.Group(roomId).SendAsync("VoiceParticipantLeft", participant.Id, participant.DisplayName);
            }

            // Mark as left
            _roomService.RemoveParticipant(roomId, participant.Id);

            // Recalculate vote if active
            _votingService.RecalculateVote(roomId);

            // Notify others
            var systemMessage = _messageService.CreateSystemMessage($"{participant.DisplayName} left the room");
            _messageService.AddMessage(roomId, systemMessage);
            await Clients.OthersInGroup(roomId).SendAsync("UserLeft", participant.Id);
            await Clients.Group(roomId).SendAsync("ReceiveMessage", systemMessage);

            // Remove from group
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);

            _logger.LogInformation($"Participant {participant.DisplayName} left room {roomId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving room");
        }
    }

    /// <summary>
    /// Initiate a vote to close the room
    /// </summary>
    public async Task InitiateVote(string roomId)
    {
        try
        {
            var participant = _roomService.GetParticipantByConnectionId(roomId, Context.ConnectionId);
            if (participant == null)
            {
                await Clients.Caller.SendAsync("Error", "Participant not found");
                return;
            }

            if (_votingService.InitiateVote(roomId, participant.Id))
            {
                var systemMessage = _messageService.CreateSystemMessage($"{participant.DisplayName} started a vote to close this room");
                _messageService.AddMessage(roomId, systemMessage);

                await Clients.Group(roomId).SendAsync("VoteStarted", participant.DisplayName);
                await Clients.Group(roomId).SendAsync("ReceiveMessage", systemMessage);
                await Clients.Group(roomId).SendAsync("VoteUpdated", _votingService.GetVoteStatus(roomId));

                _logger.LogInformation($"Vote initiated in room {roomId} by {participant.DisplayName}");
            }
            else
            {
                await Clients.Caller.SendAsync("Error", "Failed to initiate vote");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating vote");
            await Clients.Caller.SendAsync("Error", "Failed to initiate vote");
        }
    }

    /// <summary>
    /// Cast a vote
    /// </summary>
    public async Task CastVote(string roomId, bool voteYes)
    {
        try
        {
            var participant = _roomService.GetParticipantByConnectionId(roomId, Context.ConnectionId);
            if (participant == null)
            {
                await Clients.Caller.SendAsync("Error", "Participant not found");
                return;
            }

            if (_votingService.CastVote(roomId, participant.Id, voteYes))
            {
                var voteStatus = _votingService.GetVoteStatus(roomId);
                await Clients.Group(roomId).SendAsync("VoteUpdated", voteStatus);

                if (voteStatus?.HasPassed == true)
                {
                    var systemMessage = _messageService.CreateSystemMessage("Vote passed! This room will close in 5 minutes.");
                    _messageService.AddMessage(roomId, systemMessage);
                    await Clients.Group(roomId).SendAsync("VotePassed");
                    await Clients.Group(roomId).SendAsync("ReceiveMessage", systemMessage);
                    _logger.LogInformation($"Vote passed in room {roomId}");
                }

                _logger.LogInformation($"Participant {participant.DisplayName} voted {(voteYes ? "yes" : "no")} in room {roomId}");
            }
            else
            {
                await Clients.Caller.SendAsync("Error", "Failed to cast vote");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error casting vote");
            await Clients.Caller.SendAsync("Error", "Failed to cast vote");
        }
    }

    /// <summary>
    /// Update participant activity (heartbeat)
    /// </summary>
    public async Task UpdateActivity(string roomId)
    {
        var participant = _roomService.GetParticipantByConnectionId(roomId, Context.ConnectionId);
        if (participant != null)
        {
            _roomService.UpdateParticipantActivity(roomId, participant.Id);
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// Join the room voice call
    /// </summary>
    public async Task JoinVoice(string roomId, string participantId)
    {
        try
        {
            var room = _roomService.GetRoom(roomId);
            if (room == null)
            {
                await Clients.Caller.SendAsync("VoiceError", "Room not found");
                return;
            }

            var participant = _roomService.GetParticipantByConnectionId(roomId, Context.ConnectionId);
            if (participant == null || participant.Id != participantId)
            {
                await Clients.Caller.SendAsync("VoiceError", "Participant not found");
                return;
            }

            if (participant.IsInVoice)
            {
                return;
            }

            var activeVoiceCount = room.Participants.Count(p => !p.HasLeft && p.IsInVoice);
            if (activeVoiceCount >= MaxVoiceParticipants)
            {
                await Clients.Caller.SendAsync("VoiceError", $"Voice call is full (max {MaxVoiceParticipants})");
                return;
            }

            participant.IsInVoice = true;
            participant.IsInVideo = false;

            var otherVoiceParticipants = room.Participants
                .Where(p => !p.HasLeft && p.IsInVoice && p.Id != participant.Id)
                .Select(p => new { p.Id, p.DisplayName })
                .ToList();

            var videoParticipants = room.Participants
                .Where(p => !p.HasLeft && p.IsInVideo)
                .Select(p => new { p.Id, p.DisplayName })
                .ToList();

            await Clients.Caller.SendAsync("VoiceJoined", new
            {
                participants = otherVoiceParticipants,
                videoParticipants,
                maxParticipants = MaxVoiceParticipants,
                isVideo = false
            });

            await Clients.OthersInGroup(roomId).SendAsync("VoiceParticipantJoined", new
            {
                id = participant.Id,
                displayName = participant.DisplayName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining voice");
            await Clients.Caller.SendAsync("VoiceError", "Failed to join voice call");
        }
    }

    /// <summary>
    /// Join the room video call (audio + video)
    /// </summary>
    public async Task JoinVideo(string roomId, string participantId)
    {
        try
        {
            var room = _roomService.GetRoom(roomId);
            if (room == null)
            {
                await Clients.Caller.SendAsync("VoiceError", "Room not found");
                return;
            }

            var participant = _roomService.GetParticipantByConnectionId(roomId, Context.ConnectionId);
            if (participant == null || participant.Id != participantId)
            {
                await Clients.Caller.SendAsync("VoiceError", "Participant not found");
                return;
            }

            if (participant.IsInVoice && participant.IsInVideo)
            {
                return;
            }

            var alreadyInVoice = participant.IsInVoice;
            var activeVoiceCount = room.Participants.Count(p => !p.HasLeft && p.IsInVoice);
            if (!alreadyInVoice && activeVoiceCount >= MaxVoiceParticipants)
            {
                await Clients.Caller.SendAsync("VoiceError", $"Voice call is full (max {MaxVoiceParticipants})");
                return;
            }

            participant.IsInVoice = true;
            participant.IsInVideo = true;

            var otherVoiceParticipants = room.Participants
                .Where(p => !p.HasLeft && p.IsInVoice && p.Id != participant.Id)
                .Select(p => new { p.Id, p.DisplayName })
                .ToList();

            var videoParticipants = room.Participants
                .Where(p => !p.HasLeft && p.IsInVideo)
                .Select(p => new { p.Id, p.DisplayName })
                .ToList();

            await Clients.Caller.SendAsync("VoiceJoined", new
            {
                participants = otherVoiceParticipants,
                videoParticipants,
                maxParticipants = MaxVoiceParticipants,
                isVideo = true
            });

            if (!alreadyInVoice)
            {
                await Clients.OthersInGroup(roomId).SendAsync("VoiceParticipantJoined", new
                {
                    id = participant.Id,
                    displayName = participant.DisplayName
                });
            }

            await Clients.Group(roomId).SendAsync("VideoParticipantJoined", new
            {
                id = participant.Id,
                displayName = participant.DisplayName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining video");
            await Clients.Caller.SendAsync("VoiceError", "Failed to join video call");
        }
    }

    /// <summary>
    /// Leave the room voice call
    /// </summary>
    public async Task LeaveVoice(string roomId, string participantId)
    {
        try
        {
            var participant = _roomService.GetParticipantByConnectionId(roomId, Context.ConnectionId);
            if (participant == null || participant.Id != participantId)
            {
                return;
            }

            if (!participant.IsInVoice)
            {
                return;
            }

            participant.IsInVoice = false;
            if (participant.IsInVideo)
            {
                participant.IsInVideo = false;
                await Clients.Group(roomId).SendAsync("VideoParticipantLeft", participant.Id, participant.DisplayName);
            }
            await Clients.Group(roomId).SendAsync("VoiceParticipantLeft", participant.Id, participant.DisplayName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving voice");
            await Clients.Caller.SendAsync("VoiceError", "Failed to leave voice call");
        }
    }

    /// <summary>
    /// Relay WebRTC signaling data between participants
    /// </summary>
    public async Task SendVoiceSignal(string roomId, string fromParticipantId, string toParticipantId, string signalType, string signalData)
    {
        try
        {
            var sender = _roomService.GetParticipantByConnectionId(roomId, Context.ConnectionId);
            if (sender == null || sender.Id != fromParticipantId || !sender.IsInVoice)
            {
                return;
            }

            var room = _roomService.GetRoom(roomId);
            var target = room?.Participants.FirstOrDefault(p => p.Id == toParticipantId && !p.HasLeft && p.IsInVoice);
            if (target == null || string.IsNullOrEmpty(target.ConnectionId))
            {
                return;
            }

            await Clients.Client(target.ConnectionId).SendAsync("VoiceSignal", new
            {
                fromParticipantId = sender.Id,
                type = signalType,
                data = signalData
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending voice signal");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Find all rooms this connection is in and mark participant as offline
        var allRooms = _roomService.GetAllRooms();
        foreach (var room in allRooms)
        {
            var participant = room.Participants.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
            if (participant != null && !participant.HasLeft)
            {
                if (participant.IsInVoice)
                {
                    participant.IsInVoice = false;
                    if (participant.IsInVideo)
                    {
                        participant.IsInVideo = false;
                        await Clients.Group(room.Id).SendAsync("VideoParticipantLeft", participant.Id, participant.DisplayName);
                    }
                    await Clients.Group(room.Id).SendAsync("VoiceParticipantLeft", participant.Id, participant.DisplayName);
                }

                _roomService.UpdateParticipantStatus(room.Id, participant.Id, ParticipantStatus.Offline);
                await Clients.OthersInGroup(room.Id).SendAsync("ParticipantStatusChanged", participant.Id, ParticipantStatus.Offline);

                _logger.LogInformation($"Participant {participant.DisplayName} disconnected from room {room.Id}");
            }
        }

        await base.OnDisconnectedAsync(exception);
    }
}
