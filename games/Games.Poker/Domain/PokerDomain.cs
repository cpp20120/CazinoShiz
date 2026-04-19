namespace Games.Poker.Domain;

public static class PokerDomain
{
    public static void StartHand(PokerTable table, List<PokerSeat> allSeats)
    {
        var playable = allSeats.Where(s => s.Stack > 0).OrderBy(s => s.Position).ToList();

        foreach (var s in allSeats)
        {
            s.HoleCards = "";
            s.CurrentBet = 0;
            s.HasActedThisRound = false;
            s.Status = s.Stack > 0 ? PokerSeatStatus.Seated : PokerSeatStatus.SittingOut;
        }

        var deck = Deck.BuildShuffled();

        foreach (var s in playable)
        {
            var cards = Deck.Draw(ref deck, 2);
            s.HoleCards = string.Join(" ", cards);
        }

        var buttonPos = table.Status == PokerTableStatus.HandComplete
            ? NextActiveSeat(table.ButtonSeat, playable)
            : playable[0].Position;
        if (buttonPos < 0) buttonPos = playable[0].Position;

        table.ButtonSeat = buttonPos;
        table.DeckState = deck;
        table.CommunityCards = "";
        table.Pot = 0;
        table.CurrentBet = 0;
        table.MinRaise = table.BigBlind;
        table.Phase = PokerPhase.PreFlop;
        table.Status = PokerTableStatus.HandActive;
        table.LastActionAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        int sbPos, bbPos, utgPos;
        if (playable.Count == 2)
        {
            sbPos = buttonPos;
            bbPos = NextActiveSeat(buttonPos, playable);
            utgPos = sbPos;
        }
        else
        {
            sbPos = NextActiveSeat(buttonPos, playable);
            bbPos = NextActiveSeat(sbPos, playable);
            utgPos = NextActiveSeat(bbPos, playable);
        }

        PostBlind(playable.First(s => s.Position == sbPos), table.SmallBlind, table);
        PostBlind(playable.First(s => s.Position == bbPos), table.BigBlind, table);
        table.CurrentBet = table.BigBlind;
        table.CurrentSeat = utgPos;
    }

    public static ValidationResult Validate(PokerTable table, PokerSeat seat, PokerAction action)
    {
        var toCall = Math.Max(0, table.CurrentBet - seat.CurrentBet);
        var minTotal = table.CurrentBet + Math.Max(table.BigBlind, table.MinRaise);
        var maxTotal = seat.CurrentBet + seat.Stack;
        return action.Kind switch
        {
            PokerActionKind.Check when toCall > 0 => ValidationResult.CannotCheck,
            PokerActionKind.Check => ValidationResult.Ok,
            PokerActionKind.Call => ValidationResult.Ok,
            PokerActionKind.Fold => ValidationResult.Ok,
            PokerActionKind.AllIn when seat.Stack > 0 => ValidationResult.Ok,
            PokerActionKind.AllIn => ValidationResult.Invalid,
            PokerActionKind.Raise when action.Amount < minTotal => ValidationResult.RaiseTooSmall,
            PokerActionKind.Raise when action.Amount > maxTotal => ValidationResult.RaiseTooLarge,
            PokerActionKind.Raise when action.Amount <= seat.CurrentBet => ValidationResult.Invalid,
            PokerActionKind.Raise => ValidationResult.Ok,
            _ => ValidationResult.Invalid,
        };
    }

    public static void Apply(PokerTable table, PokerSeat seat, PokerAction action)
    {
        switch (action.Kind)
        {
            case PokerActionKind.Check: break;
            case PokerActionKind.Fold:
                seat.Status = PokerSeatStatus.Folded;
                break;
            case PokerActionKind.Call:
            {
                var toCall = Math.Min(seat.Stack, table.CurrentBet - seat.CurrentBet);
                seat.Stack -= toCall;
                seat.CurrentBet += toCall;
                table.Pot += toCall;
                if (seat.Stack == 0) seat.Status = PokerSeatStatus.AllIn;
                break;
            }
            case PokerActionKind.AllIn:
            {
                var put = seat.Stack;
                seat.CurrentBet += put;
                table.Pot += put;
                seat.Stack = 0;
                seat.Status = PokerSeatStatus.AllIn;
                if (seat.CurrentBet > table.CurrentBet)
                {
                    var raiseAmt = seat.CurrentBet - table.CurrentBet;
                    table.MinRaise = Math.Max(table.MinRaise, raiseAmt);
                    table.CurrentBet = seat.CurrentBet;
                }
                break;
            }
            case PokerActionKind.Raise:
            {
                var put = action.Amount - seat.CurrentBet;
                seat.Stack -= put;
                table.Pot += put;
                var raiseAmt = action.Amount - table.CurrentBet;
                seat.CurrentBet = action.Amount;
                table.MinRaise = Math.Max(table.MinRaise, raiseAmt);
                table.CurrentBet = action.Amount;
                if (seat.Stack == 0) seat.Status = PokerSeatStatus.AllIn;
                break;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public static Transition ResolveAfterAction(PokerTable table, List<PokerSeat> seats)
    {
        foreach (var s in seats.Where(s => s.Status == PokerSeatStatus.Seated && s.CurrentBet < table.CurrentBet))
        {
            s.HasActedThisRound = false;
        }

        var inHand = seats.Where(s => s.Status == PokerSeatStatus.Seated || s.Status == PokerSeatStatus.AllIn).ToList();
        if (inHand.Count == 1)
        {
            var from = table.Phase;
            var winner = inHand[0];
            var showdown = Settle(table, seats, awardSingle: winner);
            return new Transition(TransitionKind.HandEndedLastStanding, from, PokerPhase.None, showdown);
        }

        var stillActing = seats.Where(s => s.Status == PokerSeatStatus.Seated).ToList();
        if (stillActing.Count <= 1 && inHand.Count >= 2)
        {
            var from = table.Phase;
            while (table.Phase != PokerPhase.Showdown)
                AdvanceToNextPhase(table, seats);
            var showdown = Settle(table, seats, awardSingle: null);
            return new Transition(TransitionKind.HandEndedRunout, from, PokerPhase.None, showdown);
        }

        if (IsBettingRoundComplete(table, seats))
        {
            if (table.Phase == PokerPhase.River)
            {
                var from = table.Phase;
                table.Phase = PokerPhase.Showdown;
                var showdown = Settle(table, seats, awardSingle: null);
                return new Transition(TransitionKind.HandEndedShowdown, from, PokerPhase.None, showdown);
            }

            var prev = table.Phase;
            AdvanceToNextPhase(table, seats);
            return new Transition(TransitionKind.PhaseAdvanced, prev, table.Phase);
        }

        var next = NextActiveSeat(table.CurrentSeat, seats);
        if (next >= 0) table.CurrentSeat = next;
        return new Transition(TransitionKind.TurnAdvanced, table.Phase, table.Phase);
    }

    public static PokerAction DecideAutoAction(PokerTable table, PokerSeat seat)
    {
        var toCall = table.CurrentBet - seat.CurrentBet;
        return toCall <= 0 ? PokerAction.Check() : PokerAction.Fold();
    }

    private static void PostBlind(PokerSeat seat, int amount, PokerTable table)
    {
        var posted = Math.Min(seat.Stack, amount);
        seat.Stack -= posted;
        seat.CurrentBet += posted;
        table.Pot += posted;
        if (seat.Stack == 0) seat.Status = PokerSeatStatus.AllIn;
    }

    private static int NextActiveSeat(int currentPosition, IList<PokerSeat> seats)
    {
        if (seats.Count == 0) return -1;
        var sorted = seats.OrderBy(s => s.Position).ToList();
        var startIdx = sorted.FindIndex(s => s.Position == currentPosition);
        if (startIdx < 0) startIdx = -1;

        for (var i = 1; i <= sorted.Count; i++)
        {
            var idx = (startIdx + i) % sorted.Count;
            var candidate = sorted[idx];
            if (candidate.Status == PokerSeatStatus.Seated)
                return candidate.Position;
        }
        return -1;
    }

    private static int FirstActiveSeatFrom(int startPosition, IList<PokerSeat> seats)
    {
        if (seats.Count == 0) return -1;
        var sorted = seats.OrderBy(s => s.Position).ToList();
        var startIdx = sorted.FindIndex(s => s.Position >= startPosition);
        if (startIdx < 0) startIdx = 0;

        for (var i = 0; i < sorted.Count; i++)
        {
            var idx = (startIdx + i) % sorted.Count;
            var candidate = sorted[idx];
            if (candidate.Status == PokerSeatStatus.Seated)
                return candidate.Position;
        }
        return -1;
    }

    private static bool IsBettingRoundComplete(PokerTable table, IList<PokerSeat> seats)
    {
        var active = seats.Where(s => s.Status == PokerSeatStatus.Seated).ToList();
        if (active.Count <= 1) return true;

        foreach (var s in active)
        {
            if (!s.HasActedThisRound) return false;
            if (s.CurrentBet != table.CurrentBet) return false;
        }
        return true;
    }

    private static void ResetBettingRound(IList<PokerSeat> seats)
    {
        foreach (var s in seats)
        {
            s.CurrentBet = 0;
            if (s.Status == PokerSeatStatus.Seated)
                s.HasActedThisRound = false;
        }
    }

    private static void AdvanceToNextPhase(PokerTable table, IList<PokerSeat> seats)
    {
        var deck = table.DeckState;
        var community = Deck.Parse(table.CommunityCards).ToList();

        switch (table.Phase)
        {
            case PokerPhase.PreFlop:
                Deck.Draw(ref deck, 1);
                var flop = Deck.Draw(ref deck, 3);
                community.AddRange(flop);
                table.Phase = PokerPhase.Flop;
                break;
            case PokerPhase.Flop:
                Deck.Draw(ref deck, 1);
                var turn = Deck.Draw(ref deck, 1);
                community.AddRange(turn);
                table.Phase = PokerPhase.Turn;
                break;
            case PokerPhase.Turn:
                Deck.Draw(ref deck, 1);
                var river = Deck.Draw(ref deck, 1);
                community.AddRange(river);
                table.Phase = PokerPhase.River;
                break;
            case PokerPhase.River:
                table.Phase = PokerPhase.Showdown;
                break;
        }

        table.DeckState = deck;
        table.CommunityCards = string.Join(" ", community);

        table.CurrentBet = 0;
        table.MinRaise = table.BigBlind;
        ResetBettingRound(seats);

        if (table.Phase is PokerPhase.Flop or PokerPhase.Turn or PokerPhase.River)
        {
            var firstActor = FirstActiveSeatFrom(table.ButtonSeat + 1, seats);
            table.CurrentSeat = firstActor;
        }
    }

    private static List<ShowdownEntry> Settle(PokerTable table, List<PokerSeat> seats, PokerSeat? awardSingle)
    {
        var results = new List<ShowdownEntry>();

        if (awardSingle != null)
        {
            awardSingle.Stack += table.Pot;
            results.Add(new ShowdownEntry(awardSingle, null, table.Pot, awardSingle.HoleCards));
            table.Pot = 0;
        }
        else
        {
            var contenders = seats
                .Where(s => s.Status == PokerSeatStatus.Seated || s.Status == PokerSeatStatus.AllIn)
                .ToList();
            var community = Deck.Parse(table.CommunityCards);

            var ranked = contenders
                .Select(s => (seat: s, rank: HandEvaluator.EvaluateBest(Deck.Parse(s.HoleCards).Concat(community))))
                .OrderByDescending(x => x.rank, Comparer<HandRank>.Default)
                .ToList();

            var best = ranked[0].rank;
            var winners = ranked.Where(x => x.rank.CompareTo(best) == 0).ToList();
            var share = table.Pot / winners.Count;
            var remainder = table.Pot - share * winners.Count;

            foreach (var (s, r) in ranked)
            {
                var won = 0;
                if (winners.Any(w => w.seat.UserId == s.UserId))
                {
                    won = share + (remainder > 0 ? 1 : 0);
                    if (remainder > 0) remainder--;
                    s.Stack += won;
                }
                results.Add(new ShowdownEntry(s, r, won, s.HoleCards));
            }
            table.Pot = 0;
        }

        table.Status = PokerTableStatus.HandComplete;
        table.Phase = PokerPhase.None;

        foreach (var s in seats) { s.HoleCards = ""; s.CurrentBet = 0; s.HasActedThisRound = false; }
        return results;
    }
}
