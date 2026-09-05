using MomentApp.Models;

namespace MomentApp.Services;

/// <summary>
/// Status of a vote session
/// </summary>
public class VoteStatus
{
    public int TotalParticipants { get; set; }
    public int YesVotes { get; set; }
    public int NoVotes { get; set; }
    public int NotVoted { get; set; }

    /// <summary>
    /// Yes votes needed to pass, so the UI can show "3 of 5" without duplicating the rule.
    /// </summary>
    public int RequiredVotes { get; set; }

    public double YesPercentage { get; set; }
    public bool HasPassed { get; set; }
    public Dictionary<string, bool?> ParticipantVotes { get; set; } = new();
}

/// <summary>
/// Interface for voting management operations
/// </summary>
public interface IVotingService
{
    /// <summary>
    /// Initiates a vote to close the room
    /// </summary>
    bool InitiateVote(string roomId, string initiatorId);

    /// <summary>
    /// Casts a vote for a participant
    /// </summary>
    bool CastVote(string roomId, string participantId, bool voteYes);

    /// <summary>
    /// Calculates if majority has been reached
    /// </summary>
    bool CalculateMajority(string roomId);

    /// <summary>
    /// Gets current vote status
    /// </summary>
    VoteStatus? GetVoteStatus(string roomId);

    /// <summary>
    /// Recalculates vote when participants leave
    /// </summary>
    void RecalculateVote(string roomId);
}

/// <summary>
/// Implementation of voting management service
/// </summary>
public class VotingService : IVotingService
{
    private readonly IRoomService _roomService;

    public VotingService(IRoomService roomService)
    {
        _roomService = roomService;
    }

    /// <summary>
    /// Participants entitled to vote: still in the room, and reachable.
    /// </summary>
    /// <remarks>
    /// Offline participants are excluded deliberately. They cannot cast a vote, so counting
    /// them in the denominator lets a single ghost — someone who closed their laptop — make
    /// the room permanently impossible to close.
    /// </remarks>
    private static IReadOnlyList<Participant> EligibleVoters(Room room) =>
        room.Participants.Where(p => !p.HasLeft && p.Status != ParticipantStatus.Offline).ToArray();

    public bool InitiateVote(string roomId, string initiatorId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null)
        {
            return false;
        }

        // A vote that nobody carried is cleared here so it can't block the room forever.
        if (room.ActiveVote != null && room.ActiveVote.HasLapsed(DateTime.UtcNow))
        {
            room.ActiveVote = null;
        }

        if (room.ActiveVote != null)
        {
            return false;
        }

        room.ActiveVote = new VoteSession
        {
            InitiatedBy = initiatorId,
            StartedAt = DateTime.UtcNow
        };

        return true;
    }

    public bool CastVote(string roomId, string participantId, bool voteYes)
    {
        var room = _roomService.GetRoom(roomId);
        var vote = room?.ActiveVote;
        if (room == null || vote == null)
        {
            return false;
        }

        // Once it has passed the outcome is settled; further votes would be misleading.
        if (vote.IsPassed)
        {
            return false;
        }

        var participant = room.FindParticipant(participantId);
        if (participant == null || participant.HasLeft)
        {
            return false;
        }

        // Changing your mind is allowed until the vote passes. Ending the room is
        // irreversible, so a mis-tap must not be.
        vote.Votes[participantId] = voteYes;

        if (CalculateMajority(roomId))
        {
            vote.IsPassed = true;
            vote.PassedAt = DateTime.UtcNow;
            _roomService.StartGracePeriod(roomId);
        }

        return true;
    }

    public bool CalculateMajority(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.ActiveVote == null)
        {
            return false;
        }

        var eligible = EligibleVoters(room).Count;
        if (eligible == 0)
        {
            return false;
        }

        var yesVotes = room.ActiveVote.Votes.Count(v => v.Value);

        // Strict majority: 2->2, 3->2, 4->3, 5->3. Integer division, not Math.Ceiling —
        // ceil(n/2)+1 demands unanimity at n=2 and n=3, which is not what ">50%" means.
        var requiredVotes = eligible / 2 + 1;

        return yesVotes >= requiredVotes;
    }

    public VoteStatus? GetVoteStatus(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.ActiveVote == null)
        {
            return null;
        }

        var eligible = EligibleVoters(room);
        var totalParticipants = eligible.Count;
        var yesVotes = room.ActiveVote.Votes.Count(v => v.Value);
        var noVotes = room.ActiveVote.Votes.Count(v => !v.Value);
        var notVoted = Math.Max(0, totalParticipants - (yesVotes + noVotes));

        var participantVotes = new Dictionary<string, bool?>();
        foreach (var participant in eligible)
        {
            participantVotes[participant.DisplayName] =
                room.ActiveVote.Votes.TryGetValue(participant.Id, out var vote) ? vote : null;
        }

        return new VoteStatus
        {
            TotalParticipants = totalParticipants,
            YesVotes = yesVotes,
            NoVotes = noVotes,
            NotVoted = notVoted,
            RequiredVotes = totalParticipants > 0 ? totalParticipants / 2 + 1 : 0,
            YesPercentage = totalParticipants > 0 ? (double)yesVotes / totalParticipants * 100 : 0,
            HasPassed = room.ActiveVote.IsPassed,
            ParticipantVotes = participantVotes
        };
    }

    public void RecalculateVote(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.ActiveVote == null || room.ActiveVote.IsPassed)
        {
            return;
        }

        // Drop votes cast by anyone who is no longer eligible, so the numerator and the
        // denominator always describe the same set of people.
        var eligibleIds = EligibleVoters(room).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in room.ActiveVote.Votes.Keys.Where(id => !eligibleIds.Contains(id)).ToArray())
        {
            room.ActiveVote.Votes.TryRemove(id, out _);
        }

        // Someone leaving can itself carry the vote, since it shrinks the denominator.
        if (CalculateMajority(roomId))
        {
            room.ActiveVote.IsPassed = true;
            room.ActiveVote.PassedAt = DateTime.UtcNow;
            _roomService.StartGracePeriod(roomId);
        }
    }
}
