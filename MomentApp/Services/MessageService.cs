using MomentApp.Models;
using System.Text.RegularExpressions;
using System.Web;

namespace MomentApp.Services;

/// <summary>
/// Interface for message management operations
/// </summary>
public interface IMessageService
{
    /// <summary>
    /// Adds a message to a room
    /// </summary>
    bool AddMessage(string roomId, Message message);

    /// <summary>
    /// Gets all messages for a room
    /// </summary>
    List<Message> GetMessages(string roomId);

    /// <summary>
    /// Validates message content
    /// </summary>
    bool ValidateMessage(string content);

    /// <summary>
    /// Sanitizes message content to prevent XSS
    /// </summary>
    string SanitizeContent(string content);

    /// <summary>
    /// Creates a system message
    /// </summary>
    Message CreateSystemMessage(string content, string senderName = "System");
}

/// <summary>
/// Implementation of message management service
/// </summary>
public class MessageService : IMessageService
{
    private readonly IRoomService _roomService;
    private const int MaxMessageLength = 2000;

    public MessageService(IRoomService roomService)
    {
        _roomService = roomService;
    }

    public bool AddMessage(string roomId, Message message)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null)
        {
            return false;
        }

        // Validate and sanitize
        if (!ValidateMessage(message.Content))
        {
            return false;
        }

        message.Content = SanitizeContent(message.Content);
        message.Timestamp = DateTime.UtcNow;

        room.Messages.Add(message);
        return true;
    }

    public List<Message> GetMessages(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        return room?.Messages ?? new List<Message>();
    }

    public bool ValidateMessage(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        if (content.Length > MaxMessageLength)
        {
            return false;
        }

        return true;
    }

    public string SanitizeContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        // HTML encode to prevent XSS
        content = HttpUtility.HtmlEncode(content);

        // Convert URLs to clickable links (simple regex)
        content = Regex.Replace(content,
            @"(https?://[^\s]+)",
            "<a href=\"$1\" target=\"_blank\" rel=\"noopener noreferrer\">$1</a>",
            RegexOptions.IgnoreCase);

        return content.Trim();
    }

    public Message CreateSystemMessage(string content, string senderName = "System")
    {
        return new Message
        {
            Id = Guid.NewGuid().ToString(),
            SenderId = "system",
            SenderName = senderName,
            SenderColor = "#6B7280", // Gray color
            Content = content,
            Timestamp = DateTime.UtcNow,
            Type = MessageType.System
        };
    }
}
