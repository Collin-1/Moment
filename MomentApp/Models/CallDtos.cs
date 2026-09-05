namespace MomentApp.Models;

/// <summary>
/// A participant's live media state, sent as one object so the flags can never disagree.
/// </summary>
/// <remarks>
/// Previously video-on and video-off were separate events. Independent booleans arriving out
/// of order is how a UI ends up showing somebody as simultaneously sharing and not sharing.
/// </remarks>
public sealed record MediaStateDto(bool IsVideoOn, bool IsMuted);

/// <summary>
/// One peer in the call roster.
/// </summary>
/// <param name="ShouldOffer">
/// Whether the recipient should open the connection to this peer. Decided server-side from a
/// stable comparison of the two ids so exactly one side initiates. Perfect negotiation would
/// survive both sides offering, but halving the offers halves the collisions.
/// </param>
public sealed record CallPeerDto(
    string Id,
    string DisplayName,
    bool IsVideoOn,
    bool IsMuted,
    bool ShouldOffer);
