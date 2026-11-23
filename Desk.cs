#nullable enable annotations
using System.Text.Json;
using Godot;
using GeopoliticsSim.Core;
using System.Threading.Tasks;

public partial class Desk : Control
{
    private static readonly Color Ink = new("#ecf1fa");
    private static readonly Color Muted = new("#a0b0c9");
    private static readonly Color Gold = new("#edbd72");
    private static readonly Color Blue = new("#549ee7");
    private static readonly Color Red = new("#dc777d");
    private static readonly Color Slate = new("#202f46");
    private Campaign _campaign;
    private VBoxContainer _root;
    private ScrollContainer _scroll;
    private string _renderedPage = "";
    private string _page = "Oval Office";
    private string _state = "MI";
    private string _greatPower = "Russia";
    private string _message = "";
    private bool _senate = true;
    private int _archiveYear;
    private bool _storyArchive = true;
    private bool _running;
    private bool _simulationFault;
    private bool _primarySaveValid;
    private int _speed;
    private int _calls;
    private int _seed = Random.Shared.Next();
    private int _saveVersion = 5;
    private int _oversightSeed = Random.Shared.Next();
    private const string SaveFile = "user://campaign.json";
    private readonly List<Command> _commands = [];
    private record Command(string Kind, int Number = 0, string Code = "");
    private record SaveData(int Version, int Seed, int Calls, List<Command> Commands, int? OversightSeed = null,
        string? Page = null, string? State = null, string? GreatPower = null, int ArchiveYear = 0, bool StoryArchive = true);
    private double _elapsed;

    public override void _Ready()
    {
        var margin = new MarginContainer();
        AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, side is "left" or "right" ? 42 : 24);
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        margin.AddChild(_scroll);
        _root = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _root.AddThemeConstantOverride("separation", 12);
        _scroll.AddChild(_root);
        _campaign = new Campaign(_seed, oversightSeed: _oversightSeed, variableWorld: true);
        Restore();
        Render();
    }

    public override void _Process(double delta)
    {
        if (_speed == 0 || _running) return;
        _elapsed += delta;
        if (_elapsed < .65) return;
        _elapsed = 0;
        Advance(_speed);
    }

    private async void Advance(int days)
    {
        if (_running || _simulationFault || _campaign.Finished || _campaign.OnDesk || ElectionCallsPending) return;
        string priorEpisode = _campaign.Current.Title;
        var priorConversation = _campaign.PendingConversation?.Id;
        _running = true;
        _message = "";
        Render();
        int count;
        try
        {
            count = await Task.Run(() => _campaign.Advance(days, stopAtStory: true));
        }
        catch (Exception error)
        {
            _running = false;
            _speed = 0;
            GD.PushError($"Campaign advancement interrupted: {error.Message}");
            try
            {
                _campaign = Replay(CaptureSave());
                _message = "The calendar was interrupted. Your last completed day and decisions were restored.";
            }
            catch (Exception replayError)
            {
                GD.PushError($"Campaign recovery interrupted: {replayError.Message}");
                _simulationFault = true;
            }
            if (IsInstanceValid(this) && IsInsideTree()) Render();
            return;
        }
        _running = false;
        if (!IsInstanceValid(this) || !IsInsideTree()) return;
        if (count > 0) _commands.Add(new Command("advance", count));
        if (count < days || _campaign.DecisionToday || _campaign.StoryToday)
            _speed = 0;
        if (_campaign.Removed || (_campaign.Results.Count > 0 && _calls == 0 && _campaign.Date == _campaign.CurrentElectionDay))
            _page = "Decision Desk";
        else if (_campaign.OnDesk || _campaign.Date == _campaign.CurrentMidterms || _campaign.PendingOffer != null
            || (_campaign.CanChooseAgenda && _campaign.Current.Title != priorEpisode)
            || (_campaign.PendingConversation is { } conversation && conversation.Id != priorConversation))
            _page = "Oval Office";
        if (count > 0) Save();
        Render();
    }

    private void Render()
    {
        if (_renderedPage != _page)
        {
            _scroll.ScrollVertical = 0;
            _renderedPage = _page;
        }
        foreach (Node child in _root.GetChildren())
        {
            _root.RemoveChild(child);
            child.QueueFree();
        }

        // The simulation has exclusive ownership until its daily batch completes.
        if (_running)
        {
            Text(_root, "THE MANDATE", 36, Ink);
            Text(_root, "Advancing the presidential calendar…", 19, Gold);
            return;
        }

        if (_simulationFault)
        {
            Text(_root, "Campaign paused", 34, Ink);
            Text(_root, "The calendar could not recover in this session. Close and reopen the game to try the last autosave, or start a new presidency.", 17, Muted);
            Button(_root, "Start a new presidency", ConfirmNewPresidency);
            return;
        }

        var headline = Row(_root);
        var brand = Column(headline);
        Text(brand, "THE MANDATE", 32, Ink);
        Text(brand, "PRESIDENT BRIAN BANKS", 13, Gold);
        brand.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var date = Column(headline);
        Text(date, _campaign.Date.ToString("dddd, MMMM d, yyyy"), 20, Ink);
        var termDay = (_campaign.Date - _campaign.CurrentTermStart).Days + 1;
        var termLength = (_campaign.CurrentTermEnd - _campaign.CurrentTermStart).Days;
        Text(date, _campaign.Removed ? "REMOVED FROM OFFICE" : _campaign.Finished ? $"TERM {_campaign.Term} COMPLETE" : $"TERM {_campaign.Term}  /  DAY {termDay:N0} OF {termLength:N0}", 13, Gold);
        var metrics = Row(_root);
        Metric(metrics, "APPROVAL", $"{_campaign.Approval:F1}%");
        Metric(metrics, "POLITICAL CAPITAL", $"{_campaign.Capital}");
        Metric(metrics, "INFLATION", $"{_campaign.Inflation:F1}%");
        Metric(metrics, "FED RATE", $"{_campaign.Rate:F2}%");
        Metric(metrics, "UNEMPLOYMENT", $"{_campaign.Unemployment:F1}%");

        var nav = Row(_root);
        foreach (string page in new[] { "Oval Office", "Capitol Hill", "Situation Room", "Decision Desk", "Campaign Record" })
            Button(nav, page, () => { _page = page; _message = ""; Render(); }, page == _page);
        Control spacer = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        nav.AddChild(spacer);
        bool paused = _campaign.OnDesk || _campaign.Finished || ElectionCallsPending;
        Button(nav, _speed == 0 ? "Play" : "Pause", () => { _speed = _speed == 0 ? 1 : 0; Render(); }, paused);
        Button(nav, "+1 day", () => Advance(1), paused);
        Button(nav, "+7 days", () => Advance(7), paused);
        Button(nav, "Next decision", () => Advance(120), paused);

        if (_campaign.WorldWarActive)
            Text(_root, "WORLD WAR III · Civilian relief and ceasefire diplomacy are available at the Great Powers desk.", 16, Gold);
        if (_message != "") Text(_root, _message, 15, Gold);
        if (ElectionCallsPending && _page != "Decision Desk")
            Text(_root, "The calendar is paused while the Decision Desk calls the election.", 16, Gold);
        if (_campaign.Finished && _page != "Decision Desk")
            Text(_root, "Visit the Decision Desk to review your record and close the term.", 16, Gold);
        if (_campaign.OnDesk && !_campaign.Finished && _page != "Oval Office")
            Text(_root, "The calendar is paused. Return to the Oval Office to sign or veto the bill.", 16, Gold);
        if (!_campaign.Finished && _page is not ("Oval Office" or "Campaign Record" or "Decision Desk")
            && VisibleBriefings().LastOrDefault(entry => entry.Story && entry.Date == _campaign.Date) is { } newScene)
        {
            var notice = Row(_root);
            Text(notice, $"New story scene · {newScene.SceneTitle}", 16, Gold);
            Button(notice, "Read in the Oval Office", () => { _page = "Oval Office"; Render(); });
        }
        switch (_page)
        {
            case "Capitol Hill": Hill(); break;
            case "Situation Room": Situation(); break;
            case "Pentagon": Pentagon(); break;
            case "Great Powers": GreatPowerDesk(); break;
            case "Decision Desk": Election(); break;
            case "Campaign Record": Archive(); break;
            default: Oval(); break;
        }
    }

    private void Oval()
    {
        var layout = Row(_root);
        var briefing = Card(layout, 2);
        Text(briefing, "PRESIDENT’S DAILY BRIEF", 13, Gold);
        Text(briefing, _campaign.Current.Title, 35, Ink);
        Text(briefing, _campaign.Current.Brief, 18, Ink);
        if (_campaign.EpisodeMemo is { } memo)
            Text(briefing, memo, 16, Muted);
        if (_campaign.AgendaCallback is { } callback)
            Text(briefing, callback, 16, Gold);
        if (_campaign.Episode == null || _campaign.Episode.Tools.HasFlag(AgencyTools.Trade))
            Text(briefing, _campaign.Intelligence, 15, Muted);
        Rule(briefing);
        var chapter = _campaign.Chapter;
        Text(briefing, chapter.Title, 23, Ink);
        if ((_campaign.Date - chapter.Start).Days < 45)
        {
            Text(briefing, chapter.Speaker, 13, Gold);
            Text(briefing, _campaign.ChapterScene, 17, Ink);
        }
        Text(briefing, chapter.Objective, 15, Muted);
        var recentScenes = VisibleBriefings().Where(entry => entry.Story && entry.SceneTitle != chapter.Title
            && entry.SceneTitle != _campaign.PendingConversation?.Title && (_campaign.Date - entry.Date).Days < 15);
        foreach (var scene in recentScenes.TakeLast(2))
        {
            Rule(briefing);
            Text(briefing, scene.SceneTitle!, 20, Ink);
            Text(briefing, scene.Text, 16, Ink);
        }
        Rule(briefing);
        Text(briefing, "White House log", 21, Ink);
        foreach (var entry in VisibleBriefings().Where(entry => !entry.Story).TakeLast(5).Reverse())
            Text(briefing, $"{entry.Date:MMM d}   {entry.Text}", 15, entry.Decision ? Ink : Muted);

        var desk = Card(layout);
        OversightPanel(desk);
        ConversationPanel(desk);
        if (_campaign.Episode?.First == null && _campaign.PendingAgendaFollowUp is { } followUp && !_campaign.Finished)
            Text(desk, $"Earlier policy follow-up expected {followUp:MMMM d}.", 16, Muted);
        AgendaPanel(desk);
        Text(desk, "RESOLUTE DESK", 13, Gold);
        Text(desk, _campaign.BillStatus, 23, Ink);
        if (_campaign.OnDesk && !_campaign.Finished)
        {
            if (_campaign.LastFloorVote is { } vote)
                Text(desk, $"Passed the House {vote.House}–{435 - vote.House} and Senate {vote.Senate}–{100 - vote.Senate}.", 15, Ink);
            Text(desk, $"Treasury estimates {(_campaign.Deficit >= 0 ? "+" : "−")}${Math.Abs(_campaign.Deficit)}B over ten years. New spending can raise price pressure.", 15, Muted);
            Action(desk, "Sign into law", () => Perform(new Command("sign")));
            Action(desk, "Veto · 5 capital", () => Perform(new Command("veto")), _campaign.Capital < 5);
        }
        if (_campaign.CanMakeCommitment)
        {
            Rule(desk);
            Text(desk, "What will you promise the country?", 19, Ink);
            Action(desk, "Pledge worker relief · −5 capital", () => Perform(new Command("pledge-jobs")), _campaign.Capital < 5);
            Action(desk, "Pledge a $130B cap · +4 capital", () => Perform(new Command("pledge-budget")));
            Text(desk, "A kept pledge earns 2 approval and 4 capital. A broken pledge costs the same. You can leave both offers on the table.", 14, Muted);
        }
        else if (_campaign.Commitment != OpeningCommitment.None && _campaign.Day < 64)
            Text(desk, _campaign.OpeningRecord, 15, Gold);
        Rule(desk);
        if (_campaign.Episode == null || _campaign.Episode.Tools.HasFlag(AgencyTools.Order))
        {
            Text(desk, $"Attorney General  /  {_campaign.OrderStatus}", 15, Muted);
            Action(desk, "Issue emergency order · 9", () => Perform(new Command("order")), !_campaign.CanOrder || _campaign.Capital < 9);
        }
        if (_campaign.Episode == null || _campaign.Episode.Tools.HasFlag(AgencyTools.Rule))
        {
            Text(desk, "Agency review depends on your cabinet appointment.", 15, Muted);
            Action(desk, "Start agency rulemaking · 4", () => Perform(new Command("rule")), !_campaign.CanRule || _campaign.Capital < 4);
        }
        Action(desk, "Appoint policy expert · 7", () => Perform(new Command("expert")), !_campaign.CanAppoint || _campaign.Capital < 7);
        Action(desk, "Appoint party loyalist · +5", () => Perform(new Command("loyalist")), !_campaign.CanAppoint);
        Rule(desk);
        Text(desk, $"{_campaign.LawsSigned} laws signed  /  {_campaign.LawsVetoed} vetoes  /  {_campaign.Losses} stalled votes", 15, Muted);
    }

    private void ConversationPanel(VBoxContainer desk)
    {
        if (_campaign.PendingConversation is not { } conversation) return;
        Text(desk, conversation.Speaker, 13, Gold);
        Text(desk, conversation.Title, 25, Ink);
        Text(desk, conversation.Scene, 16, Ink);
        Text(desk, $"Reply by {conversation.Deadline:MMMM d}.", 14, Muted);
        for (int index = 0; index < conversation.Options.Count; index++)
        {
            var option = conversation.Options[index];
            string capital = option.CapitalChange > 0 ? $"+{option.CapitalChange}" : $"−{-option.CapitalChange}";
            int choice = index;
            Action(desk, $"{option.Label} · {capital} capital",
                () => Perform(new Command("story-choice", choice, conversation.Id.ToString())),
                _campaign.Capital < -option.CapitalChange);
            Text(desk, option.Stakes, 14, Muted);
        }
        Rule(desk);
    }

    private void AgendaPanel(VBoxContainer desk)
    {
        if (_campaign.Episode is not { First: { } first, Second: { } second } || _campaign.Finished) return;
        Text(desk, "PRESIDENTIAL DECISION", 13, Gold);
        Text(desk, _campaign.AgendaStatus, 15, Muted);
        if (_campaign.CanChooseAgenda)
        {
            Action(desk, $"{first.Label} · {first.CapitalCost} capital", () => Perform(new Command("agenda", 1)), _campaign.Capital < first.CapitalCost);
            Text(desk, $"Expected follow-up in {first.Days} days", 14, Muted);
            Action(desk, $"{second.Label} · {second.CapitalCost} capital", () => Perform(new Command("agenda", 2)), _campaign.Capital < second.CapitalCost);
            Text(desk, $"Expected follow-up in {second.Days} days", 14, Muted);
            if (_campaign.HardAgendaOption is { } hard)
            {
                Action(desk, $"{hard.Label} · +{hard.CapitalGain} capital", () => Perform(new Command("agenda", 3)));
                Text(desk, $"Expected follow-up in {hard.Days} days", 14, Muted);
            }
        }
        Rule(desk);
    }

    private void OversightPanel(VBoxContainer desk)
    {
        if (_campaign.PendingOffer is { } offer)
        {
            Text(desk, "CONFIDENTIAL · WHITE HOUSE COUNSEL", 13, Gold);
            Text(desk, offer.Source, 20, Ink);
            Text(desk, offer.Request, 15, Ink);
            Text(desk, $"Offer expires {offer.Deadline:MMM d}. Taking it may lead to a leak, hearings, or impeachment.", 14, Muted);
            Action(desk, "Refuse the offer", () => Perform(new Command("refuse-offer")));
            Action(desk, "Report to White House Counsel", () => Perform(new Command("report-offer")));
            Action(desk, $"Accept improper offer · +{offer.Capital} capital", () => Perform(new Command("accept-offer")));
            Rule(desk);
        }
        else if (_campaign.Oversight is OversightStage.Hearings or OversightStage.SenateTrial)
        {
            Text(desk, _campaign.Oversight == OversightStage.Hearings ? "HOUSE IMPEACHMENT HEARINGS" : "SENATE IMPEACHMENT TRIAL", 15, Gold);
            Text(desk, _campaign.Oversight == OversightStage.Hearings
                ? "A majority in the House can impeach. The presidency continues as Congress considers the article."
                : "President Banks remains in office during the trial. With 100 senators present, conviction takes 67 votes.", 15, Muted);
            Rule(desk);
        }
    }

    private void Hill()
    {
        var layout = Row(_root);
        var chamber = Card(layout, 2);
        if (!_campaign.HasFloorVote)
        {
            Text(chamber, "CAPITOL HILL", 13, Gold);
            Text(chamber, "No floor vote scheduled", 28, Ink);
            Text(chamber, "Leadership is waiting for a new package. The current issue is being handled by the White House.", 17, Muted);
            Button(chamber, "Return to the Oval Office", () => { _page = "Oval Office"; Render(); });
            return;
        }

        var votes = _campaign.WhipCount(_senate);
        int yes = votes.Count(vote => vote.Yes);
        int target = _senate ? _campaign.SenateTarget : 218;
        Text(chamber, "CAPITOL WHIP COUNT", 13, Gold);
        Text(chamber, $"{yes} / {target} votes needed", 32, Ink);
        Text(chamber, "Blue · yes     Red · no     Gold · swing", 15, Muted);
        var tabs = Row(chamber);
        Button(tabs, "Senate", () => { _senate = true; Render(); }, _senate);
        Button(tabs, "House", () => { _senate = false; Render(); }, !_senate);
        var seats = new SeatChart();
        seats.CustomMinimumSize = new Vector2(0, 320);
        seats.Votes = votes;
        seats.Selected = _state;
        seats.OnSeat = code => { _state = code; Render(); };
        chamber.AddChild(seats);
        var delegation = votes.Where(vote => vote.Seat.State == _state).ToArray();
        Text(chamber, $"{_state} delegation selected", 18, Ink);
        Text(chamber, delegation.Length == 0 ? "No voting members in this chamber."
            : $"{delegation.Count(vote => vote.Yes)} of {delegation.Length} leaning yes · {delegation.Count(vote => vote.Swing)} persuadable", 15, Muted);
        Action(chamber, $"Offer {_state} funding pledge · 6", () => Perform(new Command("pledge", Code: _state)),
            delegation.Length == 0 || !_campaign.CanEdit || _campaign.Capital < 6 || _campaign.Leverage(_state) > 0);

        var bill = Card(layout);
        Text(bill, "LEGISLATIVE PACKAGE", 13, Gold);
        Text(bill, _campaign.PackageName, 26, Ink);
        Text(bill, $"10-year deficit impact  {(_campaign.Deficit >= 0 ? "+" : "−")}${Math.Abs(_campaign.Deficit)}B", 17, Ink);
        Text(bill, _campaign.BillStatus, 15, Muted);
        if (_campaign.LastFloorVote is { } vote)
            Text(bill, $"Final roll call · House {vote.House}/218 · Senate {vote.Senate}/{vote.SenateTarget}", 15, Ink);
        if (_campaign.CommitmentOutcome == OpeningOutcome.Pending && _campaign.Commitment != OpeningCommitment.None)
            Text(bill, _campaign.OpeningRecord, 15, Gold);
        Rule(bill);
        Provision(bill, Measure.WorkerRelief, _campaign.ProvisionName(Measure.WorkerRelief));
        Provision(bill, Measure.ProductionCredits, _campaign.ProvisionName(Measure.ProductionCredits));
        Provision(bill, Measure.DeficitOffset, _campaign.ProvisionName(Measure.DeficitOffset));
        Provision(bill, Measure.DomesticSourcing, _campaign.ProvisionName(Measure.DomesticSourcing));
        Rule(bill);
        Text(bill, _campaign.Reconciliation ? "Budget reconciliation · 51 Senate votes" : "Regular order · 60 Senate votes", 15, Ink);
        Action(bill, "Switch legislative track", () => Perform(new Command("track")),
            !_campaign.CanEdit || _campaign.HasMeasure(Measure.DomesticSourcing));
        if (_campaign.HasMeasure(Measure.DomesticSourcing))
            Text(bill, "The mandate is ineligible for reconciliation.", 15, Muted);
    }

    private void Situation()
    {
        var layout = Row(_root);
        var intelligence = Card(layout, 2);
        Text(intelligence, "SITUATION ROOM", 13, Gold);
        Text(intelligence, _campaign.Current.Title, 32, Ink);
        Text(intelligence, _campaign.Current.Brief, 18, Ink);
        Rule(intelligence);
        Text(intelligence, "Supply disruption", 20, Ink);
        var rungs = Row(intelligence);
        for (int rung = 1; rung <= 10; rung++)
            Text(rungs, $" {rung} ", 19, rung <= _campaign.Escalation ? Gold : Muted);
        Text(intelligence, $"Rung {_campaign.Escalation} of 10   /   {_campaign.Intelligence}", 16, Muted);
        Rule(intelligence);
        foreach (var briefing in VisibleBriefings().Where(entry => !entry.Story).TakeLast(4).Reverse())
            Text(intelligence, $"{briefing.Date:MMM d}   {briefing.Text}", 15, Muted);
        var diplomacy = Card(layout);
        if (_campaign.Episode == null || _campaign.Episode.Tools.HasFlag(AgencyTools.Trade))
        {
            Text(diplomacy, "DIPLOMATIC CHANNEL", 13, Gold);
            Text(diplomacy, _campaign.Current.Ally, 26, Ink);
            Text(diplomacy, "A temporary import agreement can ease the shortage. Your counterpart can decline under domestic pressure.", 17, Muted);
            if (_campaign.TradeReadout is { } readout)
                Text(diplomacy, readout, 16, Ink);
            Action(diplomacy, "Open trade talks · 8", () => Perform(new Command("trade")), !_campaign.CanTrade || _campaign.Capital < 8);
            Rule(diplomacy);
        }
        else if (_campaign.Episode is { First: not null })
        {
            Text(diplomacy, "WHITE HOUSE COORDINATION", 13, Gold);
            Text(diplomacy, _campaign.EpisodeMemo ?? "", 16, Ink);
            Button(diplomacy, "Review the Oval Office decision", () => { _page = "Oval Office"; Render(); });
            Rule(diplomacy);
        }
        Text(diplomacy, "The independent Fed reviews conditions every 42 days. Its rate decisions feed back into employment and approval.", 16, Muted);
        Rule(diplomacy);
        Button(diplomacy, "Open Pentagon briefing", () => { _page = "Pentagon"; Render(); });
        Button(diplomacy, "Open Great Powers desk", () => { _page = "Great Powers"; Render(); });
    }

    private void Pentagon()
    {
        var row = Row(_root);
        var chiefs = Card(row, 2);
        Text(chiefs, "JOINT CHIEFS BRIEFING", 13, Gold);
        Text(chiefs, "Pentagon", 34, Ink);
        Text(chiefs, $"Force readiness {_campaign.Readiness}/100", 24, Ink);
        Text(chiefs, _campaign.JointChiefsAssessment, 17, Muted);
        Rule(chiefs);
        string mission = _campaign.Mission switch
        {
            DefenseMission.CivilianSupport => "Civilian support mission",
            DefenseMission.ProtectivePresence => "Carrier group on protective assignment",
            DefenseMission.Withdrawal => "Withdrawal underway",
            _ => "No active commitment"
        };
        Text(chiefs, mission, 22, Ink);
        if (_campaign.Mission != DefenseMission.None)
            Text(chiefs, _campaign.DefenseContext, 16, Gold);
        if (_campaign.MissionReviewDate is { } review)
            Text(chiefs, $"Next mission review or completion · {review:MMMM d, yyyy}", 16, Muted);
        if (_campaign.WarPowersDaysRemaining is int remaining)
            Text(chiefs, $"War Powers clock · {remaining} days remaining", 20, Gold);
        if (!_campaign.DefenseWindowOpen && _campaign.Mission == DefenseMission.None)
            Text(chiefs, "The President has no pending request to commit forces.", 16, Muted);
        Button(chiefs, "Open Great Powers desk", () => { _page = "Great Powers"; Render(); });
        Button(chiefs, "Return to the Situation Room", () => { _page = "Situation Room"; Render(); });

        var authority = Card(row);
        Text(authority, "PRESIDENTIAL AUTHORITY", 13, Gold);
        if (_campaign.DefenseWindowOpen)
        {
            Text(authority, _campaign.DefenseContext, 22, Ink);
            Text(authority, "Civilian support serves the relief effort. A protective presence commits a carrier group and can trigger War Powers reporting and time limits.", 16, Muted);
            Action(authority, "Authorize civilian support · 6", () => Perform(new Command("defense-support")), !_campaign.CanOrderCivilianSupport);
            Action(authority, "Commit a protective presence · 12", () => Perform(new Command("defense-presence")), !_campaign.CanOrderProtectivePresence);
        }
        if (_campaign.CanWithdraw)
            Action(authority, "Order withdrawal", () => Perform(new Command("defense-withdraw")));
        Rule(authority);
        Text(authority, "Congressional authorization", 20, Ink);
        Text(authority, "This scenario requires 218 House votes and a 60-vote Senate floor threshold.", 14, Muted);
        string status = _campaign.Authorization switch
        {
            DefenseAuthorization.Approved => "Congress granted limited authority.",
            DefenseAuthorization.Denied => "The authorization failed.",
            DefenseAuthorization.Pending => $"Floor votes expected {_campaign.AuthorizationVoteDate:MMMM d}.",
            _ => "No authorization on record."
        };
        Text(authority, status, 16, Muted);
        if (_campaign.DefenseVote is { } vote)
            Text(authority, $"House {vote.HouseYes}/{DefenseRollCall.HouseTarget} · Senate {vote.SenateYes}/{DefenseRollCall.SenateTarget}", 16, Ink);
        if (_campaign.DefenseWindowOpen || _campaign.Mission == DefenseMission.ProtectivePresence)
            Action(authority, "Seek limited authorization · 5", () => Perform(new Command("defense-authorize")), !_campaign.CanRequestAuthorization);
    }

    private void GreatPowerDesk()
    {
        var row = Row(_root);
        var watch = Card(row, 2);
        Text(watch, "STATE DEPARTMENT BRIEFING", 13, Gold);
        Text(watch, _campaign.WorldWarActive ? "World War III" : "Great Powers", 34, Ink);
        foreach (string country in Campaign.GreatPowers)
        {
            var power = _campaign.PowerBrief(country);
            Button(watch, $"{country} · Rung {power.Rung}/10", () => { _greatPower = country; Render(); }, country == _greatPower);
        }
        Rule(watch);
        Text(watch, "If one confrontation reaches rung 10 while a second is at rung 7 or higher, the crisis can become a global war.", 15, Muted);
        if (_campaign.PendingForeignReply is { } pending)
            Text(watch, pending, 16, Gold);
        else if (!_campaign.WorldWarActive && _campaign.LastCeasefire is { } ceasefire)
            Text(watch, $"Ceasefire framework in effect since {ceasefire:MMMM d, yyyy}.", 16, Ink);
        Button(watch, "Return to the Pentagon", () => { _page = "Pentagon"; Render(); });

        var policy = Card(row);
        var selected = _campaign.PowerBrief(_greatPower);
        Text(policy, selected.Country, 24, Ink);
        Text(policy, selected.EmbassyReadout, 16, Muted);
        Action(policy, "Issue a public ultimatum · 6", () => Perform(new Command("great-power-pressure", Code: _greatPower)), !_campaign.CanPressurePower(_greatPower));
        Action(policy, "Open a crisis hotline · 3", () => Perform(new Command("great-power-channel", Code: _greatPower)), !_campaign.CanOpenHotline(_greatPower));
        if (_campaign.WorldWarActive)
        {
            Rule(policy);
            Text(policy, "WARTIME CABINET", 13, Gold);
            Action(policy, "Fund civilian relief · 10", () => Perform(new Command("great-power-relief")), !_campaign.CanFundWartimeRelief);
            Action(policy, "Call a ceasefire summit · 8", () => Perform(new Command("great-power-summit")), !_campaign.CanCallCeasefire);
        }
    }

    private IEnumerable<Briefing> VisibleBriefings() => _campaign.Briefings.Where(entry =>
        !entry.RequiresElectionCall || !ElectionCallsPending || entry.Date != _campaign.CurrentElectionDay);

    private bool ElectionCallsPending => !_campaign.Removed && _campaign.Results.Count > _calls;

    private void Election()
    {
        if (_campaign.Removed)
        {
            RemovalEnding();
            return;
        }
        var layout = Row(_root);
        var map = Card(layout, 3);
        Text(map, "ELECTION NIGHT DECISION DESK", 13, Gold);
        Text(map, _campaign.Results.Count == 0 ? "Road to 270" : "The race for 270", 32, Ink);
        var called = _campaign.Results.Take(_calls).ToDictionary(result => result.State.Code);
        var grid = new GridContainer { Columns = 13 };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        map.AddChild(grid);
        for (int y = 0; y < 6; y++)
        for (int x = 0; x < 13; x++)
        {
            var state = Campaign.States.FirstOrDefault(state => state.X == x && state.Y == y);
            if (state == null)
            {
                grid.AddChild(new Control { CustomMinimumSize = new Vector2(37, 42) });
                continue;
            }
            var result = called.GetValueOrDefault(state.Code);
            var color = result == null ? Slate : result.Margin >= 0 ? Blue.Darkened(.42f) : Red.Darkened(.44f);
            var tile = Button(grid, state.Code, () => { _state = state.Code; Render(); }, false, color);
            tile.CustomMinimumSize = new Vector2(37, 42);
            tile.TooltipText = $"{state.Code} · {state.Electors} electoral votes";
            if (_state == state.Code) tile.AddThemeColorOverride("font_color", Gold);
        }

        var desk = Card(layout);
        Text(desk, "ELECTORAL VOTE", 13, Gold);
        int incumbent = called.Values.Where(result => result.Margin >= 0).Sum(result => result.State.Electors);
        int challenger = called.Values.Where(result => result.Margin < 0).Sum(result => result.State.Electors);
        Text(desk, $"{incumbent}  /  {challenger}", 36, Ink);
        Text(desk, _campaign.Term == 1 ? $"Brian Banks   /   {_campaign.OppositionName}" : $"{CampaignStory.VicePresident}   /   {_campaign.OppositionName}", 15, Muted);
        if (ElectionCallsPending)
            Text(desk, $"{_calls} of {_campaign.Results.Count} races called", 15, Muted);
        var selected = Campaign.States.First(state => state.Code == _state);
        Text(desk, $"{selected.Code} · {selected.Electors} electoral votes", 17, Ink);
        if (called.TryGetValue(_state, out var selectedResult))
        {
            Text(desk, $"Your party’s margin {selectedResult.Margin:+0.0;-0.0;0.0} points", 15, Ink);
            Text(desk, $"State lean {selected.Lean:+0.0;-0.0;0.0} · Approval {selectedResult.ApprovalEffect:+0.0;-0.0;0.0}", 14, Muted);
            Text(desk, $"Economy {selectedResult.EconomyEffect:+0.0;-0.0;0.0} · Visits {selectedResult.CampaignEffect:+0.0;-0.0;0.0} · Election night {selectedResult.ElectionVariation:+0.0;-0.0;0.0}", 14, Muted);
            if (_campaign.VariableWorld)
                Text(desk, $"Local climate {selectedResult.LocalClimate:+0.0;-0.0;0.0} · Opposition campaign {selectedResult.OppositionEffect:+0.0;-0.0;0.0}", 14, Muted);
            if (selectedResult.PolicyReputation != 0)
                Text(desk, $"Policy record {selectedResult.PolicyReputation:+0.0;-0.0;0.0}", 14, Muted);
        }
        if (_campaign.Results.Count == 0)
        {
            Text(desk, $"Election Day · {_campaign.CurrentElectionDay:MMMM d, yyyy}", 15, Muted);
            if (_campaign.OppositionBrief is { } opponent)
                Text(desk, opponent, 15, Ink);
            if (_campaign.Term == 2)
                Text(desk, $"You have reached the two-term limit. Campaign appearances support {CampaignStory.VicePresident}.", 15, Muted);
            double margin = _campaign.EstimatedMargin(selected);
            Text(desk, Math.Abs(margin) < 3 ? "Current outlook · competitive"
                : margin >= 3 ? "Current outlook · favors your party" : "Current outlook · favors the opposition", 15, Ink);
            Text(desk, "The outlook moves with approval, the economy, and campaigning.", 15, Muted);
            if (_campaign.HasVisited(_state))
                Text(desk, "You have already campaigned in this state.", 15, Muted);
            Action(desk, $"Campaign in {_state} · 5", () => Perform(new Command("rally", Code: _state)), _campaign.Capital < 5 || _campaign.HasVisited(_state));
            return;
        }
        if (_calls < _campaign.Results.Count)
        {
            Button(desk, "Call next states", () => { _calls = Math.Min(_calls + 8, _campaign.Results.Count); Save(); Render(); });
            Button(desk, "Call all remaining states", () => { _calls = _campaign.Results.Count; Save(); Render(); });
            return;
        }
        string verdict = _campaign.Term == 1
            ? incumbent >= 270 ? "REELECTED" : challenger >= 270 ? "DEFEATED" : "ELECTORAL TIE"
            : incumbent >= 270 ? "YOUR PARTY HOLDS THE WHITE HOUSE"
                : challenger >= 270 ? "OPPOSITION WINS THE WHITE HOUSE" : "ELECTORAL TIE";
        Text(desk, verdict, 23, Gold);
        if (incumbent == 269)
            Text(desk, "Neither ticket reached 270. This campaign requires an Electoral College majority to unlock a second term.", 15, Muted);
        if (!_campaign.Finished)
        {
            Rule(map);
            Text(map, "Election night in Michigan", 22, Ink);
            Text(map, _campaign.ElectionNightScene, 17, Ink);
            Text(desk, $"Your term ends on {_campaign.CurrentTermEnd:MMMM d, yyyy}.", 15, Muted);
            Button(desk, "Continue governing", () => { _page = "Oval Office"; Render(); });
            return;
        }

        Rule(desk);
        Text(desk, _campaign.CanBeginSecondTerm ? "First-term record" : "Presidential record", 20, Ink);
        Text(desk, $"{_campaign.Term * 4} years served · {_campaign.LawsSigned} laws signed · {_campaign.LawsVetoed} vetoes", 15, Ink);
        Text(desk, $"Final approval {_campaign.Approval:F1}% · Inflation {_campaign.Inflation:F1}% · Unemployment {_campaign.Unemployment:F1}%", 15, Muted);
        Text(desk, _campaign.OpeningRecord, 15, Muted);
        if (_campaign.Impeachments > 0)
            Text(desk, $"House impeachments · {_campaign.Impeachments}", 15, Muted);
        if (_campaign.FirstElectionElectoralVotes is int firstResult)
            Text(desk, $"2032 reelection · {firstResult} electoral votes", 15, Muted);
        Rule(map);
        Text(map, _campaign.CanBeginSecondTerm ? "Inauguration morning" : "The handover", 22, Ink);
        Text(map, _campaign.Epilogue, 18, Ink);
        ClosingLetter(map);
        Button(map, "Review the campaign record", () => { _page = "Campaign Record"; Render(); });
        if (_campaign.CanBeginSecondTerm)
        {
            Action(desk, "Take the oath for your second term", () => Perform(new Command("second-term")));
            return;
        }
        Text(desk, _campaign.Term == 2 ? "Eight years served. Your presidency is complete." : "Your presidency is complete.", 15, Gold);
        Button(desk, "Start a new presidency", ConfirmNewPresidency);
    }

    private void RemovalEnding()
    {
        var row = Row(_root);
        var ending = Card(row);
        Text(ending, "THE PRESIDENCY ENDS", 13, Gold);
        Text(ending, "Brian Banks removed from office", 32, Ink);
        if (_campaign.LastHouseOversightVote is { } house && _campaign.LastSenateOversightVote is { } senate)
        {
            Text(ending, $"House impeachment · {house.Yes}–{house.Members - house.Yes}", 18, Ink);
            Text(ending, $"Senate conviction · {senate.Yes}–{senate.Members - senate.Yes} · {senate.Threshold} required", 18, Ink);
        }
        Text(ending, $"On {_campaign.Date:MMMM d, yyyy}, Vice President Jordan Reed took the oath. Brian Banks’s presidency ended that day.", 17, Muted);
        Text(ending, _campaign.Epilogue, 18, Ink);
        ClosingLetter(ending);
        Button(ending, "Review the campaign record", () => { _page = "Campaign Record"; Render(); });
        Button(ending, "Start a new presidency", ConfirmNewPresidency);
    }

    private void ClosingLetter(VBoxContainer panel)
    {
        if (_campaign.ClosingLetter is not { } letter) return;
        Rule(panel);
        Text(panel, "Elena’s last letter", 20, Ink);
        Text(panel, letter, 16, Muted);
    }

    private void Archive()
    {
        var layout = Row(_root);
        var story = Card(layout);
        Text(story, "CAMPAIGN RECORD", 13, Gold);
        Text(story, "Your presidency", 28, Ink);
        int year = _archiveYear == 0 ? _campaign.Date.Year : _archiveYear;
        foreach (var chapter in CampaignStory.Chapters.TakeWhile(chapter => chapter.Start <= _campaign.Chapter.Start))
            Button(story, $"{chapter.Start.Year} · {chapter.Title}", () => { _archiveYear = chapter.Start.Year; _storyArchive = true; Render(); },
                chapter.Start.Year == year && _storyArchive);
        Rule(story);
        Text(story, _campaign.OpeningRecord, 16, Muted);
        if (_campaign.Finished && (_campaign.Removed || _campaign.Results.Count > 0))
            Button(story, "Return to the Decision Desk", () => { _page = "Decision Desk"; Render(); });
        Button(story, "Start a new presidency", ConfirmNewPresidency);

        var log = Card(layout, 2);
        Text(log, "WHITE HOUSE ARCHIVE", 13, Gold);
        var view = Row(log);
        Button(view, "Story scenes", () => { _storyArchive = true; Render(); }, _storyArchive);
        Button(view, "All briefings", () => { _storyArchive = false; Render(); }, !_storyArchive);
        var filters = Row(log);
        Button(filters, "Previous year", () => { _archiveYear = year - 1; Render(); }, year <= Campaign.Inauguration.Year);
        Text(filters, year.ToString(), 20, Ink);
        Button(filters, "Next year", () => { _archiveYear = year + 1; Render(); }, year >= _campaign.Date.Year);
        var entries = VisibleBriefings().Where(entry => entry.Date.Year == year && (!_storyArchive || entry.Story)).ToArray();
        if (entries.Length == 0)
            Text(log, "No entries in this view yet.", 15, Muted);
        foreach (var entry in _storyArchive ? entries : entries.Reverse())
        {
            if (entry.Story)
            {
                Text(log, $"{entry.Date:MMM d} · {entry.SceneTitle}", 17, Gold);
                Text(log, entry.Text, 16, Ink);
                Rule(log);
            }
            else
                Text(log, $"{entry.Date:MMM d}   {entry.Text}", 15, entry.Decision ? Ink : Muted);
        }
    }

    private void ConfirmNewPresidency()
    {
        _speed = 0;
        Render();
        var dialog = new ConfirmationDialog
        {
            Title = "Start a new presidency",
            DialogText = "Start in January 2029 and replace this campaign’s autosave?",
            OkButtonText = "Start new campaign"
        };
        AddChild(dialog);
        dialog.Confirmed += () =>
        {
            _oversightSeed = Random.Shared.Next();
            _seed = Random.Shared.Next();
            _campaign = new Campaign(_seed, oversightSeed: _oversightSeed, variableWorld: true);
            _saveVersion = 5;
            _commands.Clear();
            _calls = 0;
            _speed = 0;
            _page = "Oval Office";
            _state = "MI";
            _greatPower = "Russia";
            _archiveYear = 0;
            _storyArchive = true;
            _simulationFault = false;
            Save();
            Render();
        };
        dialog.VisibilityChanged += () => { if (!dialog.Visible) dialog.QueueFree(); };
        dialog.PopupCentered();
    }

    private void Provision(Container parent, Measure measure, string title)
    {
        Button(parent, (_campaign.HasMeasure(measure) ? "✓  " : "○  ") + title,
            () => { Perform(new Command("provision", (int)measure)); Render(); }, !_campaign.CanEdit);
    }

    private void Action(Container parent, string title, Func<bool> action, bool disabled = false)
    {
        Button(parent, title, () =>
        {
            if (!action()) _message = "That action is currently unavailable.";
            Render();
        }, disabled);
    }


    private bool Perform(Command command)
    {
        _message = "";
        if (!Apply(_campaign, command)) return false;
        _commands.Add(command);
        if (command.Kind == "second-term")
        {
            _calls = 0;
            _page = "Oval Office";
        }
        Save();
        return true;
    }

    private static bool Apply(Campaign campaign, Command command)
    {
        switch (command.Kind)
        {
            case "advance": return command.Number is >= 1 and <= 120 && campaign.Advance(command.Number) == command.Number;
            case "enable-agenda": return campaign.EnableAgenda();
            case "great-power-pressure": return campaign.PressGreatPower(command.Code);
            case "great-power-channel": return campaign.OpenCrisisHotline(command.Code);
            case "great-power-relief": return campaign.FundWartimeRelief();
            case "great-power-summit": return campaign.CallCeasefireSummit();
            case "defense-support": return campaign.OrderCivilianSupport();
            case "defense-presence": return campaign.OrderProtectivePresence();
            case "defense-authorize": return campaign.RequestDefenseAuthorization();
            case "defense-withdraw": return campaign.WithdrawDefenseMission();
            case "enable-conversations": return campaign.EnableConversations();
            case "story-choice":
                return Enum.TryParse<ConversationId>(command.Code, out var id) && campaign.AnswerConversation(id, command.Number);
            case "agenda": return campaign.ChooseAgendaOption(command.Number);
            case "enable-oversight": return campaign.EnableOversight(command.Number);
            case "refuse-offer": return campaign.RespondToOffer(OfferResponse.Decline);
            case "report-offer": return campaign.RespondToOffer(OfferResponse.Report);
            case "accept-offer": return campaign.RespondToOffer(OfferResponse.Accept);
            case "second-term": return campaign.BeginSecondTerm();
            case "sign": return campaign.ResolveBill(true);
            case "veto": return campaign.ResolveBill(false);
            case "order": return campaign.Order();
            case "rule": return campaign.Rule();
            case "expert": return campaign.Appoint(true);
            case "loyalist": return campaign.Appoint(false);
            case "pledge-jobs": return campaign.MakeOpeningCommitment(OpeningCommitment.ProtectJobs);
            case "pledge-budget": return campaign.MakeOpeningCommitment(OpeningCommitment.ControlCosts);
            case "pledge": return campaign.Deal(command.Code);
            case "track": return campaign.ChangeTrack();
            case "trade": return campaign.Trade();
            case "rally": return campaign.Rally(command.Code);
            case "provision" when campaign.CanEdit && Enum.IsDefined(typeof(Measure), command.Number):
                campaign.Toggle((Measure)command.Number);
                return true;
            default: return false;
        }
    }

    private SaveData CaptureSave() =>
        new(_saveVersion, _seed, _calls, _commands, _oversightSeed, _page, _state, _greatPower, _archiveYear, _storyArchive);

    private void Save()
    {
        string target = ProjectSettings.GlobalizePath(SaveFile);
        string temporary = target + ".tmp";
        try
        {
            System.IO.File.WriteAllText(temporary, JsonSerializer.Serialize(CaptureSave()));
            if (_primarySaveValid && System.IO.File.Exists(target))
                System.IO.File.Replace(temporary, target, target + ".backup");
            else
            {
                if (System.IO.File.Exists(target))
                    System.IO.File.Copy(target, target + $".unreadable-{Guid.NewGuid():N}");
                System.IO.File.Move(temporary, target, true);
            }
            _primarySaveValid = true;
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        {
            _message = "Autosave failed. The campaign is still running.";
        }
    }

    private static Campaign Replay(SaveData? save)
    {
        if (save is not { Version: 1 or 2 or 3 or 4 or 5, Commands: not null })
            throw new System.IO.InvalidDataException("Unsupported or incomplete campaign save.");
        var campaign = new Campaign(save.Seed, oversightEnabled: save.Version >= 2, oversightSeed: save.OversightSeed,
            agendaEnabled: save.Version >= 3, variableWorld: save.Version >= 4, conversationsEnabled: save.Version >= 5);
        foreach (var command in save.Commands)
        {
            if (command?.Code == null || !Apply(campaign, command))
                throw new System.IO.InvalidDataException("The campaign history contains an invalid decision.");
        }
        return campaign;
    }

    private static (SaveData Save, Campaign Campaign)? ReadSave(string path)
    {
        if (!System.IO.File.Exists(path)) return null;
        try
        {
            var save = JsonSerializer.Deserialize<SaveData>(System.IO.File.ReadAllText(path));
            var campaign = Replay(save);
            return (save!, campaign);
        }
        catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException
            or UnauthorizedAccessException or JsonException)
        {
            GD.PrintErr($"Campaign save unavailable: {error.Message}");
            return null;
        }
    }

    private void Restore()
    {
        string target = ProjectSettings.GlobalizePath(SaveFile);
        var primary = ReadSave(target);
        _primarySaveValid = primary.HasValue;
        var loaded = primary ?? ReadSave(target + ".backup");
        if (loaded is not { } checkpoint)
        {
            if (System.IO.File.Exists(target) || System.IO.File.Exists(target + ".backup"))
                _message = "The previous campaign could not be loaded. A new term is open.";
            return;
        }

        var (save, campaign) = checkpoint;
        _campaign = campaign;
        _commands.AddRange(save.Commands);
        _seed = save.Seed;
        _saveVersion = save.Version;
        _oversightSeed = save.OversightSeed ?? (save.Version >= 2 ? save.Seed : _oversightSeed);
        _calls = Math.Clamp(save.Calls, 0, campaign.Results.Count);
        _state = Campaign.States.Any(state => state.Code == save.State) ? save.State! : "MI";
        _greatPower = Campaign.GreatPowers.Contains(save.GreatPower) ? save.GreatPower! : "Russia";
        _archiveYear = Math.Clamp(save.ArchiveYear, 0, campaign.Date.Year);
        if (_archiveYear < Campaign.Inauguration.Year) _archiveYear = 0;
        _storyArchive = save.StoryArchive;
        if (campaign.Finished || ElectionCallsPending)
            _page = "Decision Desk";
        else if (campaign.OnDesk)
            _page = "Oval Office";
        else if (save.Page is "Oval Office" or "Capitol Hill" or "Situation Room" or "Decision Desk" or "Campaign Record" or "Pentagon" or "Great Powers")
            _page = save.Page;
        else if (campaign.Results.Count > 0)
            _page = "Decision Desk";
        if (!campaign.OversightEnabled)
            Perform(new Command("enable-oversight", _oversightSeed));
        if (!campaign.AgendaEnabled)
            Perform(new Command("enable-agenda"));
        if (!campaign.Finished && !campaign.ConversationsEnabled)
            Perform(new Command("enable-conversations"));
        if (primary == null && _message == "")
            _message = "The latest autosave was unreadable. Your previous checkpoint was restored.";
    }

    private static HBoxContainer Row(Container parent)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        parent.AddChild(row);
        return row;
    }

    private static VBoxContainer Column(Container parent)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        parent.AddChild(column);
        return column;
    }

    private static VBoxContainer Card(Container parent, float weight = 1)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = weight };
        panel.AddThemeStyleboxOverride("panel", Box(new Color("#131e30"), 18));
        parent.AddChild(panel);
        return Column(panel);
    }

    private static void Metric(Container parent, string name, string value)
    {
        var card = Card(parent);
        Text(card, name, 12, Muted);
        Text(card, value, 24, Ink);
    }

    private static void Rule(Container parent) => parent.AddChild(new HSeparator());

    private static void Text(Container parent, string value, int size, Color color)
    {
        var label = new Label { Text = value, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
    }

    private static Button Button(Container parent, string title, System.Action click, bool disabled = false, Color? color = null)
    {
        var button = new Button { Text = title, Disabled = disabled };
        button.AddThemeFontSizeOverride("font_size", 15);
        button.AddThemeColorOverride("font_color", Ink);
        button.AddThemeStyleboxOverride("normal", Box(color ?? Slate, 8));
        button.AddThemeStyleboxOverride("hover", Box((color ?? Slate).Lightened(.15f), 8));
        button.AddThemeStyleboxOverride("pressed", Box((color ?? Slate).Lightened(.25f), 8));
        button.AddThemeStyleboxOverride("disabled", Box(new Color("#172235"), 8));
        button.Pressed += click;
        parent.AddChild(button);
        return button;
    }

    private static StyleBoxFlat Box(Color color, int padding) => new()
    {
        BgColor = color,
        CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
        CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        ContentMarginLeft = padding, ContentMarginRight = padding,
        ContentMarginTop = padding, ContentMarginBottom = padding
    };

    private partial class SeatChart : Control
    {
        public Vote[] Votes = [];
        public string Selected = "";
        public System.Action<string> OnSeat;
        private readonly List<(Vector2 Point, Vote Vote)> _points = [];

        public override void _Draw()
        {
            _points.Clear();
            int rows = Votes.Length > 100 ? 12 : 6;
            float spacing = Votes.Length > 100 ? 5.1f : 7f;
            var ordered = Votes.OrderBy(vote => !vote.Yes).ThenBy(vote => vote.Seat.State).ToArray();
            Vector2 center = new(Size.X / 2, Size.Y - 22);
            float outer = Mathf.Min(Size.X / 2 - 18, Size.Y - 46);
            for (int index = 0; index < ordered.Length; index++)
            {
                int row = index % rows;
                int column = index / rows;
                int columns = (ordered.Length + rows - 1 - row) / rows;
                float angle = Mathf.Lerp(Mathf.Pi - .10f, .10f, column / (float)Math.Max(1, columns - 1));
                float radius = outer * (.42f + .58f * row / (rows - 1));
                Vector2 point = center + new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle)) * radius;
                Vote vote = ordered[index];
                if (vote.Seat.State == Selected) DrawCircle(point, spacing + 2, Ink);
                DrawCircle(point, spacing, vote.Swing ? Gold : vote.Yes ? Blue : Red);
                _points.Add((point, vote));
            }
        }

        public override void _GuiInput(InputEvent input)
        {
            if (input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouse || _points.Count == 0) return;
            var hit = _points.MinBy(entry => entry.Point.DistanceSquaredTo(mouse.Position));
            if (hit.Point.DistanceSquaredTo(mouse.Position) <= 100)
            {
                AcceptEvent();
                OnSeat?.Invoke(hit.Vote.Seat.State);
            }
        }
    }
}
