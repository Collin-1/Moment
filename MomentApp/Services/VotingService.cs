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

    public bool InitiateVote(string roomId, string initiatorId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null || room.ActiveVote != null)
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
        if (room?.ActiveVote == null)
        {
            return false;
        }

        var participant = room.Participants.FirstOrDefault(p => p.Id == participantId && !p.HasLeft);
        if (participant == null)
        {
            return false;
        }

        // Don't allow changing vote
        if (room.ActiveVote.Votes.ContainsKey(participantId))
        {
            return false;
        }

        room.ActiveVote.Votes[participantId] = voteYes;

        // Check if majority reached
        if (CalculateMajority(roomId))
        {
            room.ActiveVote.IsPassed = true;
            room.ActiveVote.PassedAt = DateTime.UtcNow;
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

        var activeParticipants = room.Participants.Count(p => !p.HasLeft);
        if (activeParticipants == 0)
        {
            return false;
        }

        var yesVotes = room.ActiveVote.Votes.Count(v => v.Value);
        var requiredVotes = (int)Math.Ceiling(activeParticipants / 2.0) + 1; // Need >50%

        return yesVotes >= requiredVotes;
    }

    public VoteStatus? GetVoteStatus(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.ActiveVote == null)
        {
            return null;
        }

        var activeParticipants = room.Participants.Where(p => !p.HasLeft).ToList();
        var totalParticipants = activeParticipants.Count;
        var yesVotes = room.ActiveVote.Votes.Count(v => v.Value);
        var noVotes = room.ActiveVote.Votes.Count(v => !v.Value);
        var notVoted = totalParticipants - (yesVotes + noVotes);

        var participantVotes = new Dictionary<string, bool?>();
        foreach (var participant in activeParticipants)
        {
            if (room.ActiveVote.Votes.TryGetValue(participant.Id, out var vote))
            {
                participantVotes[participant.DisplayName] = vote;
            }
            else
            {
                participantVotes[participant.DisplayName] = null;
            }
        }

        return new VoteStatus
        {
            TotalParticipants = totalParticipants,
            YesVotes = yesVotes,
            NoVotes = noVotes,
            NotVoted = notVoted,
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

        // Remove votes from participants who have left
        var activeParticipantIds = room.Participants.Where(p => !p.HasLeft).Select(p => p.Id).ToHashSet();
        var votesToRemove = room.ActiveVote.Votes.Keys.Where(id => !activeParticipantIds.Contains(id)).ToList();

        foreach (var id in votesToRemove)
        {
            room.ActiveVote.Votes.Remove(id);
        }

        // Check if majority reached after recalculation
        if (CalculateMajority(roomId))
        {
            room.ActiveVote.IsPassed = true;
            room.ActiveVote.PassedAt = DateTime.UtcNow;
            _roomService.StartGracePeriod(roomId);
        }
    }
}
