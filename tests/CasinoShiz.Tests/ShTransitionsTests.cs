using CasinoShiz.Data.Entities;
using CasinoShiz.Services.SecretHitler.Domain;
using Xunit;

namespace CasinoShiz.Tests;

public class ShTransitionsTests
{
    private static List<SecretHitlerPlayer> MakePlayers(int count, ShRole? hitlerAt = null, int hitlerPosition = 0)
    {
        var players = new List<SecretHitlerPlayer>();
        for (int i = 0; i < count; i++)
        {
            players.Add(new SecretHitlerPlayer
            {
                Position = i,
                UserId = 100 + i,
                IsAlive = true,
                Role = ShRole.Liberal,
            });
        }
        if (hitlerAt.HasValue) players[hitlerPosition].Role = hitlerAt.Value;
        return players;
    }

    private static SecretHitlerGame MakeActiveGame(string deck = "LLLLLLFFFFFFFFFFF")
    {
        return new SecretHitlerGame
        {
            InviteCode = "TEST",
            Status = ShStatus.Active,
            Phase = ShPhase.Nomination,
            DeckState = deck,
            DiscardState = "",
            CurrentPresidentPosition = 0,
            NominatedChancellorPosition = -1,
            LastElectedPresidentPosition = -1,
            LastElectedChancellorPosition = -1,
        };
    }

    [Fact]
    public void ValidateNomination_wrongPhase_returnsWrongPhase()
    {
        var game = MakeActiveGame();
        game.Phase = ShPhase.Election;
        var players = MakePlayers(5);

        var r = ShTransitions.ValidateNomination(game, players[0], 1, players);
        Assert.Equal(ShValidation.WrongPhase, r);
    }

    [Fact]
    public void ValidateNomination_notPresident_returnsNotPresident()
    {
        var game = MakeActiveGame();
        var players = MakePlayers(5);

        var r = ShTransitions.ValidateNomination(game, players[1], 2, players);
        Assert.Equal(ShValidation.NotPresident, r);
    }

    [Fact]
    public void ValidateNomination_selfNomination_returnsInvalidTarget()
    {
        var game = MakeActiveGame();
        var players = MakePlayers(5);

        var r = ShTransitions.ValidateNomination(game, players[0], 0, players);
        Assert.Equal(ShValidation.InvalidTarget, r);
    }

    [Fact]
    public void ValidateNomination_deadChancellor_returnsInvalidTarget()
    {
        var game = MakeActiveGame();
        var players = MakePlayers(5);
        players[2].IsAlive = false;

        var r = ShTransitions.ValidateNomination(game, players[0], 2, players);
        Assert.Equal(ShValidation.InvalidTarget, r);
    }

    [Fact]
    public void ValidateNomination_reelectingLastChancellor_termLimited()
    {
        var game = MakeActiveGame();
        game.LastElectedChancellorPosition = 3;
        var players = MakePlayers(5);

        var r = ShTransitions.ValidateNomination(game, players[0], 3, players);
        Assert.Equal(ShValidation.TermLimited, r);
    }

    [Fact]
    public void ValidateNomination_moreThanFive_lastPresidentAlsoTermLimited()
    {
        var game = MakeActiveGame();
        game.LastElectedPresidentPosition = 2;
        var players = MakePlayers(7);

        var r = ShTransitions.ValidateNomination(game, players[0], 2, players);
        Assert.Equal(ShValidation.TermLimited, r);
    }

    [Fact]
    public void ValidateNomination_fivePlayers_lastPresidentStillEligible()
    {
        var game = MakeActiveGame();
        game.LastElectedPresidentPosition = 2;
        var players = MakePlayers(5);

        var r = ShTransitions.ValidateNomination(game, players[0], 2, players);
        Assert.Equal(ShValidation.Ok, r);
    }

    [Fact]
    public void ApplyNomination_advancesPhaseAndResetsVotes()
    {
        var game = MakeActiveGame();
        var players = MakePlayers(5);
        foreach (var p in players) p.LastVote = ShVote.Ja;

        ShTransitions.ApplyNomination(game, 2, players);

        Assert.Equal(ShPhase.Election, game.Phase);
        Assert.Equal(2, game.NominatedChancellorPosition);
        Assert.All(players, p => Assert.Equal(ShVote.None, p.LastVote));
    }

    [Fact]
    public void ValidateVote_alreadyVoted_returnsAlreadyVoted()
    {
        var game = MakeActiveGame();
        game.Phase = ShPhase.Election;
        var players = MakePlayers(5);
        players[1].LastVote = ShVote.Ja;

        var r = ShTransitions.ValidateVote(game, players[1]);
        Assert.Equal(ShValidation.AlreadyVoted, r);
    }

    [Fact]
    public void ValidateVote_deadPlayer_returnsInvalidTarget()
    {
        var game = MakeActiveGame();
        game.Phase = ShPhase.Election;
        var players = MakePlayers(5);
        players[1].IsAlive = false;

        var r = ShTransitions.ValidateVote(game, players[1]);
        Assert.Equal(ShValidation.InvalidTarget, r);
    }

    [Fact]
    public void ApplyVote_pending_returnsNullUntilAllVoted()
    {
        var game = MakeActiveGame();
        game.Phase = ShPhase.Election;
        game.NominatedChancellorPosition = 1;
        var players = MakePlayers(5);

        var r = ShTransitions.ApplyVote(game, players[0], ShVote.Ja, players);
        Assert.Null(r);
        Assert.Equal(ShVote.Ja, players[0].LastVote);
    }

    [Fact]
    public void ApplyVote_majorityJa_advancesToLegislativePresident()
    {
        var game = MakeActiveGame();
        game.Phase = ShPhase.Election;
        game.NominatedChancellorPosition = 1;
        var players = MakePlayers(5);
        players[0].LastVote = ShVote.Ja;
        players[1].LastVote = ShVote.Ja;
        players[2].LastVote = ShVote.Nein;
        players[3].LastVote = ShVote.Nein;

        var r = ShTransitions.ApplyVote(game, players[4], ShVote.Ja, players);

        Assert.NotNull(r);
        Assert.Equal(ShAfterVoteKind.ElectionPassed, r!.Kind);
        Assert.Equal(3, r.JaVotes);
        Assert.Equal(2, r.NeinVotes);
        Assert.Equal(ShPhase.LegislativePresident, game.Phase);
        Assert.Equal(3, game.PresidentDraw.Length);
        Assert.Equal(0, game.ElectionTracker);
        Assert.Equal(0, game.LastElectedPresidentPosition);
        Assert.Equal(1, game.LastElectedChancellorPosition);
    }

    [Fact]
    public void ApplyVote_tieVotes_electionFails()
    {
        var game = MakeActiveGame();
        game.Phase = ShPhase.Election;
        game.NominatedChancellorPosition = 1;
        var players = MakePlayers(6);
        players[0].LastVote = ShVote.Ja;
        players[1].LastVote = ShVote.Ja;
        players[2].LastVote = ShVote.Ja;
        players[3].LastVote = ShVote.Nein;
        players[4].LastVote = ShVote.Nein;

        var r = ShTransitions.ApplyVote(game, players[5], ShVote.Nein, players);

        Assert.NotNull(r);
        Assert.Equal(ShAfterVoteKind.ElectionFailed, r!.Kind);
        Assert.Equal(ShPhase.Nomination, game.Phase);
        Assert.Equal(1, game.ElectionTracker);
        Assert.Equal(1, game.CurrentPresidentPosition);
    }

    [Fact]
    public void ApplyVote_hitlerElectedAfterThreeFascistPolicies_fascistsWin()
    {
        var game = MakeActiveGame();
        game.Phase = ShPhase.Election;
        game.NominatedChancellorPosition = 1;
        game.FascistPolicies = 3;
        var players = MakePlayers(5, hitlerAt: ShRole.Hitler, hitlerPosition: 1);
        players[0].LastVote = ShVote.Ja;
        players[1].LastVote = ShVote.Ja;
        players[2].LastVote = ShVote.Ja;
        players[3].LastVote = ShVote.Nein;

        var r = ShTransitions.ApplyVote(game, players[4], ShVote.Nein, players);

        Assert.NotNull(r);
        Assert.Equal(ShAfterVoteKind.HitlerElectedWin, r!.Kind);
        Assert.Equal(ShPhase.GameEnd, game.Phase);
        Assert.Equal(ShStatus.Completed, game.Status);
        Assert.Equal(ShWinner.Fascists, game.Winner);
        Assert.Equal(ShWinReason.HitlerElected, game.WinReason);
    }

    [Fact]
    public void ApplyVote_hitlerElectedBeforeThreeFascistPolicies_noSpecialWin()
    {
        var game = MakeActiveGame();
        game.Phase = ShPhase.Election;
        game.NominatedChancellorPosition = 1;
        game.FascistPolicies = 2;
        var players = MakePlayers(5, hitlerAt: ShRole.Hitler, hitlerPosition: 1);
        players[0].LastVote = ShVote.Ja;
        players[1].LastVote = ShVote.Ja;
        players[2].LastVote = ShVote.Ja;
        players[3].LastVote = ShVote.Nein;

        var r = ShTransitions.ApplyVote(game, players[4], ShVote.Nein, players);

        Assert.Equal(ShAfterVoteKind.ElectionPassed, r!.Kind);
        Assert.Equal(ShPhase.LegislativePresident, game.Phase);
    }

    [Fact]
    public void ApplyVote_thirdFailedElection_enactsTopPolicyAndAdvances()
    {
        var game = MakeActiveGame(deck: "LLLLLLLLLLLLLLLLL");
        game.Phase = ShPhase.Election;
        game.NominatedChancellorPosition = 1;
        game.ElectionTracker = 2;
        var players = MakePlayers(5);
        players[0].LastVote = ShVote.Nein;
        players[1].LastVote = ShVote.Nein;
        players[2].LastVote = ShVote.Nein;
        players[3].LastVote = ShVote.Nein;

        var r = ShTransitions.ApplyVote(game, players[4], ShVote.Nein, players);

        Assert.Equal(ShAfterVoteKind.ElectionFailed, r!.Kind);
        Assert.Equal(1, game.LiberalPolicies);
        Assert.Equal(0, game.ElectionTracker);
        Assert.Equal(-1, game.LastElectedPresidentPosition);
        Assert.Equal(-1, game.LastElectedChancellorPosition);
        Assert.Equal(ShPhase.Nomination, game.Phase);
    }

    [Fact]
    public void ApplyVote_thirdFailedElection_canTriggerLiberalWin()
    {
        var game = MakeActiveGame(deck: "LLLLLLLLLLLLLLLLL");
        game.Phase = ShPhase.Election;
        game.NominatedChancellorPosition = 1;
        game.ElectionTracker = 2;
        game.LiberalPolicies = 4;
        var players = MakePlayers(5);
        players[0].LastVote = ShVote.Nein;
        players[1].LastVote = ShVote.Nein;
        players[2].LastVote = ShVote.Nein;
        players[3].LastVote = ShVote.Nein;

        ShTransitions.ApplyVote(game, players[4], ShVote.Nein, players);

        Assert.Equal(5, game.LiberalPolicies);
        Assert.Equal(ShPhase.GameEnd, game.Phase);
        Assert.Equal(ShWinner.Liberals, game.Winner);
        Assert.Equal(ShWinReason.LiberalPolicies, game.WinReason);
    }

    [Fact]
    public void ValidatePresidentDiscard_wrongPhase_returnsWrongPhase()
    {
        var game = MakeActiveGame();
        game.PresidentDraw = "LFL";
        var players = MakePlayers(5);

        var r = ShTransitions.ValidatePresidentDiscard(game, players[0], 0);
        Assert.Equal(ShValidation.WrongPhase, r);
    }

    [Fact]
    public void ValidatePresidentDiscard_notPresident_returnsNotPresident()
    {
        var game = MakeActiveGame();
        game.Phase = ShPhase.LegislativePresident;
        game.PresidentDraw = "LFL";
        var players = MakePlayers(5);

        var r = ShTransitions.ValidatePresidentDiscard(game, players[1], 0);
        Assert.Equal(ShValidation.NotPresident, r);
    }

    [Fact]
    public void ValidatePresidentDiscard_indexOutOfRange_returnsInvalidPolicy()
    {
        var game = MakeActiveGame();
        game.Phase = ShPhase.LegislativePresident;
        game.PresidentDraw = "LFL";
        var players = MakePlayers(5);

        Assert.Equal(ShValidation.InvalidPolicy, ShTransitions.ValidatePresidentDiscard(game, players[0], -1));
        Assert.Equal(ShValidation.InvalidPolicy, ShTransitions.ValidatePresidentDiscard(game, players[0], 3));
    }

    [Fact]
    public void ApplyPresidentDiscard_movesToDiscardAndHandsTwoToChancellor()
    {
        var game = MakeActiveGame();
        game.Phase = ShPhase.LegislativePresident;
        game.PresidentDraw = "LFL";
        game.DiscardState = "";

        ShTransitions.ApplyPresidentDiscard(game, 1);

        Assert.Equal("", game.PresidentDraw);
        Assert.Equal("LL", game.ChancellorReceived);
        Assert.Equal("F", game.DiscardState);
        Assert.Equal(ShPhase.LegislativeChancellor, game.Phase);
    }

    [Fact]
    public void ApplyChancellorEnact_liberal_incrementsAndAdvances()
    {
        var game = MakeActiveGame();
        game.Phase = ShPhase.LegislativeChancellor;
        game.ChancellorReceived = "LF";
        game.DiscardState = "";
        game.CurrentPresidentPosition = 0;
        game.NominatedChancellorPosition = 1;
        var players = MakePlayers(5);

        var r = ShTransitions.ApplyChancellorEnact(game, 0, players);

        Assert.Equal(ShAfterEnactKind.NextRound, r.Kind);
        Assert.Equal(ShPolicy.Liberal, r.Enacted);
        Assert.Equal(1, game.LiberalPolicies);
        Assert.Equal("F", game.DiscardState);
        Assert.Equal(ShPhase.Nomination, game.Phase);
        Assert.Equal(1, game.CurrentPresidentPosition);
        Assert.Equal(-1, game.NominatedChancellorPosition);
    }

    [Fact]
    public void ApplyChancellorEnact_fifthLiberal_liberalsWin()
    {
        var game = MakeActiveGame();
        game.Phase = ShPhase.LegislativeChancellor;
        game.ChancellorReceived = "LL";
        game.LiberalPolicies = 4;
        var players = MakePlayers(5);

        var r = ShTransitions.ApplyChancellorEnact(game, 0, players);

        Assert.Equal(ShAfterEnactKind.LiberalsWin, r.Kind);
        Assert.Equal(5, game.LiberalPolicies);
        Assert.Equal(ShPhase.GameEnd, game.Phase);
        Assert.Equal(ShStatus.Completed, game.Status);
        Assert.Equal(ShWinner.Liberals, game.Winner);
        Assert.Equal(ShWinReason.LiberalPolicies, game.WinReason);
    }

    [Fact]
    public void ApplyChancellorEnact_sixthFascist_fascistsWin()
    {
        var game = MakeActiveGame();
        game.Phase = ShPhase.LegislativeChancellor;
        game.ChancellorReceived = "FF";
        game.FascistPolicies = 5;
        var players = MakePlayers(5);

        var r = ShTransitions.ApplyChancellorEnact(game, 0, players);

        Assert.Equal(ShAfterEnactKind.FascistsWin, r.Kind);
        Assert.Equal(6, game.FascistPolicies);
        Assert.Equal(ShWinner.Fascists, game.Winner);
        Assert.Equal(ShWinReason.FascistPolicies, game.WinReason);
    }

    [Fact]
    public void ApplyChancellorEnact_skipsDeadPlayersWhenAdvancingPresident()
    {
        var game = MakeActiveGame();
        game.Phase = ShPhase.LegislativeChancellor;
        game.ChancellorReceived = "LF";
        game.CurrentPresidentPosition = 0;
        game.NominatedChancellorPosition = 3;
        var players = MakePlayers(5);
        players[1].IsAlive = false;
        players[2].IsAlive = false;

        ShTransitions.ApplyChancellorEnact(game, 0, players);

        Assert.Equal(3, game.CurrentPresidentPosition);
    }

    [Fact]
    public void ApplyChancellorEnact_wrapsAroundToFirstAlive()
    {
        var game = MakeActiveGame();
        game.Phase = ShPhase.LegislativeChancellor;
        game.ChancellorReceived = "LF";
        game.CurrentPresidentPosition = 4;
        game.NominatedChancellorPosition = 0;
        var players = MakePlayers(5);

        ShTransitions.ApplyChancellorEnact(game, 0, players);

        Assert.Equal(0, game.CurrentPresidentPosition);
    }

    [Fact]
    public void StartGame_dealsRolesAndResetsAllState()
    {
        var game = new SecretHitlerGame
        {
            InviteCode = "X",
            FascistPolicies = 3,
            LiberalPolicies = 2,
            ElectionTracker = 2,
            NominatedChancellorPosition = 7,
            Winner = ShWinner.Fascists,
            WinReason = ShWinReason.HitlerElected,
        };
        var players = MakePlayers(5);
        foreach (var p in players) p.LastVote = ShVote.Ja;

        ShTransitions.StartGame(game, players);

        Assert.Equal(ShStatus.Active, game.Status);
        Assert.Equal(ShPhase.Nomination, game.Phase);
        Assert.Equal(0, game.LiberalPolicies);
        Assert.Equal(0, game.FascistPolicies);
        Assert.Equal(0, game.ElectionTracker);
        Assert.Equal(-1, game.NominatedChancellorPosition);
        Assert.Equal(-1, game.LastElectedPresidentPosition);
        Assert.Equal(-1, game.LastElectedChancellorPosition);
        Assert.Equal("", game.PresidentDraw);
        Assert.Equal("", game.ChancellorReceived);
        Assert.Equal(ShWinner.None, game.Winner);
        Assert.Equal(ShWinReason.None, game.WinReason);
        Assert.Equal(17, game.DeckState.Length);
        Assert.Equal(0, game.CurrentPresidentPosition);
        Assert.All(players, p => Assert.Equal(ShVote.None, p.LastVote));
        Assert.Equal(1, players.Count(p => p.Role == ShRole.Hitler));
    }

    [Fact]
    public void StartGame_firstPresidentIsLowestAlivePosition()
    {
        var game = new SecretHitlerGame { InviteCode = "X" };
        var players = MakePlayers(5);
        players[0].IsAlive = false;
        players[1].IsAlive = false;

        ShTransitions.StartGame(game, players);

        Assert.Equal(2, game.CurrentPresidentPosition);
    }
}
