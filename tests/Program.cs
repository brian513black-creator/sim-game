using GeopoliticsSim.Core;

static void Require(bool condition, string context)
{
    if (!condition) throw new InvalidOperationException(context);
}

static void PlayTerm(Campaign game, bool engage)
{
    while (!game.Finished)
    {
        if (game.OnDesk)
        {
            Require(game.Advance(120) == 0, "Calendar must wait for the President’s signature.");
            Require(game.ResolveBill(true), "President can sign the bill.");
        }
        if (game.PendingOffer != null)
            Require(game.RespondToOffer(OfferResponse.Decline), "President can decline an improper offer.");
        if (engage)
        {
            if (game.CanEdit)
            {
                if (!game.HasMeasure(Measure.DeficitOffset)) game.Toggle(Measure.DeficitOffset);
                if (!game.Reconciliation) game.ChangeTrack();
            }
            if (game.CanChooseAgenda) game.ChooseAgendaOption(1);
            if (game.PendingConversation is { } conversation) game.AnswerConversation(conversation.Id, 0);
            if (game.CanTrade) game.Trade();
            if (game.CanRule) game.Rule();
            if (game.Date.Year >= game.CurrentElectionDay.Year && game.Results.Count == 0)
                foreach (string state in new[] { "MI", "PA", "WI", "NV", "GA", "NC", "AZ", "MN" })
                    game.Rally(state);
        }
        Require(game.Advance(120, stopAtStory: true) > 0, "Active term must be able to advance.");
    }

    Require(game.Results.Count == 51, "Election resolves all states and D.C.");
    Require(game.Results.Sum(result => result.State.Electors) == 538, "Election accounts for all electoral votes.");
}

var loss = new Campaign(2029, oversightSeed: 2029, variableWorld: true);
PlayTerm(loss, engage: false);
Require(loss.ElectoralVotes < 270 && !loss.CanBeginSecondTerm && !loss.BeginSecondTerm(), "Defeat ends the first presidency.");

var winner = new Campaign(2029, oversightSeed: 2029, variableWorld: true);
PlayTerm(winner, engage: true);
Require(winner.ElectoralVotes >= 270 && winner.BeginSecondTerm(), "Reelection unlocks the second term.");
PlayTerm(winner, engage: true);
Require(winner.Date == new DateTime(2037, 1, 20) && !winner.BeginSecondTerm(), "Second term ends at inauguration with no third term.");
Require(winner.Briefings.Any(briefing => briefing.SceneTitle == "Elena’s last letter"), "Final story survives the handover.");

var lateBill = new Campaign(2110, oversightSeed: 2029, agendaEnabled: false, variableWorld: true);
while (!lateBill.Finished)
{
    if (lateBill.OnDesk) Require(lateBill.ResolveBill(true), "An active president can sign an earlier bill.");
    if (lateBill.CanEdit)
    {
        if (!lateBill.HasMeasure(Measure.DeficitOffset)) lateBill.Toggle(Measure.DeficitOffset);
        if (!lateBill.Reconciliation) lateBill.ChangeTrack();
    }
    Require(lateBill.Advance(120, stopAtStory: true) > 0, "Calendar advances to the scheduled handover.");
}
Require(lateBill.Date == Campaign.TermEnd && lateBill.OnDesk && !lateBill.CanBeginSecondTerm,
    "The floor vote on inauguration day leaves a bill on the outgoing desk.");
Require(!lateBill.ResolveBill(true) && !lateBill.ResolveBill(false) && lateBill.OnDesk,
    "An outgoing president cannot sign or veto after leaving office.");
Require(lateBill.Briefings.Any(briefing => briefing.Date == lateBill.Date && briefing.Text.Contains("left unsigned at the handover")),
    "The final floor-vote briefing records its unsigned status.");

Console.WriteLine($"Campaign smoke checks passed. First-term loss {loss.ElectoralVotes} EV, reelection {winner.FirstElectionElectoralVotes} EV, eight-year handover.");

DefenseScopeSmoke.Run();
TradeHandoffSmoke.Run();
RollCallSmoke.Run();
ExecutivePolicySmoke.Run();
WartimeStorySmoke.Run();
