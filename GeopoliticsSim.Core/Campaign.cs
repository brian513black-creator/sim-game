namespace GeopoliticsSim.Core;

public record Crisis(string Title, string Brief, string Ally, double Severity);
public record State(string Code, int Electors, int HouseSeats, double Lean, double Exposure, int X, int Y);
public record Seat(string State, bool Senator, bool Government, double Persuadability);
public record Vote(Seat Seat, bool Yes, bool Swing);
public record FloorVote(int House, int Senate, int SenateTarget);
public record StateResult(State State, double ApprovalEffect, double EconomyEffect, double CampaignEffect, double ElectionVariation, double OppositionEffect = 0, double LocalClimate = 0, double PolicyReputation = 0)
{
    public double Margin => State.Lean + ApprovalEffect + EconomyEffect + CampaignEffect + ElectionVariation + OppositionEffect + LocalClimate + PolicyReputation;
}
public record Briefing(DateTime Date, string Text, bool Decision, string? SceneTitle = null, bool RequiresElectionCall = false, string? Speaker = null)
{
    public bool Story => SceneTitle != null;
}
public enum Measure { WorkerRelief, ProductionCredits, DeficitOffset, DomesticSourcing }

public sealed partial class Campaign
{
    private readonly Random _random;
    private readonly List<Seat> _seats = [];
    private readonly List<Briefing> _briefings = [];
    private readonly HashSet<Measure> _measures = [Measure.WorkerRelief, Measure.ProductionCredits];
    private readonly HashSet<string> _visited = [];
    private readonly Dictionary<string, double> _deals = [];
    private (IReadOnlyList<Vote> House, IReadOnlyList<Vote> Senate)? _floorRollCall;
    private int _crisisIndex;
    private int _secondTermCrisis;
    private int _nextCrisis = 64;
    private int _orderExpires;
    private int _courtDate;
    private int _ruleDate;
    private int _dealDate;
    private string _tradePartner = "";
    private bool _tradeUsesLockedTerms;
    private bool _tradeIncludesDomesticSourcing;
    private bool _tradeRequested;
    private bool _orderIssued;
    private bool _ruleRequested;
    private bool _billResolved;
    private bool _cabinetExpert;
    private bool _cabinetChosen;
    private double _pressure = 2.1;
    private double _fiscalImpulse;
    private double _economicRecord;

    public const int RallyCost = 5;
    public const double RallyMarginBonus = 3.4;
    public const double ElectionDaySwing = 1.7;

    public static readonly DateTime Inauguration = new(2029, 1, 20);
    public static readonly DateTime ElectionDay = new(2032, 11, 2);
    public static readonly DateTime TermEnd = new(2033, 1, 20);
    public static readonly DateTime Midterms = new(2030, 11, 5);
    public OpeningCommitment Commitment { get; private set; }
    public OpeningOutcome CommitmentOutcome { get; private set; }
    public bool CanMakeCommitment => Term == 1 && _crisisIndex == 0 && CanEdit && Commitment == OpeningCommitment.None;
    public StoryChapter Chapter => CampaignStory.Chapters.Last(chapter => chapter.Start <= Date
        && (chapter.Start < TermEnd || Term == 2));
    public int Term { get; private set; } = 1;
    public DateTime CurrentElectionDay => Term == 1 ? ElectionDay : new DateTime(2036, 11, 4);
    public DateTime CurrentMidterms => Term == 1 ? Midterms : new DateTime(2034, 11, 7);
    public DateTime CurrentTermStart => Term == 1 ? Inauguration : TermEnd;
    public DateTime CurrentTermEnd => Term == 1 ? TermEnd : new DateTime(2037, 1, 20);
    public int? FirstElectionElectoralVotes { get; private set; }
    public bool CanBeginSecondTerm => !Removed && Term == 1 && Finished && Results.Count > 0 && ElectoralVotes >= 270;
    public DateTime Date => Inauguration.AddDays(Day);
    public int Day { get; private set; }
    public int FloorDay { get; private set; } = 21;
    public bool Finished => Removed || Date >= CurrentTermEnd;
    public int ElectoralVotes => Results.Where(result => result.Margin >= 0).Sum(result => result.State.Electors);
    public IReadOnlyList<StateResult> Results { get; private set; } = [];
    public int Capital { get; private set; } = 52;
    public double Approval { get; private set; } = 51;
    public double Inflation { get; private set; } = 3.2;
    public double Rate { get; private set; } = 4.5;
    public double Unemployment { get; private set; } = 4.3;
    public int LawsSigned { get; private set; }
    public int LawsVetoed { get; private set; }
    public int Losses { get; private set; }
    public bool MidtermResolved { get; private set; }
    public int MidtermShift { get; private set; }
    public bool OnDesk { get; private set; }
    public FloorVote? LastFloorVote { get; private set; }
    public bool Reconciliation { get; private set; }
    public int SenateTarget => Reconciliation ? 51 : 60;
    public int Escalation => Math.Clamp((int)Math.Ceiling(_pressure * 2), 1, 10);
    public int Deficit => (_measures.Contains(Measure.WorkerRelief) ? ActivePlan.ReliefCost : 0)
        + (_measures.Contains(Measure.ProductionCredits) ? ActivePlan.InvestmentCost : 0)
        - (_measures.Contains(Measure.DeficitOffset) ? ActivePlan.OffsetSavings : 0)
        + (_measures.Contains(Measure.DomesticSourcing) ? ActivePlan.MandateCost : 0);
    public Crisis Current => Episode?.Crisis ?? (Term == 2 && _crisisIndex >= _secondTermCrisis
        ? SecondTermCrises[(_crisisIndex - _secondTermCrisis) % SecondTermCrises.Length]
        : Crises[_crisisIndex % Crises.Length]);
    public IReadOnlyList<Briefing> Briefings => _briefings;
    public bool DecisionToday => _briefings.LastOrDefault(entry => entry.Decision)?.Date == Date;
    public bool StoryToday => _briefings.LastOrDefault(entry => entry.Story)?.Date == Date;
    public bool HasMeasure(Measure measure) => _measures.Contains(measure);
    public bool CanEdit => HasFloorVote && !_billResolved && !OnDesk && !Finished;
    public bool CanTrade => Allows(AgencyTools.Trade) && !_tradeRequested && _dealDate <= Day && !Finished;
    public bool CanOrder => Allows(AgencyTools.Order) && !_orderIssued && _orderExpires <= Day && !Finished;
    public bool CanRule => Allows(AgencyTools.Rule) && !_ruleRequested && _ruleDate <= Day && !Finished;
    public bool CanAppoint => !_cabinetChosen && !Finished;
    public DateTime? EmergencyOrderExpiry => _orderExpires > Day ? Inauguration.AddDays(_orderExpires) : null;
    public DateTime? PendingCourtReview => _courtDate > Day ? Inauguration.AddDays(_courtDate) : null;
    public DateTime? PendingAgencyReview => _ruleDate > Day ? Inauguration.AddDays(_ruleDate) : null;
    public string OrderStatus => _orderExpires > Day ? (_courtDate > Day ? "Active · under review" : "Active") : "Inactive";
    public string BillStatus => OnDesk ? (Finished ? "Bill left unsigned at the handover" : "Awaiting your signature")
        : !HasFloorVote ? "No floor vote scheduled"
        : _billResolved ? "Floor vote concluded" : $"Floor vote in {FloorDay - Day} days";
    public string? PendingTradePartner => _dealDate > Day ? _tradePartner : null;
    public bool LockedTradeTerms { get; private set; }
    public string Intelligence => PendingTradePartner is { } partner ? $"Response from {partner} expected in {_dealDate - Day} days."
        : $"Estimated recovery window: {Math.Max(5, Escalation * 5 - 6)}–{Escalation * 5 + 12} days. Moderate confidence.";
    public double EstimatedMargin(State state) => ElectionResult(state, 0).Margin;

    public ElectionOutlook ForecastFor(State state) => new(ElectionResult(state, 0), HasVisited(state.Code));

    private StateResult ElectionResult(State state, double variation) =>
        new(state, (Approval - 49) * .85, _economicRecord * state.Exposure * .46,
            HasVisited(state.Code) ? RallyMarginBonus : 0, variation, OppositionEffect(state), LocalClimate(state), AgendaReputation(state) + ConversationReputation(state));
    public bool HasVisited(string code) => _visited.Contains(code);
    public string OpeningRecord => CommitmentOutcome switch
    {
        OpeningOutcome.Kept => Commitment == OpeningCommitment.ProtectJobs
            ? "You kept the Michigan jobs pledge by signing worker relief."
            : "You kept your budget pledge by signing a bill scored at $130B or less.",
        OpeningOutcome.Broken => Commitment == OpeningCommitment.ProtectJobs
            ? "The jobs pledge went unfulfilled. The opening bill ended without signed worker relief."
            : "The budget pledge went unfulfilled. No opening bill at or below $130B became law.",
        OpeningOutcome.NoCommitment => "You went to the first floor vote without making a public pledge.",
        _ => Commitment == OpeningCommitment.ProtectJobs ? "Jobs pledge pending. Sign worker relief in the opening bill."
            : Commitment == OpeningCommitment.ControlCosts ? "Budget pledge pending. Sign an opening bill scored at $130B or less."
            : "A public promise is available until the opening floor vote."
    };

    public int Leverage(string code) => _deals.ContainsKey(code) ? 1 : 0;

    public Campaign(int seed = 2029, bool oversightEnabled = true, int? oversightSeed = null, bool agendaEnabled = true, bool variableWorld = false, bool conversationsEnabled = true, bool extendedStoryEnabled = true, bool scopedDefenseAuthorization = true, bool lockedTradeTerms = true, bool scopedWartimeStory = true)
    {
        _random = new Random(seed);
        InitializeEscalation(seed);
        if (agendaEnabled) _agendaStartIndex = 0;
        if (variableWorld) InitializeWorld(seed);
        ConversationsEnabled = conversationsEnabled;
        ExtendedStoryEnabled = extendedStoryEnabled;
        ScopedDefenseAuthorization = scopedDefenseAuthorization;
        LockedTradeTerms = lockedTradeTerms;
        ScopedWartimeStory = scopedWartimeStory;
        if (oversightEnabled) EnableOversight(oversightSeed ?? seed);
        foreach (var state in States.Where(state => state.HouseSeats > 0))
        {
            for (int index = 0; index < state.HouseSeats; index++)
                AddSeat(state, false);
            AddSeat(state, true);
            AddSeat(state, true);
        }
        RecordChapterOpening();
        AddBrief("Brian Banks took the oath of office. Semiconductor shipments are delayed and automakers warn of production cuts. Senate leadership has set a floor vote in 21 days.", true);
    }

    private void AddSeat(State state, bool senator)
    {
        bool government = _random.NextDouble() < Math.Clamp(.53 + state.Lean / 70, .12, .88);
        _seats.Add(new Seat(state.Code, senator, government, _random.NextDouble()));
    }

    public Vote[] WhipCount(bool senate)
    {
        return _seats.Where(seat => seat.Senator == senate)
            .Select(seat =>
            {
                var state = States.First(state => state.Code == seat.State);
                double baseline = seat.Government ? .78 : .28;
                baseline += (Approval - 51) * .009 + state.Exposure * .065;
                baseline += _deals.GetValueOrDefault(seat.State);
                baseline += _measures.Contains(Measure.WorkerRelief) ? .04 : -.02;
                baseline += _measures.Contains(Measure.ProductionCredits) ? state.Exposure * .035 : 0;
                baseline += _measures.Contains(Measure.DomesticSourcing) ? state.Exposure * .045 - .035 : 0;
                baseline -= Math.Max(0, Deficit - 100) * .00035;
                baseline += MidtermShift * .045;
                if (senate && Reconciliation)
                    baseline += seat.Government ? .10 : -.20;
                double margin = baseline - seat.Persuadability;
                return new Vote(seat, margin >= 0, Math.Abs(margin) < .10);
            }).ToArray();
    }

    public IReadOnlyList<Vote>? FloorRollCall(bool senate) => senate ? _floorRollCall?.Senate : _floorRollCall?.House;

    private IReadOnlyList<Vote> CaptureRollCall(bool senate) =>
        Array.AsReadOnly(WhipCount(senate).Select(vote => vote with { Swing = false }).ToArray());

    public bool MakeOpeningCommitment(OpeningCommitment commitment)
    {
        if (!CanMakeCommitment || commitment is not (OpeningCommitment.ProtectJobs or OpeningCommitment.ControlCosts))
            return false;
        if (commitment == OpeningCommitment.ProtectJobs && !Spend(5)) return false;
        Commitment = commitment;
        if (commitment == OpeningCommitment.ControlCosts) Capital = Math.Min(100, Capital + 4);
        AddBrief(commitment == OpeningCommitment.ProtectJobs
            ? "You promised Elena’s crew you would sign worker relief. The public appearance used 5 political capital."
            : "You pledged to keep the signed bill’s deficit score at $130B or less. Fiscal moderates offered 4 political capital.");
        return true;
    }

    private void ResolveOpeningCommitment(bool signed)
    {
        if (_crisisIndex != 0 || CommitmentOutcome != OpeningOutcome.Pending) return;
        if (Commitment == OpeningCommitment.None)
        {
            CommitmentOutcome = OpeningOutcome.NoCommitment;
            return;
        }

        bool kept = signed && (Commitment == OpeningCommitment.ProtectJobs
            ? HasMeasure(Measure.WorkerRelief) : Deficit <= 130);
        CommitmentOutcome = kept ? OpeningOutcome.Kept : OpeningOutcome.Broken;
        Approval = Math.Clamp(Approval + (kept ? 2 : -2), 15, 85);
        Capital = Math.Clamp(Capital + (kept ? 4 : -4), 0, 100);
        AddBrief(OpeningRecord + (kept ? " Approval rose 2 points and you gained 4 capital."
            : " Approval fell 2 points and you lost 4 capital."));
    }

    public void Toggle(Measure measure)
    {
        if (!CanEdit) return;
        if (!_measures.Remove(measure)) _measures.Add(measure);
        if (_measures.Contains(Measure.DomesticSourcing)) Reconciliation = false;
    }

    public bool ChangeTrack()
    {
        if (!CanEdit || _measures.Contains(Measure.DomesticSourcing)) return false;
        Reconciliation = !Reconciliation;
        return true;
    }

    public bool Deal(string state)
    {
        if (!CanEdit || _deals.ContainsKey(state) || !States.Any(entry => entry.Code == state) || !Spend(6)) return false;
        _deals[state] = .17;
        AddBrief($"Leadership reports stronger commitments from {state} after your funding pledge.");
        return true;
    }

    public bool BeginSecondTerm()
    {
        if (!CanBeginSecondTerm) return false;
        FirstElectionElectoralVotes = ElectoralVotes;
        Term = 2;
        _secondTermCrisis = _crisisIndex + 1;
        Results = [];
        _visited.Clear();
        InitializeOpposition();
        MidtermResolved = false;
        AddBrief("You took the oath for your second and final term. The economy, Congress, and unfinished business carry forward.", true);
        RecordChapterOpening();
        return true;
    }

    public bool Rally(string state)
    {
        if (Date >= CurrentElectionDay || !States.Any(entry => entry.Code == state) || _visited.Contains(state) || !Spend(RallyCost)) return false;
        _visited.Add(state);
        AddBrief(Term == 1 ? $"Your visit to {state} made the evening news. Local support ticked upward."
            : $"Your visit to {state} boosted local support for your party’s nominee.");
        return true;
    }

    public bool Appoint(bool expert)
    {
        if (!CanAppoint || !Spend(expert ? 7 : 0)) return false;
        _cabinetChosen = true;
        _cabinetExpert = expert;
        if (!expert) Capital = Math.Min(100, Capital + 5);
        AddBrief(expert ? "The Senate confirmed your technocratic nominee. Agency implementation will move faster."
            : "Your coalition welcomed a loyalist nominee. Agency review will take longer.");
        return true;
    }

    public bool Trade()
    {
        if (!CanTrade || !Spend(8)) return false;
        _tradeRequested = true;
        _tradePartner = Current.Ally;
        _tradeUsesLockedTerms = LockedTradeTerms;
        _tradeIncludesDomesticSourcing = HasMeasure(Measure.DomesticSourcing);
        _dealDate = Day + _random.Next(5, 13);
        AddBrief($"State opened temporary trade relief talks with {_tradePartner}.");
        return true;
    }

    public bool Order()
    {
        if (!CanOrder || !Spend(9)) return false;
        _orderIssued = true;
        _orderExpires = Day + 38;
        _courtDate = Day + _random.Next(3, 10);
        Approval = Math.Min(100, Approval + 1);
        AddBrief("Emergency relief began immediately. The Attorney General expects a legal challenge.");
        return true;
    }

    public bool Rule()
    {
        if (!CanRule || !Spend(4)) return false;
        _ruleRequested = true;
        _ruleDate = Day + (_cabinetExpert ? 20 : 42);
        AddBrief("Your agency opened a regulatory review. Relief begins only when the rule takes effect.");
        return true;
    }

    public bool ResolveBill(bool sign)
    {
        if (Finished || !OnDesk || (!sign && !Spend(5))) return false;
        OnDesk = false;
        if (sign)
        {
            LawsSigned++;
            Approval += 1.5;
            Capital = Math.Min(100, Capital + 9);
            double relief = Episode == null || _crisisIndex == 0 ? .52
                : (HasMeasure(Measure.WorkerRelief) ? .27 : 0) + (HasMeasure(Measure.ProductionCredits) ? .31 : 0)
                    + (HasMeasure(Measure.DomesticSourcing) ? .12 : 0);
            _pressure = Math.Max(.1, _pressure - relief);
            _fiscalImpulse += Deficit / 380.0;
        }
        else
        {
            LawsVetoed++;
            Approval -= .8;
        }
        AddBrief(sign ? $"You signed the {PackageName}. Treasury recorded a net ten-year deficit impact of ${Deficit}B."
            : $"You vetoed the {PackageName}. The provisions will not take effect.", true);
        ResolveOpeningCommitment(sign);
        RecordOpeningBillOutcome(sign ? OpeningBillOutcome.Signed : OpeningBillOutcome.Vetoed);
        if (sign && (Current.Title is "The Apprenticeship Hearing" or "The Skills Program Renewal")
            && (HasMeasure(Measure.WorkerRelief) || HasMeasure(Measure.ProductionCredits)))
            _trainingFunded = true;
        return true;
    }

    public int Advance(int days, bool stopAtStory = false)
    {
        if (days < 1 || OnDesk || Finished) return 0;
        int start = Day;
        while (Day - start < days && !Finished)
        {
            Day++;
            Tick();
            if (DecisionToday || (stopAtStory && StoryToday)) break;
        }
        return Day - start;
    }

    private void Tick()
    {
        if (Chapter.Start == Date)
            RecordChapterOpening();

        if (Day == _nextCrisis)
        {
            _crisisIndex++;
            _episodeOpened = Day;
            _nextCrisis += 76 + _random.Next(18) + (Episode?.CadenceOffset ?? 0);
            _pressure += Current.Severity - PriorDecisionRelief();
            _tradeRequested = false;
            _orderIssued = false;
            _ruleRequested = false;
            if (!OnDesk)
            {
                _billResolved = !HasFloorVote;
                Reconciliation = false;
                LastFloorVote = null;
                _floorRollCall = null;
                FloorDay = HasFloorVote ? Day + ActivePlan.FloorLeadDays : -1;
                _deals.Clear();
                if (Episode?.Bill != null)
                {
                    _measures.Clear();
                    _measures.Add(Measure.WorkerRelief);
                    _measures.Add(Measure.ProductionCredits);
                }
            }
            bool renewedCrisis = Term == 1 || _crisisIndex < _secondTermCrisis
                ? _crisisIndex >= Crises.Length : _crisisIndex - _secondTermCrisis >= SecondTermCrises.Length;
            string nextStep = HasFloorVote ? $" Leadership scheduled the {PackageName} for a vote in {ActivePlan.FloorLeadDays} days."
                : " An Oval Office decision is required within 18 days to intervene.";
            AddBrief((Episode == null && renewedCrisis ? "The pressure has returned. " : "") + Current.Brief + nextStep, true);
            if (AgendaCallback is { } callback) AddStory($"{Current.Title} · Earlier decisions", callback);
            RecordEpisodeScene();
            if (Current.Title is "The War at the Aster Strait" or "The Aster Strait Ceasefire")
                OpenDefenseWindow(Current.Title);
        }
        if (Day == _courtDate)
        {
            if (_random.NextDouble() < .45)
            {
                _orderExpires = 0;
                Approval -= 1.2;
                AddBrief("A federal judge stayed the relief order. Its economic effect ends today.", true);
            }
            else AddBrief("A federal court declined to stay the relief order. The policy remains active.", true);
        }
        if (Day == _orderExpires)
            AddBrief("The emergency relief order expired. Its economic effect ends today.");
        if (Day == _dealDate && VariableWorld)
            ResolveTradeTalks();
        else if (Day == _dealDate)
        {
            bool agreed = _random.NextDouble() < .74;
            if (agreed)
            {
                _pressure = Math.Max(.1, _pressure - .85);
                Approval += 1.1;
            }
            AddBrief(agreed ? $"{_tradePartner} accepted temporary trade relief. Price pressure eased."
                : $"{_tradePartner} declined the offer under domestic pressure. Import conditions remain tight.", true);
        }
        if (Day == _ruleDate)
        {
            _pressure = Math.Max(.1, _pressure - .42);
            AddBrief("Agency review concluded and the revised rule took effect.", true);
        }
        TickAgenda();
        if (Day == FloorDay)
        {
            var houseVotes = CaptureRollCall(senate: false);
            var senateVotes = CaptureRollCall(senate: true);
            _floorRollCall = (houseVotes, senateVotes);
            int house = houseVotes.Count(vote => vote.Yes);
            int senate = senateVotes.Count(vote => vote.Yes);
            LastFloorVote = new FloorVote(house, senate, SenateTarget);
            OnDesk = house >= 218 && senate >= SenateTarget && (_measures.Contains(Measure.WorkerRelief) || _measures.Contains(Measure.ProductionCredits) || _measures.Contains(Measure.DomesticSourcing));
            _billResolved = true;
            if (OnDesk)
            {
                string nextStep = Finished ? "It was left unsigned at the handover." : "It awaits your decision.";
                AddBrief($"The bill passed {house}–{435 - house} in the House and {senate}–{100 - senate} in the Senate. {nextStep}", true);
            }
            else
            {
                Losses++;
                Approval -= .8;
                Capital = Math.Max(0, Capital - 3);
                AddBrief($"The package stalled. House support was {house}/218 and Senate support was {senate}/{SenateTarget}.", true);
                ResolveOpeningCommitment(false);
                RecordOpeningBillOutcome(OpeningBillOutcome.Stalled);
            }
        }

        double orderRelief = _orderExpires > Day ? .45 : 0;
        _pressure *= .988;
        _fiscalImpulse *= .997;
        Inflation += (2.05 + _pressure * .84 + _fiscalImpulse - orderRelief - Inflation) * .045;
        Unemployment += (4.0 + Math.Max(0, Rate - 3.25) * .42 + _pressure * .38 - Unemployment) * .026;
        if (Day % 42 == 0)
        {
            double oldRate = Rate;
            if (Inflation > 2.8) Rate = Math.Min(8, Rate + .25);
            else if (Inflation < 2.35 || Unemployment > 5.5) Rate = Math.Max(1, Rate - .25);
            if (Rate != oldRate)
                AddBrief($"The independent Fed {(Rate > oldRate ? "raised" : "lowered")} its policy rate to {Rate:F2}%.");
        }
        double economy = (2.45 - Inflation) * 2.4 + (4.5 - Unemployment) * 2.2 - (Rate - 3.5) * .8;
        _economicRecord += (economy - _economicRecord) * .008;
        double governingRecord = _agendaDeliveries == 0 ? Math.Min(LawsSigned, 12)
            : Math.Min(LawsSigned + _agendaDeliveries * .82, 13);
        double mood = 49 + economy + governingRecord * 1.03 - Losses * .18;
        Approval = Math.Clamp(Approval + (mood - Approval) * .011, 15, 85);
        if (Day % 12 == 0) Capital = Math.Min(100, Capital + (Approval >= 50 ? 6 : 4));
        if (Date == CurrentMidterms)
        {
            MidtermResolved = true;
            MidtermShift = Approval >= 50 ? 1 : -1;
            Capital = Math.Clamp(Capital + (Approval >= 50 ? 12 : -12), 0, 100);
            AddBrief(Approval >= 50 ? "Midterms strengthened your coalition. Whips report more reliable floor support."
                : "Midterm losses weakened your coalition. Whips expect a tougher second half of the term.", true);
            RecordMidtermScene();
        }
        if (Date == CurrentElectionDay)
        {
            Results = States.Select(state => ElectionResult(state, (_random.NextDouble() - .5) * (2 * ElectionDaySwing))).ToArray();
            AddBrief(Term == 1 ? "Polls have closed. Follow the state calls and the race to 270 at the Decision Desk."
                : "Polls have closed in the race to succeed you. Follow the results at the Decision Desk.", true);
            AddStory("Election night in Michigan", ElectionNightScene, requiresElectionCall: true);
        }
        if (Date == CurrentElectionDay.AddDays(21) && Results.Count > 0)
            AddStory("The first transition meeting", TransitionScene);
        TickOpposition();
        TickDefense();
        TickEscalation();
        TickConversations();
        TickOversight();
        if (Finished && !Removed)
        {
            AddBrief(CanBeginSecondTerm ? "Your first term is complete. You may now take the oath for your final term."
                : Term == 2 ? "Your second term is complete. Your presidency ends at the constitutional two-term limit."
                : "Your term is complete. The transition staff take charge of the outstanding work.", true);
            RecordEpilogue();
        }
    }

    private bool Spend(int amount)
    {
        if (Finished || Capital < amount) return false;
        Capital -= amount;
        return true;
    }

    private void AddBrief(string text, bool decision = false) => _briefings.Add(new Briefing(Date, text, decision));

    private static readonly Crisis[] Crises =
    [
        new("The Chip Shock", "A semiconductor shortage threatens auto production across the Midwest.", "Taiwan", 2.1),
        new("Energy Squeeze", "A disruption in global energy markets is driving up diesel and heating bills.", "Canada", 1.45),
        new("Harvest Failure", "Poor harvests abroad are tightening grain inventories and raising grocery prices.", "Brazil", 1.15),
        new("Minerals Bottleneck", "Battery makers warn that critical mineral inventories are running low.", "Australia", 1.55),
        new("Port Backlog", "Congestion at major ports is delaying parts for businesses and raising freight rates.", "Mexico", 1.25),
        new("Credit Tightening", "Global lenders have tightened credit, putting stressed manufacturers at risk.", "European Union", 1.35),
        new("Grid Supply Crunch", "Utilities are competing for scarce transformers as maintenance delays mount.", "Japan", 1.4),
        new("Factory Slowdown", "Export demand has softened and industrial employers are delaying investments.", "South Korea", 1.25)
    ];

    private static readonly Crisis[] SecondTermCrises =
    [
        new("Medicine Shortage", "Drugmakers warn that delays in active ingredient shipments are reducing generic medicine inventories.", "India", 1.5),
        new("Transit Equipment Crunch", "City transit agencies are delaying upgrades amid a shortage of replacement components.", "Germany", 1.2),
        new("Fertilizer Price Surge", "Growers face higher input prices ahead of planting season, with downstream risks for grocery bills.", "Canada", 1.55),
        new("Battery Materials Dispute", "New export rules are slowing shipments of battery materials to domestic plants.", "Chile", 1.7),
        new("Commercial Aircraft Backlog", "Maintenance suppliers report long lead times while airlines postpone fleet expansions.", "France", 1.25),
        new("Pacific Freight Disruption", "Freight schedules are slipping across the Pacific, leaving retailers and factories short of inventory.", "Singapore", 1.5)
    ];

    // Electoral apportionment and House delegation sizes based on the 2020 Census.
    public static IReadOnlyList<State> States { get; } = Array.AsReadOnly<State>(
    [
        new("WA",12,10,15,.7,0,0),new("OR",8,6,13,.65,0,1),new("CA",54,52,22,.8,0,2),
        new("AK",3,1,-16,.8,0,5),new("HI",4,2,26,.7,1,5),new("NV",6,4,-1,.8,1,2),
        new("ID",4,2,-28,.6,1,1),new("MT",4,2,-18,.65,2,0),new("WY",3,1,-36,.55,2,1),
        new("UT",6,4,-21,.65,2,2),new("AZ",11,9,-2,.9,2,3),new("CO",10,8,11,.7,3,2),
        new("NM",5,3,6,.75,3,3),new("ND",3,1,-31,.6,3,0),new("SD",3,1,-28,.65,3,1),
        new("NE",5,3,-18,.65,4,1),new("KS",6,4,-15,.7,4,2),new("OK",7,5,-28,.75,4,3),
        new("TX",40,38,-12,.85,4,4),new("MN",10,8,4,.8,4,0),new("IA",6,4,-11,.8,5,1),
        new("MO",10,8,-17,.75,5,2),new("AR",6,4,-23,.65,5,3),new("LA",8,6,-18,.85,5,4),
        new("WI",10,8,0,1.2,5,0),new("IL",19,17,11,.95,6,1),new("MI",15,13,1,1.4,7,0),
        new("IN",11,9,-16,1.2,7,1),new("OH",17,15,-7,1.35,8,1),new("KY",8,6,-22,.8,6,2),
        new("TN",11,9,-20,.92,6,3),new("MS",6,4,-17,.7,6,4),new("AL",9,7,-20,.85,7,4),
        new("GA",16,14,-1,1.05,8,4),new("FL",30,28,-9,.85,9,5),new("SC",9,7,-13,.8,9,4),
        new("NC",16,14,-2,1,9,3),new("VA",13,11,5,.8,9,2),new("WV",4,2,-34,.66,8,2),
        new("PA",19,17,0,1.25,9,1),new("NY",28,26,13,.8,9,0),new("VT",3,1,26,.6,10,0),
        new("NH",4,2,3,.7,11,0),new("ME",4,2,6,.67,12,0),new("MA",11,9,25,.68,11,1),
        new("RI",4,2,16,.72,12,1),new("CT",7,5,15,.78,11,2),new("NJ",14,12,7,.95,10,1),
        new("DE",3,1,13,.73,11,3),new("MD",10,8,23,.78,10,2),new("DC",3,0,72,.45,10,3)
    ]);
}
