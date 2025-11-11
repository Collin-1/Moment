namespace MomentApp.Models;

/// <summary>
/// Represents a message in a chat room
/// </summary>
public class Message
{
    /// <summary>
    /// Unique identifier for the message
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// ID of the participant who sent the message
    /// </summary>
    public string SenderId { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the sender
    /// </summary>
    public string SenderName { get; set; } = string.Empty;

    /// <summary>
    /// Hex color code of the sender
    /// </summary>
    public string SenderColor { get; set; } = string.Empty;

    /// <summary>
    /// Content of the message (max 2000 characters)
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the message was sent
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Type of message (User or System)
    /// </summary>
    public MessageType Type { get; set; } = MessageType.User;
}
