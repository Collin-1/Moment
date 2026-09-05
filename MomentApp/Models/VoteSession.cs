using System.Collections.Concurrent;

namespace MomentApp.Models;

/// <summary>
/// Represents a voting session to close a room
/// </summary>
public class VoteSession
{
    /// <summary>
    /// How long a vote stays open before it lapses and anyone may start a fresh one.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    /// <summary>
    /// ID of the participant who initiated the vote
    /// </summary>
    public string InitiatedBy { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the vote was started
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Participant votes: ParticipantId -> Yes(true)/No(false).
    /// </summary>
    /// <remarks>
    /// Concurrent because votes are cast from hub threads while the timer thread and other
    /// hub invocations enumerate it to build vote status.
    /// </remarks>
    public ConcurrentDictionary<string, bool> Votes { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Whether the vote has passed (>50% of eligible participants voted yes)
    /// </summary>
    public bool IsPassed { get; set; } = false;

    /// <summary>
    /// Timestamp when the vote passed (if applicable)
    /// </summary>
    public DateTime? PassedAt { get; set; }

    /// <summary>
    /// True once an unpassed vote has been open longer than <see cref="Lifetime"/>.
    /// </summary>
    /// <remarks>
    /// Without this a single failed vote would block the room forever: starting a vote
    /// requires <c>ActiveVote</c> to be null, and nothing else ever clears it.
    /// </remarks>
    public bool HasLapsed(DateTime utcNow) => !IsPassed && utcNow - StartedAt > Lifetime;
}
