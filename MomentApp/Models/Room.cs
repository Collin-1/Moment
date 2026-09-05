using System.Collections.Concurrent;

namespace MomentApp.Models;

/// <summary>
/// Represents a chat room with ephemeral messages.
/// </summary>
/// <remarks>
/// Rooms are touched concurrently from three thread pools: MVC request threads
/// (<c>RoomController</c>), SignalR hub invocation threads (<c>RoomHub</c>), and the
/// background timer thread (<c>TimerService</c>). Participants and messages are therefore
/// held in concurrent collections and exposed only as snapshots, so a caller can never
/// enumerate a collection that another thread is mutating.
///
/// Concurrent collections make each individual operation safe, but not a sequence of them.
/// Compound "check then act" logic — capacity check, duplicate-name check, then add — must
/// hold <see cref="MutationLock"/>. The lock is per room, so rooms never contend.
/// </remarks>
public class Room
{
    private readonly ConcurrentDictionary<string, Participant> _participants = new(StringComparer.Ordinal);
    private readonly List<Message> _messages = new();
    private readonly object _messagesLock = new();

    /// <summary>
    /// Guards compound participant operations that must be atomic as a group.
    /// </summary>
    internal object MutationLock { get; } = new();

    /// <summary>
    /// Unique 6-character room code (e.g., "X7K2M9")
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Optional name for the room (e.g., "Birthday Planning")
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Optional description shown as people arrive.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Timestamp when the room was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the room will automatically expire
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Which surface this room opens into.
    /// </summary>
    public RoomType Type { get; set; }

    /// <summary>
    /// Snapshot of all participants, in join order.
    /// </summary>
    /// <remarks>
    /// Returns a new array on every access so callers can enumerate and LINQ over it freely.
    /// Ordering by <see cref="Participant.JoinedAt"/> is deliberate: the backing dictionary
    /// has no defined order, and without this the roster would reshuffle between renders.
    ///
    /// Id is the tie-break because <see cref="DateTime.UtcNow"/> only advances every ~15ms on
    /// Windows, so two people joining together get identical timestamps — and the roster would
    /// then be ordered by whatever the dictionary happened to return, giving each participant
    /// a different order.
    /// </remarks>
    public IReadOnlyList<Participant> Participants =>
        _participants.Values
            .OrderBy(p => p.JoinedAt)
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Snapshot of all messages, in the order they were added.
    /// </summary>
    public IReadOnlyList<Message> Messages
    {
        get { lock (_messagesLock) { return _messages.ToArray(); } }
    }

    /// <summary>
    /// Current active vote session (if any)
    /// </summary>
    public VoteSession? ActiveVote { get; set; }

    /// <summary>
    /// Maximum number of participants allowed.
    /// </summary>
    /// <remarks>
    /// Capped at the size of <c>ColorService</c>'s palette: every participant is identified
    /// by a distinct colour throughout the UI, so the palette is the real ceiling.
    /// </remarks>
    public int MaxParticipants { get; set; } = 12;

    /// <summary>
    /// Whether the room is in grace period after vote passed
    /// </summary>
    public bool IsInGracePeriod { get; set; } = false;

    /// <summary>
    /// Timestamp when grace period started (if applicable)
    /// </summary>
    public DateTime? GracePeriodStartedAt { get; set; }

    /// <summary>
    /// Adds a participant. Returns false if the id is already present.
    /// </summary>
    internal bool TryAddParticipant(Participant participant) =>
        _participants.TryAdd(participant.Id, participant);

    /// <summary>
    /// Looks up a participant by id in O(1).
    /// </summary>
    internal Participant? FindParticipant(string participantId) =>
        _participants.TryGetValue(participantId, out var participant) ? participant : null;

    /// <summary>
    /// Appends a message.
    /// </summary>
    internal void AddMessage(Message message)
    {
        lock (_messagesLock) { _messages.Add(message); }
    }
}
