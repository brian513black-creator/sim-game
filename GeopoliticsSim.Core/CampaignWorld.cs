namespace GeopoliticsSim.Core;

public sealed partial class Campaign
{
    private enum TradePosture { ExportFirst, ProtectSupply, SeekTerms }
    private enum CampaignStrategy { Battlegrounds, Industrial, Crossover }

    private Random? _worldRandom;
    private AgendaEpisode[]? _firstAgendaOrder;
    private AgendaEpisode[]? _secondAgendaOrder;
    private readonly Dictionary<string, TradePosture> _partnerPostures = [];
    private readonly Dictionary<string, int> _tradeHistory = [];
    private readonly Dictionary<string, double> _oppositionVisits = [];
    private readonly Dictionary<string, double> _localClimate = [];
    private CampaignStrategy _oppositionStrategy;
    private int _nextOppositionTrip;
    private string? _oppositionLastTrip;

    public bool VariableWorld => _worldRandom != null;

    private void InitializeWorld(int seed)
    {
        _worldRandom = new Random(seed ^ unchecked((int)0xAE41B72D));
        // Shuffle within acts so investigations and policy callbacks still follow the choices they refer to.
        _firstAgendaOrder = FirstAgenda.ToArray();
        _secondAgendaOrder = SecondAgenda.ToArray();
        ShuffleAct(_firstAgendaOrder, 2, 3);
        ShuffleAct(_firstAgendaOrder, 8, 2);
        ShuffleAct(_firstAgendaOrder, 11, 4);
        ShuffleAct(_secondAgendaOrder, 1, 3);
        ShuffleAct(_secondAgendaOrder, 5, 4);
        ShuffleAct(_secondAgendaOrder, 9, 4);
        foreach (var partner in FirstAgenda.Concat(SecondAgenda).Select(episode => episode.Crisis.Ally).Distinct())
            _partnerPostures.Add(partner, (TradePosture)_worldRandom.Next(3));
        var regionalClimate = Enumerable.Range(0, 4).Select(_ => (_worldRandom.NextDouble() - .5) * 2.5).ToArray();
        foreach (var state in States)
            _localClimate.Add(state.Code, regionalClimate[Math.Min(3, state.X / 3)] + (_worldRandom.NextDouble() - .5) * 6);
        InitializeOpposition();
    }

    private void ShuffleAct(AgendaEpisode[] episodes, int start, int length) =>
        _worldRandom!.Shuffle(episodes.AsSpan(start, length));

    private void InitializeOpposition()
    {
        if (_worldRandom == null) return;
        _oppositionVisits.Clear();
        _oppositionLastTrip = null;
        _oppositionStrategy = (CampaignStrategy)_worldRandom.Next(3);
        _nextOppositionTrip = (CurrentElectionDay - Inauguration).Days - _worldRandom.Next(218, 290);
    }

    public string OppositionName => Term == 1 ? CampaignStory.Challenger : "Amara Bell";

    public string? OppositionBrief => _oppositionLastTrip;

    public string? TradeReadout => !VariableWorld ? null : TradePostureFor(PendingTradePartner ?? Current.Ally) switch
    {
        TradePosture.ExportFirst => "Embassy readout. Exporters have the trade ministry’s ear and want access to U.S. buyers.",
        TradePosture.ProtectSupply => "Embassy readout. The ministry is under pressure to reserve supplies for buyers at home.",
        _ => "Embassy readout. The ministry wants a narrow agreement and is listening to its industry groups."
    };

    private TradePosture TradePostureFor(string partner) =>
        _partnerPostures.GetValueOrDefault(partner, TradePosture.SeekTerms);

    public bool EnableLockedTradeTerms()
    {
        if (LockedTradeTerms) return false;
        LockedTradeTerms = true;
        return true;
    }

    private void ResolveTradeTalks()
    {
        TradePosture posture = TradePostureFor(_tradePartner);
        double chance = posture switch
        {
            TradePosture.ExportFirst => .83,
            TradePosture.ProtectSupply => .40,
            _ => .64
        };
        int history = _tradeHistory.GetValueOrDefault(_tradePartner);
        bool includesDomesticSourcing = _tradeUsesLockedTerms ? _tradeIncludesDomesticSourcing : HasMeasure(Measure.DomesticSourcing);
        chance = Math.Clamp(chance + history * .05 + Math.Max(0, _pressure - 1) * .045
            - (includesDomesticSourcing ? .16 : 0), .20, .94);
        double roll = _worldRandom!.NextDouble();
        if (roll < chance * .67)
        {
            _pressure = Math.Max(.1, _pressure - .85);
            Approval += 1.1;
            _tradeHistory[_tradePartner] = Math.Min(3, history + 1);
            AddBrief($"{_tradePartner} approved the full relief agreement. Importers confirmed additional shipments.", true);
        }
        else if (roll < chance)
        {
            _pressure = Math.Max(.1, _pressure - .35);
            Approval += .35;
            AddBrief($"{_tradePartner} limited relief to existing orders. Treasury expects the next round of purchases to stay expensive.", true);
        }
        else
        {
            string reason = posture == TradePosture.ProtectSupply ? "The ministry reserved available inventory for its domestic buyers."
                : "The ministry could not get its industry groups to back the terms.";
            AddBrief($"{_tradePartner} declined the relief proposal. {reason}", true);
        }
    }

    private void TickOpposition()
    {
        if (_worldRandom == null || Day != _nextOppositionTrip || Date >= CurrentElectionDay) return;
        var destination = States.Where(state => state.Electors >= 5 && OppositionEffect(state) > -2.3)
            .MaxBy(state => OppositionPriority(state) + _worldRandom.NextDouble() * 7)!;
        double support = _worldRandom.NextDouble() * .6 + .65;
        _oppositionVisits[destination.Code] = Math.Min(2.8, -OppositionEffect(destination) + support);
        string theme = _oppositionStrategy switch
        {
            CampaignStrategy.Industrial => "jobs and the cost of industrial relief",
            CampaignStrategy.Crossover => "what households paid for the recovery",
            _ => "the governing record in a close state"
        };
        _oppositionLastTrip = $"{OppositionName} visited {destination.Code} to campaign on {theme}.";
        AddBrief(_oppositionLastTrip);
        _nextOppositionTrip += _worldRandom.Next(36, 54);
    }

    private double OppositionPriority(State state)
    {
        double margin = EstimatedMargin(state);
        double priority = state.Electors * .11 + OppositionEffect(state) * 2.6;
        priority -= Math.Abs(margin - (_oppositionStrategy == CampaignStrategy.Crossover ? 2 : 0));
        if (_oppositionStrategy == CampaignStrategy.Industrial) priority += state.Exposure * 4.2;
        return priority;
    }

    private double LocalClimate(State state) => _localClimate.GetValueOrDefault(state.Code);

    private double OppositionEffect(State state) => -_oppositionVisits.GetValueOrDefault(state.Code);
}
