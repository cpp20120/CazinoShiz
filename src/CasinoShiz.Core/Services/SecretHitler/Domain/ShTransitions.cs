using CasinoShiz.Data.Entities;

namespace CasinoShiz.Services.SecretHitler.Domain;

public enum ShValidation
{
    Ok,
    NotPresident,
    NotChancellor,
    NotYourTurn,
    InvalidTarget,
    TermLimited,
    AlreadyVoted,
    WrongPhase,
    InvalidPolicy,
}

public enum ShAfterVoteKind
{
    Pending,
    ElectionPassed,
    ElectionFailed,
    HitlerElectedWin,
}

public sealed record ShAfterVoteResult(ShAfterVoteKind Kind, int JaVotes, int NeinVotes);

public enum ShAfterEnactKind
{
    NextRound,
    LiberalsWin,
    FascistsWin,
}

public sealed record ShAfterEnactResult(ShAfterEnactKind Kind, ShPolicy Enacted);

public static class ShTransitions
{
    public const int FascistWinThreshold = 6;
    public const int LiberalWinThreshold = 5;
    public const int HitlerChancellorThreshold = 3;
    public const int ElectionTrackerCap = 3;

    public static void StartGame(SecretHitlerGame game, List<SecretHitlerPlayer> players)
    {
        ShRoleDealer.DealRoles(players);

        game.DeckState = ShPolicyDeck.BuildShuffledDeck();
        game.DiscardState = "";
        game.LiberalPolicies = 0;
        game.FascistPolicies = 0;
        game.ElectionTracker = 0;
        game.LastElectedPresidentPosition = -1;
        game.LastElectedChancellorPosition = -1;
        game.NominatedChancellorPosition = -1;
        game.PresidentDraw = "";
        game.ChancellorReceived = "";
        game.Winner = ShWinner.None;
        game.WinReason = ShWinReason.None;
        game.Status = ShStatus.Active;
        game.Phase = ShPhase.Nomination;

        var first = players.Where(p => p.IsAlive).OrderBy(p => p.Position).First();
        game.CurrentPresidentPosition = first.Position;

        foreach (var p in players) p.LastVote = ShVote.None;
    }

    public static ShValidation ValidateNomination(
        SecretHitlerGame game, SecretHitlerPlayer president, int chancellorPosition, List<SecretHitlerPlayer> players)
    {
        if (game.Phase != ShPhase.Nomination) return ShValidation.WrongPhase;
        if (president.Position != game.CurrentPresidentPosition) return ShValidation.NotPresident;
        if (chancellorPosition == president.Position) return ShValidation.InvalidTarget;

        var target = players.FirstOrDefault(p => p.Position == chancellorPosition);
        if (target == null || !target.IsAlive) return ShValidation.InvalidTarget;

        var alivePlayers = players.Count(p => p.IsAlive);
        if (alivePlayers > 5)
        {
            if (chancellorPosition == game.LastElectedChancellorPosition) return ShValidation.TermLimited;
            if (chancellorPosition == game.LastElectedPresidentPosition) return ShValidation.TermLimited;
        }
        else
        {
            if (chancellorPosition == game.LastElectedChancellorPosition) return ShValidation.TermLimited;
        }

        return ShValidation.Ok;
    }

    public static void ApplyNomination(SecretHitlerGame game, int chancellorPosition, List<SecretHitlerPlayer> players)
    {
        game.NominatedChancellorPosition = chancellorPosition;
        game.Phase = ShPhase.Election;
        foreach (var p in players) p.LastVote = ShVote.None;
    }

    public static ShValidation ValidateVote(SecretHitlerGame game, SecretHitlerPlayer voter)
    {
        if (game.Phase != ShPhase.Election) return ShValidation.WrongPhase;
        if (!voter.IsAlive) return ShValidation.InvalidTarget;
        if (voter.LastVote != ShVote.None) return ShValidation.AlreadyVoted;
        return ShValidation.Ok;
    }

    public static ShAfterVoteResult? ApplyVote(
        SecretHitlerGame game, SecretHitlerPlayer voter, ShVote vote, List<SecretHitlerPlayer> players)
    {
        voter.LastVote = vote;

        var alive = players.Where(p => p.IsAlive).ToList();
        if (alive.Any(p => p.LastVote == ShVote.None))
            return null;

        int ja = alive.Count(p => p.LastVote == ShVote.Ja);
        int nein = alive.Count(p => p.LastVote == ShVote.Nein);

        if (ja > nein)
        {
            var chancellor = players.First(p => p.Position == game.NominatedChancellorPosition);

            if (game.FascistPolicies >= HitlerChancellorThreshold && chancellor.Role == ShRole.Hitler)
            {
                game.Phase = ShPhase.GameEnd;
                game.Status = ShStatus.Completed;
                game.Winner = ShWinner.Fascists;
                game.WinReason = ShWinReason.HitlerElected;
                return new ShAfterVoteResult(ShAfterVoteKind.HitlerElectedWin, ja, nein);
            }

            game.Phase = ShPhase.LegislativePresident;
            game.ElectionTracker = 0;

            var president = players.First(p => p.Position == game.CurrentPresidentPosition);
            game.LastElectedPresidentPosition = president.Position;
            game.LastElectedChancellorPosition = chancellor.Position;

            var deck = game.DeckState;
            var discard = game.DiscardState;
            var drawn = ShPolicyDeck.Draw(ref deck, ref discard, 3);
            game.DeckState = deck;
            game.DiscardState = discard;
            game.PresidentDraw = ShPolicyDeck.Serialize(drawn);
            game.ChancellorReceived = "";

            return new ShAfterVoteResult(ShAfterVoteKind.ElectionPassed, ja, nein);
        }

        return FailElection(game, players, ja, nein);
    }

    private static ShAfterVoteResult FailElection(
        SecretHitlerGame game, List<SecretHitlerPlayer> players, int ja, int nein)
    {
        game.ElectionTracker++;
        game.NominatedChancellorPosition = -1;

        if (game.ElectionTracker >= ElectionTrackerCap)
        {
            var deck = game.DeckState;
            var discard = game.DiscardState;
            var forced = ShPolicyDeck.Draw(ref deck, ref discard, 1)[0];
            game.DeckState = deck;
            game.DiscardState = discard;

            EnactPolicyCore(game, forced);
            game.ElectionTracker = 0;
            game.LastElectedPresidentPosition = -1;
            game.LastElectedChancellorPosition = -1;

            var winKind = CheckWinAfterPolicy(game);
            if (winKind != ShAfterEnactKind.NextRound)
            {
                game.Phase = ShPhase.GameEnd;
                game.Status = ShStatus.Completed;
                game.Winner = winKind == ShAfterEnactKind.LiberalsWin ? ShWinner.Liberals : ShWinner.Fascists;
                game.WinReason = forced == ShPolicy.Liberal ? ShWinReason.LiberalPolicies : ShWinReason.FascistPolicies;
                return new ShAfterVoteResult(ShAfterVoteKind.ElectionFailed, ja, nein);
            }
        }

        AdvancePresident(game, players);
        game.Phase = ShPhase.Nomination;
        foreach (var p in players) p.LastVote = ShVote.None;
        return new ShAfterVoteResult(ShAfterVoteKind.ElectionFailed, ja, nein);
    }

    public static ShValidation ValidatePresidentDiscard(
        SecretHitlerGame game, SecretHitlerPlayer actor, int discardIndex)
    {
        if (game.Phase != ShPhase.LegislativePresident) return ShValidation.WrongPhase;
        if (actor.Position != game.CurrentPresidentPosition) return ShValidation.NotPresident;
        if (discardIndex < 0 || discardIndex >= game.PresidentDraw.Length) return ShValidation.InvalidPolicy;
        return ShValidation.Ok;
    }

    public static void ApplyPresidentDiscard(SecretHitlerGame game, int discardIndex)
    {
        var draw = ShPolicyDeck.Parse(game.PresidentDraw);
        var discarded = draw[discardIndex];
        draw.RemoveAt(discardIndex);
        game.PresidentDraw = "";
        game.ChancellorReceived = ShPolicyDeck.Serialize(draw);

        var discard = game.DiscardState;
        ShPolicyDeck.AddToDiscard(ref discard, discarded);
        game.DiscardState = discard;

        game.Phase = ShPhase.LegislativeChancellor;
    }

    public static ShValidation ValidateChancellorEnact(
        SecretHitlerGame game, SecretHitlerPlayer actor, int enactIndex)
    {
        if (game.Phase != ShPhase.LegislativeChancellor) return ShValidation.WrongPhase;
        if (actor.Position != game.NominatedChancellorPosition) return ShValidation.NotChancellor;
        if (enactIndex < 0 || enactIndex >= game.ChancellorReceived.Length) return ShValidation.InvalidPolicy;
        return ShValidation.Ok;
    }

    public static ShAfterEnactResult ApplyChancellorEnact(
        SecretHitlerGame game, int enactIndex, List<SecretHitlerPlayer> players)
    {
        var received = ShPolicyDeck.Parse(game.ChancellorReceived);
        var enacted = received[enactIndex];
        received.RemoveAt(enactIndex);

        var discard = game.DiscardState;
        foreach (var leftover in received) ShPolicyDeck.AddToDiscard(ref discard, leftover);
        game.DiscardState = discard;

        game.ChancellorReceived = "";
        game.PresidentDraw = "";

        EnactPolicyCore(game, enacted);

        var winKind = CheckWinAfterPolicy(game);
        if (winKind != ShAfterEnactKind.NextRound)
        {
            game.Phase = ShPhase.GameEnd;
            game.Status = ShStatus.Completed;
            game.Winner = winKind == ShAfterEnactKind.LiberalsWin ? ShWinner.Liberals : ShWinner.Fascists;
            game.WinReason = enacted == ShPolicy.Liberal ? ShWinReason.LiberalPolicies : ShWinReason.FascistPolicies;
            return new ShAfterEnactResult(winKind, enacted);
        }

        AdvancePresident(game, players);
        game.Phase = ShPhase.Nomination;
        game.NominatedChancellorPosition = -1;
        foreach (var p in players) p.LastVote = ShVote.None;

        return new ShAfterEnactResult(ShAfterEnactKind.NextRound, enacted);
    }

    private static void EnactPolicyCore(SecretHitlerGame game, ShPolicy policy)
    {
        if (policy == ShPolicy.Liberal) game.LiberalPolicies++;
        else game.FascistPolicies++;
    }

    private static ShAfterEnactKind CheckWinAfterPolicy(SecretHitlerGame game)
    {
        if (game.LiberalPolicies >= LiberalWinThreshold) return ShAfterEnactKind.LiberalsWin;
        if (game.FascistPolicies >= FascistWinThreshold) return ShAfterEnactKind.FascistsWin;
        return ShAfterEnactKind.NextRound;
    }

    private static void AdvancePresident(SecretHitlerGame game, List<SecretHitlerPlayer> players)
    {
        var alive = players.Where(p => p.IsAlive).OrderBy(p => p.Position).ToList();
        var startIdx = alive.FindIndex(p => p.Position == game.CurrentPresidentPosition);
        if (startIdx < 0) startIdx = -1;
        var nextIdx = (startIdx + 1) % alive.Count;
        game.CurrentPresidentPosition = alive[nextIdx].Position;
    }
}
