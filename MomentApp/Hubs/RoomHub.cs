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
    private const int MaxCallParticipants = 10;

    /// <summary>
    /// How much history a joiner (or a reconnecting client) is sent.
    /// </summary>
    /// <remarks>
    /// Rooms can last three days, and the whole transcript used to go to every arrival and
    /// again on every reconnect. Nothing is deleted — this only bounds what crosses the wire,
    /// and the room still holds everything until it dissolves.
    /// </remarks>
    private const int HistoryLimit = 200;

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
            RoomService.BindConnection(Context.ConnectionId, roomId, participant.Id);

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
                messages = room.Messages.TakeLast(HistoryLimit).ToList(),
                // No separate call rosters: every ParticipantDto already carries IsInVoice,
                // IsInVideo and IsMuted, and two descriptions of one fact drift apart.
                voteStatus = _votingService.GetVoteStatus(roomId)
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
            RoomService.ReleaseConnection(Context.ConnectionId);

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
    /// Join the room's call, returning who is already in it.
    /// </summary>
    /// <remarks>
    /// The roster is the method's return value, not a follow-up event. As an event it raced
    /// the invocation that asked for it, so a "someone joined" notice could arrive before the
    /// list it was meant to amend — and the client had to hold both orderings in its head.
    ///
    /// One method covers joining with video, joining without, and turning the camera on after
    /// the fact, because all three are the same thing: a participant declaring their media
    /// state. Calling it while already in the call updates that state rather than rejoining.
    /// </remarks>
    public async Task<List<CallPeerDto>> JoinCall(string roomId, MediaStateDto state)
    {
        try
        {
            if (!TryResolveCaller(roomId, out var room, out var participant))
            {
                await Clients.Caller.SendAsync("CallError", "You are not a participant in this room");
                return new List<CallPeerDto>();
            }

            var alreadyInCall = participant.IsInVoice;

            if (!alreadyInCall)
            {
                var activeCount = room.Participants.Count(p => !p.HasLeft && p.IsInVoice);
                if (activeCount >= MaxCallParticipants)
                {
                    await Clients.Caller.SendAsync("CallError", $"This call is full (max {MaxCallParticipants})");
                    return new List<CallPeerDto>();
                }
            }

            participant.IsInVoice = true;
            participant.IsInVideo = state.IsVideoOn;
            participant.IsMuted = state.IsMuted;
            participant.DisconnectedAt = null;

            var roster = BuildRoster(room, participant);

            if (alreadyInCall)
            {
                await Clients.OthersInGroup(roomId).SendAsync("MediaStateChanged",
                    participant.Id, new MediaStateDto(participant.IsInVideo, participant.IsMuted));
            }
            else
            {
                // ShouldOffer is false for the room: whoever joined opens the connections, so
                // exactly one side of each new pair offers.
                await Clients.OthersInGroup(roomId).SendAsync("CallParticipantJoined",
                    new CallPeerDto(participant.Id, participant.DisplayName,
                        participant.IsInVideo, participant.IsMuted, ShouldOffer: false));
            }

            return roster;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining call");
            await Clients.Caller.SendAsync("CallError", "Failed to join the call");
            return new List<CallPeerDto>();
        }
    }

    /// <summary>
    /// Leave the call but stay in the room.
    /// </summary>
    public async Task LeaveCall(string roomId)
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
            _logger.LogError(ex, "Error leaving call");
            await Clients.Caller.SendAsync("CallError", "Failed to leave the call");
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
            await Clients.OthersInGroup(roomId).SendAsync("CallParticipantJoined",
                new CallPeerDto(participant.Id, participant.DisplayName,
                    participant.IsInVideo, participant.IsMuted, ShouldOffer: false));
        }

        await Clients.OthersInGroup(roomId).SendAsync("MediaStateChanged",
            participant.Id, new MediaStateDto(participant.IsInVideo, participant.IsMuted));

        return BuildRoster(room, participant);
    }

    /// <summary>
    /// Relays whether the caller is currently speaking.
    /// </summary>
    /// <remarks>
    /// Relay only: deliberately not stored on the participant and not included in room state.
    /// It goes stale the instant somebody drops mid-word, and a late joiner reading it from a
    /// snapshot would see a participant who appears to be speaking forever.
    ///
    /// The value is also deduplicated here. Clients send on transition, but a buggy or
    /// hostile one could send at frame rate, and this is the cheapest place to stop that
    /// reaching every other participant.
    /// </remarks>
    public async Task SetSpeaking(string roomId, bool speaking)
    {
        if (!TryResolveCaller(roomId, out _, out var participant) || !participant.IsInVoice)
        {
            return;
        }

        var key = $"speaking:{roomId}:{participant.Id}";
        if (Context.Items.TryGetValue(key, out var last) && last is bool previous && previous == speaking)
        {
            return;
        }

        Context.Items[key] = speaking;
        await Clients.OthersInGroup(roomId).SendAsync("SpeakingChanged", participant.Id, speaking);
    }

    /// <summary>
    /// Broadcasts the caller's microphone and camera state.
    /// </summary>
    /// <remarks>
    /// One method and one event for both flags. They used to travel separately — mute over
    /// MediaStateChanged, the camera over a joined/left pair — so two independent booleans
    /// could arrive out of order and leave a participant rendered as both sharing video and
    /// not sharing it. Sending the pair makes that state unrepresentable.
    ///
    /// Mute is broadcast rather than inferred: a receiver can see that an incoming track is
    /// muted, but that signal is unreliable across browsers and lags by seconds.
    /// </remarks>
    public async Task SetMediaState(string roomId, MediaStateDto state)
    {
        if (!TryResolveCaller(roomId, out _, out var participant) || !participant.IsInVoice)
        {
            return;
        }

        if (participant.IsInVideo == state.IsVideoOn && participant.IsMuted == state.IsMuted)
        {
            return;
        }

        participant.IsInVideo = state.IsVideoOn;
        participant.IsMuted = state.IsMuted;

        await Clients.Group(roomId).SendAsync("MediaStateChanged", participant.Id, state);
    }

    /// <summary>
    /// Relays WebRTC signalling between two participants in the same call.
    /// </summary>
    public async Task SendSignal(string roomId, string toParticipantId, string signalType, string signalData)
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

            await Clients.Client(target.ConnectionId).SendAsync("Signal", new
            {
                fromParticipantId = sender.Id,
                type = signalType,
                data = signalData
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error relaying a call signal");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var resolved = RoomService.TryResolveConnection(Context.ConnectionId, out var room, out var participant);
        RoomService.ReleaseConnection(Context.ConnectionId);

        // The participant's own ConnectionId is the authority on which connection is current.
        // A refresh opens the new connection before the old one's disconnect arrives, so
        // acting on a superseded connection would mark somebody offline who is sitting there
        // looking at the room.
        if (resolved
            && !participant.HasLeft
            && string.Equals(participant.ConnectionId, Context.ConnectionId, StringComparison.Ordinal))
        {
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
        participant.IsInVideo = false;

        await Clients.Group(roomId).SendAsync("CallParticipantLeft", participant.Id, participant.DisplayName);
    }
}
