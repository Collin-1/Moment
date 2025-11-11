namespace MomentApp.Models;

/// <summary>
/// Represents a chat room with ephemeral messages
/// </summary>
public class Room
{
    /// <summary>
    /// Unique 6-character room code (e.g., "X7K2M9")
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Optional name for the room (e.g., "Birthday Planning")
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Timestamp when the room was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the room will automatically expire
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Type of room (OneOnOne or Group)
    /// </summary>
    public RoomType Type { get; set; }

    /// <summary>
    /// List of all participants in the room
    /// </summary>
    public List<Participant> Participants { get; set; } = new List<Participant>();

    /// <summary>
    /// List of all messages in the room
    /// </summary>
    public List<Message> Messages { get; set; } = new List<Message>();

    /// <summary>
    /// Current active vote session (if any)
    /// </summary>
    public VoteSession? ActiveVote { get; set; }

    /// <summary>
    /// Maximum number of participants allowed (default: 50)
    /// </summary>
    public int MaxParticipants { get; set; } = 50;

    /// <summary>
    /// Whether the room is in grace period after vote passed
    /// </summary>
    public bool IsInGracePeriod { get; set; } = false;

    /// <summary>
    /// Timestamp when grace period started (if applicable)
    /// </summary>
    public DateTime? GracePeriodStartedAt { get; set; }
}
