using Microsoft.AspNetCore.SignalR;
using MomentApp.Models;
using MomentApp.Services;

namespace MomentApp.Hubs;

/// <summary>
/// The single SignalR hub for a room: presence, chat, voting and WebRTC signalling.
/// </summary>
/// <remarks>
/// Chat used to live in a separate ChatHub on its own connection. Merging them removes two
/// bugs that only existed because of the split: system messages raised here were never shown
/// live (the client only listened for ReceiveMessage on the chat connection), and a chat
/// message could arrive before the room state it belonged after, rendering above its own
/// history. It also halves the WebSocket count per client.
/// </remarks>
public class RoomHub : MomentHub
{
    private readonly IMessageService _messageService;
    private readonly IVotingService _votingService;
    private readonly MessageRateLimiter _rateLimiter;
    private readonly ILogger<RoomHub> _logger;
    private const int MaxVoiceParticipants = 10;

    public RoomHub(
        IRoomService roomService,
        IMessageService messageService,
        IVotingService votingService,
        MessageRateLimiter rateLimiter,
        ILogger<RoomHub> logger)
        : base(roomService)
    {
        _messageService = messageService;
        _votingService = votingService;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    /// <summary>
    /// Join a room group
    /// </summary>
    public async Task JoinRoom(string roomId)
    {
        try
        {
            if (!TryResolveCaller(roomId, out var room, out var participant))
            {
                await Clients.Caller.SendAsync("Error", "You are not a participant in this room");
                return;
            }

            // Distinguishes a genuine arrival from a refresh or reconnect, so the room
            // doesn't announce the same person repeatedly.
            var isFirstConnection = participant.IsFirstConnection;

            participant.ConnectionId = Context.ConnectionId;
            participant.Status = ParticipantStatus.Online;
            participant.LastActivity = DateTime.UtcNow;
            participant.IsFirstConnection = false;
            participant.DisconnectedAt = null;

            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);

            var dto = ParticipantDto.From(participant);

            if (isFirstConnection)
            {
                var systemMessage = _messageService.CreateSystemMessage($"{participant.DisplayName} joined the room");
                _messageService.AddMessage(roomId, systemMessage);
                await Clients.Group(roomId).SendAsync("UserJoined", dto);
                await Clients.Group(roomId).SendAsync("ReceiveMessage", systemMessage);
            }
            else
            {
                await Clients.OthersInGroup(roomId).SendAsync("UserJoined", dto);
            }

            await Clients.Caller.SendAsync("RoomState", new
            {
                participants = room.Participants.Where(p => !p.HasLeft).Select(ParticipantDto.From).ToList(),
                messages = room.Messages,
                voteStatus = _votingService.GetVoteStatus(roomId),
                voiceParticipants = room.Participants
                    .Where(p => !p.HasLeft && p.IsInVoice)
                    .Select(p => new { p.Id, p.DisplayName })
                    .ToList(),
                videoParticipants = room.Participants
                    .Where(p => !p.HasLeft && p.IsInVideo)
                    .Select(p => new { p.Id, p.DisplayName })
                    .ToList()
            });

            _logger.LogInformation("Participant {ParticipantId} joined room {RoomId}", participant.Id, roomId);
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
            if (!TryResolveCaller(roomId, out _, out var participant))
            {
                return;
            }

            await ClearCallStateAsync(roomId, participant);

            RoomService.RemoveParticipant(roomId, participant.Id);
            _votingService.RecalculateVote(roomId);

            var systemMessage = _messageService.CreateSystemMessage($"{participant.DisplayName} left the room");
            _messageService.AddMessage(roomId, systemMessage);

            // Both arguments matter: the client's handler takes (id, name) and previously
            // only received the id, rendering "undefined left the room".
            await Clients.OthersInGroup(roomId).SendAsync("UserLeft", participant.Id, participant.DisplayName);
            await Clients.Group(roomId).SendAsync("ReceiveMessage", systemMessage);
            await Clients.Group(roomId).SendAsync("VoteUpdated", _votingService.GetVoteStatus(roomId));

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);

            _logger.LogInformation("Participant {ParticipantId} left room {RoomId}", participant.Id, roomId);
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
            if (!TryResolveCaller(roomId, out _, out var participant))
            {
                await Clients.Caller.SendAsync("Error", "You are not a participant in this room");
                return;
            }

            if (!_votingService.InitiateVote(roomId, participant.Id))
            {
                await Clients.Caller.SendAsync("Error", "A vote is already in progress");
                return;
            }

            var systemMessage = _messageService.CreateSystemMessage($"{participant.DisplayName} started a vote to close this room");
            _messageService.AddMessage(roomId, systemMessage);

            await Clients.Group(roomId).SendAsync("VoteStarted", participant.DisplayName);
            await Clients.Group(roomId).SendAsync("ReceiveMessage", systemMessage);
            await Clients.Group(roomId).SendAsync("VoteUpdated", _votingService.GetVoteStatus(roomId));

            _logger.LogInformation("Vote initiated in room {RoomId}", roomId);
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
            if (!TryResolveCaller(roomId, out _, out var participant))
            {
                await Clients.Caller.SendAsync("Error", "You are not a participant in this room");
                return;
            }

            if (!_votingService.CastVote(roomId, participant.Id, voteYes))
            {
                await Clients.Caller.SendAsync("Error", "Failed to cast vote");
                return;
            }

            var voteStatus = _votingService.GetVoteStatus(roomId);
            await Clients.Group(roomId).SendAsync("VoteUpdated", voteStatus);

            if (voteStatus?.HasPassed == true)
            {
                var systemMessage = _messageService.CreateSystemMessage("Vote passed! This room will close in 5 minutes.");
                _messageService.AddMessage(roomId, systemMessage);
                await Clients.Group(roomId).SendAsync("VotePassed");
                await Clients.Group(roomId).SendAsync("ReceiveMessage", systemMessage);
                _logger.LogInformation("Vote passed in room {RoomId}", roomId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error casting vote");
            await Clients.Caller.SendAsync("Error", "Failed to cast vote");
        }
    }

    /// <summary>
    /// Send a message to all participants in the room
    /// </summary>
    public async Task SendMessage(string roomId, string content)
    {
        try
        {
            if (!TryResolveCaller(roomId, out _, out var participant))
            {
                await Clients.Caller.SendAsync("Error", "You are not a participant in this room");
                return;
            }

            if (!_rateLimiter.TryAcquire(roomId, participant.Id))
            {
                await Clients.Caller.SendAsync("Error", "You're sending messages too quickly. Please slow down.");
                return;
            }

            if (!_messageService.ValidateMessage(content))
            {
                await Clients.Caller.SendAsync("Error", "Invalid message content");
                return;
            }

            var message = new Message
            {
                SenderId = participant.Id,
                SenderName = participant.DisplayName,
                SenderColor = participant.ColorHex,
                Content = content,
                Type = MessageType.User
            };

            if (_messageService.AddMessage(roomId, message))
            {
                RoomService.UpdateParticipantActivity(roomId, participant.Id);
                await Clients.Group(roomId).SendAsync("ReceiveMessage", message);
            }
            else
            {
                await Clients.Caller.SendAsync("Error", "Failed to send message");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message");
            await Clients.Caller.SendAsync("Error", "An error occurred while sending your message");
        }
    }

    /// <summary>
    /// Notify room that user is typing
    /// </summary>
    public async Task StartTyping(string roomId)
    {
        if (TryResolveCaller(roomId, out _, out var participant))
        {
            await Clients.OthersInGroup(roomId).SendAsync("UserTyping", participant.DisplayName);
        }
    }

    /// <summary>
    /// Notify room that user stopped typing
    /// </summary>
    public async Task StopTyping(string roomId)
    {
        if (TryResolveCaller(roomId, out _, out var participant))
        {
            await Clients.OthersInGroup(roomId).SendAsync("UserStoppedTyping", participant.DisplayName);
        }
    }

    /// <summary>
    /// Update participant activity (heartbeat)
    /// </summary>
    public Task UpdateActivity(string roomId)
    {
        if (TryResolveCaller(roomId, out _, out var participant))
        {
            RoomService.UpdateParticipantActivity(roomId, participant.Id);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Join the room voice call
    /// </summary>
    public Task JoinVoice(string roomId) => JoinCallAsync(roomId, withVideo: false);

    /// <summary>
    /// Join the room video call (audio + video)
    /// </summary>
    public Task JoinVideo(string roomId) => JoinCallAsync(roomId, withVideo: true);

    private async Task JoinCallAsync(string roomId, bool withVideo)
    {
        try
        {
            if (!TryResolveCaller(roomId, out var room, out var participant))
            {
                await Clients.Caller.SendAsync("VoiceError", "You are not a participant in this room");
                return;
            }

            if (participant.IsInVoice && participant.IsInVideo == withVideo)
            {
                return;
            }

            var alreadyInCall = participant.IsInVoice;
            if (!alreadyInCall)
            {
                var activeVoiceCount = room.Participants.Count(p => !p.HasLeft && p.IsInVoice);
                if (activeVoiceCount >= MaxVoiceParticipants)
                {
                    await Clients.Caller.SendAsync("VoiceError", $"Voice call is full (max {MaxVoiceParticipants})");
                    return;
                }
            }

            participant.IsInVoice = true;
            participant.IsInVideo = withVideo;

            await Clients.Caller.SendAsync("VoiceJoined", new
            {
                participants = room.Participants
                    .Where(p => !p.HasLeft && p.IsInVoice && p.Id != participant.Id)
                    .Select(p => new { p.Id, p.DisplayName })
                    .ToList(),
                videoParticipants = room.Participants
                    .Where(p => !p.HasLeft && p.IsInVideo)
                    .Select(p => new { p.Id, p.DisplayName })
                    .ToList(),
                maxParticipants = MaxVoiceParticipants,
                isVideo = withVideo
            });

            if (!alreadyInCall)
            {
                await Clients.OthersInGroup(roomId).SendAsync("VoiceParticipantJoined", new
                {
                    id = participant.Id,
                    displayName = participant.DisplayName
                });
            }

            if (withVideo)
            {
                await Clients.Group(roomId).SendAsync("VideoParticipantJoined", new
                {
                    id = participant.Id,
                    displayName = participant.DisplayName
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining call");
            await Clients.Caller.SendAsync("VoiceError", "Failed to join the call");
        }
    }

    /// <summary>
    /// Leave the room voice call
    /// </summary>
    public async Task LeaveVoice(string roomId)
    {
        try
        {
            if (TryResolveCaller(roomId, out _, out var participant))
            {
                await ClearCallStateAsync(roomId, participant);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving voice");
            await Clients.Caller.SendAsync("VoiceError", "Failed to leave voice call");
        }
    }

    /// <summary>
    /// The other participants currently in the call, from one caller's point of view.
    /// </summary>
    private static List<CallPeerDto> BuildRoster(Room room, Participant caller) =>
        room.Participants
            .Where(p => !p.HasLeft && p.IsInVoice && p.Id != caller.Id)
            .Select(p => new CallPeerDto(
                p.Id,
                p.DisplayName,
                p.IsInVideo,
                p.IsMuted,
                // Exactly one side opens the connection. Ordinal comparison is stable and
                // gives opposite answers on the two ends without any negotiation.
                ShouldOffer: string.CompareOrdinal(caller.Id, p.Id) < 0))
            .ToList();

    /// <summary>
    /// Re-establishes call membership after a SignalR reconnect.
    /// </summary>
    /// <remarks>
    /// Returns the roster as a method result rather than raising an event, so the caller
    /// cannot observe peer-joined notifications before the roster they belong to.
    /// </remarks>
    public async Task<List<CallPeerDto>> RejoinCall(string roomId, bool isVideo, bool isMuted)
    {
        if (!TryResolveCaller(roomId, out var room, out var participant))
        {
            return new List<CallPeerDto>();
        }

        var wasInCall = participant.IsInVoice;
        participant.IsInVoice = true;
        participant.IsInVideo = isVideo;
        participant.IsMuted = isMuted;
        participant.DisconnectedAt = null;

        // If the grace period lapsed and the room already announced their departure, the room
        // needs telling they are back.
        if (!wasInCall)
        {
            await Clients.OthersInGroup(roomId).SendAsync("VoiceParticipantJoined", new
            {
                id = participant.Id,
                displayName = participant.DisplayName
            });
        }

        await Clients.OthersInGroup(roomId).SendAsync("MediaStateChanged",
            participant.Id, new MediaStateDto(participant.IsInVideo, participant.IsMuted));

        return BuildRoster(room, participant);
    }

    /// <summary>
    /// Broadcasts the caller's microphone state.
    /// </summary>
    public async Task SetMuted(string roomId, bool muted)
    {
        if (!TryResolveCaller(roomId, out _, out var participant) || participant.IsMuted == muted)
        {
            return;
        }

        participant.IsMuted = muted;
        await Clients.Group(roomId).SendAsync("MediaStateChanged",
            participant.Id, new MediaStateDto(participant.IsInVideo, muted));
    }

    /// <summary>
    /// Relay WebRTC signaling data between participants
    /// </summary>
    public async Task SendVoiceSignal(string roomId, string toParticipantId, string signalType, string signalData)
    {
        try
        {
            if (!TryResolveCaller(roomId, out var room, out var sender) || !sender.IsInVoice)
            {
                return;
            }

            var target = room.FindParticipant(toParticipantId);
            if (target == null || target.HasLeft || !target.IsInVoice || string.IsNullOrEmpty(target.ConnectionId))
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

    /// <summary>
    /// Toggle video stream while staying in the call
    /// </summary>
    public async Task SetVideoEnabled(string roomId, bool enabled)
    {
        try
        {
            if (!TryResolveCaller(roomId, out _, out var participant))
            {
                return;
            }

            if (!participant.IsInVoice)
            {
                await Clients.Caller.SendAsync("VoiceError", "Join the call before enabling video");
                return;
            }

            if (participant.IsInVideo == enabled)
            {
                return;
            }

            participant.IsInVideo = enabled;

            if (enabled)
            {
                await Clients.Group(roomId).SendAsync("VideoParticipantJoined", new
                {
                    id = participant.Id,
                    displayName = participant.DisplayName
                });
            }
            else
            {
                await Clients.Group(roomId).SendAsync("VideoParticipantLeft", participant.Id, participant.DisplayName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling video");
            await Clients.Caller.SendAsync("VoiceError", "Failed to toggle video");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var room in RoomService.GetAllRooms())
        {
            var participant = room.Participants.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
            if (participant == null || participant.HasLeft)
            {
                continue;
            }

            // Call membership is deliberately NOT cleared here. SignalR reconnects routinely
            // on a network blip, and peer-to-peer media keeps flowing throughout — announcing
            // a departure now would make every peer tear down a connection that is still
            // working. TimerService evicts them if they fail to come back.
            participant.DisconnectedAt = DateTime.UtcNow;

            RoomService.UpdateParticipantStatus(room.Id, participant.Id, ParticipantStatus.Offline);
            await Clients.OthersInGroup(room.Id).SendAsync("ParticipantStatusChanged", participant.Id, ParticipantStatus.Offline);

            // An unreachable participant must not hold a vote hostage: they are no longer
            // counted in the denominator, which can be enough to carry it.
            _votingService.RecalculateVote(room.Id);
            await Clients.Group(room.Id).SendAsync("VoteUpdated", _votingService.GetVoteStatus(room.Id));

            _logger.LogInformation("Participant {ParticipantId} disconnected from room {RoomId}", participant.Id, room.Id);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Drops a participant out of the call and tells the room, if they were in one.
    /// </summary>
    private async Task ClearCallStateAsync(string roomId, Participant participant)
    {
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
}
