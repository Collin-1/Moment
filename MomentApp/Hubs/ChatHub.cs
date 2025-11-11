using Microsoft.AspNetCore.SignalR;
using MomentApp.Models;
using MomentApp.Services;

namespace MomentApp.Hubs;

/// <summary>
/// SignalR hub for real-time chat messaging
/// </summary>
public class ChatHub : Hub
{
    private readonly IRoomService _roomService;
    private readonly IMessageService _messageService;
    private readonly ILogger<ChatHub> _logger;
    private static readonly Dictionary<string, DateTime> _lastMessageTime = new();
    private const int MaxMessagesPerMinute = 10;

    public ChatHub(
        IRoomService roomService,
        IMessageService messageService,
        ILogger<ChatHub> logger)
    {
        _roomService = roomService;
        _messageService = messageService;
        _logger = logger;
    }

    /// <summary>
    /// Join a room for chat messaging
    /// </summary>
    public async Task JoinChatRoom(string roomId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
        _logger.LogInformation($"Connection {Context.ConnectionId} joined chat room {roomId}");
    }

    /// <summary>
    /// Send a message to all participants in the room
    /// </summary>
    public async Task SendMessage(string roomId, string participantId, string content)
    {
        try
        {
            // Rate limiting
            var key = $"{participantId}_{roomId}";
            if (_lastMessageTime.TryGetValue(key, out var lastTime))
            {
                var timeSinceLastMessage = DateTime.UtcNow - lastTime;
                if (timeSinceLastMessage.TotalSeconds < 6) // 10 messages per minute = 1 every 6 seconds
                {
                    await Clients.Caller.SendAsync("Error", "You're sending messages too quickly. Please slow down.");
                    return;
                }
            }
            _lastMessageTime[key] = DateTime.UtcNow;

            // Get participant info
            var room = _roomService.GetRoom(roomId);
            var participant = room?.Participants.FirstOrDefault(p => p.Id == participantId);
            if (participant == null)
            {
                await Clients.Caller.SendAsync("Error", "Participant not found");
                return;
            }

            // Validate message
            if (!_messageService.ValidateMessage(content))
            {
                await Clients.Caller.SendAsync("Error", "Invalid message content");
                return;
            }

            // Create and add message
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
                // Update participant activity
                _roomService.UpdateParticipantActivity(roomId, participant.Id);

                // Broadcast to all clients in the room
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
    public async Task StartTyping(string roomId, string participantId)
    {
        var room = _roomService.GetRoom(roomId);
        var participant = room?.Participants.FirstOrDefault(p => p.Id == participantId);
        if (participant != null)
        {
            await Clients.OthersInGroup(roomId).SendAsync("UserTyping", participant.DisplayName);
        }
    }

    /// <summary>
    /// Notify room that user stopped typing
    /// </summary>
    public async Task StopTyping(string roomId, string participantId)
    {
        var room = _roomService.GetRoom(roomId);
        var participant = room?.Participants.FirstOrDefault(p => p.Id == participantId);
        if (participant != null)
        {
            await Clients.OthersInGroup(roomId).SendAsync("UserStoppedTyping", participant.DisplayName);
        }
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation($"Client connected: {Context.ConnectionId}");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation($"Client disconnected: {Context.ConnectionId}");

        // Clean up rate limiting data
        var keysToRemove = _lastMessageTime.Keys.Where(k => k.StartsWith(Context.ConnectionId)).ToList();
        foreach (var key in keysToRemove)
        {
            _lastMessageTime.Remove(key);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
