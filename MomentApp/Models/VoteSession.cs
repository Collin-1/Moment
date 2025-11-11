namespace MomentApp.Models;

/// <summary>
/// Represents a voting session to close a room
/// </summary>
public class VoteSession
{
    /// <summary>
    /// ID of the participant who initiated the vote
    /// </summary>
    public string InitiatedBy { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the vote was started
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Dictionary of participant votes: ParticipantId -> Yes(true)/No(false)
    /// </summary>
    public Dictionary<string, bool> Votes { get; set; } = new Dictionary<string, bool>();

    /// <summary>
    /// Whether the vote has passed (>50% voted yes)
    /// </summary>
    public bool IsPassed { get; set; } = false;

    /// <summary>
    /// Timestamp when the vote passed (if applicable)
    /// </summary>
    public DateTime? PassedAt { get; set; }
}
