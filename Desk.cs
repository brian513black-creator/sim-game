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
    private static readonly Color StoryBackdrop = new("#191f2a");
    private static readonly Color PanelInk = new("#131e30");
    private static readonly Color DarkInk = new("#101722");
    private Campaign _campaign;
    private VBoxContainer _root;
    private VBoxContainer _chrome;
    private Texture2D? _officeArt;
    private ScrollContainer _scroll;
    private string _renderedPage = "";
    private string _page = "Story";
    private bool _atTitle = true;
    private bool _helpOpen;
    private bool _fullscreen;
    private int _saveSlot = 1;
    private FileStream? _slotWriteLease;
    private int _storyOffset;
    private bool _storyElection;
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
    private const int CurrentSaveVersion = 9;
    private int _saveVersion = CurrentSaveVersion;
    private int _oversightSeed = Random.Shared.Next();
    private const int SaveSlotCount = 3;
    private const string SettingsFile = "user://settings.cfg";
    private readonly List<Command> _commands = [];
    private record Command(string Kind, int Number = 0, string Code = "");
    private record CampaignSummary(DateTime Date, int Term, string Status);
    private record SaveData(int Version, int Seed, int Calls, List<Command> Commands, int? OversightSeed = null,
        string? Page = null, string? State = null, string? GreatPower = null, int ArchiveYear = 0, bool StoryArchive = true,
        bool StoryElection = false, CampaignSummary? Summary = null);
    private double _elapsed;

    public override void _Ready()
    {
        var margin = new MarginContainer();
        AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, side is "left" or "right" ? 34 : 18);
        var frame = Column(margin);
        frame.AddThemeConstantOverride("separation", 16);
        _chrome = Column(frame);
        _chrome.AddThemeConstantOverride("separation", 10);
        _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        frame.AddChild(_scroll);
        _root = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _root.AddThemeConstantOverride("separation", 12);
        _scroll.AddChild(_root);
        _officeArt = GD.Load<Texture2D>("res://assets/executive-office.webp");
        LoadDisplayPreference();
        Render();
    }

    public override void _ExitTree() => ReleaseSaveSlot();

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is not InputEventKey { Keycode: Key.Escape, Pressed: true, Echo: false } || _running) return;
        if (!_atTitle)
        {
            GetViewport().SetInputAsHandled();
            OpenTitle();
        }
        else if (_helpOpen)
        {
            GetViewport().SetInputAsHandled();
            _helpOpen = false;
            Render();
        }
    }

    public override void _Process(double delta)
    {
        if (_atTitle || _speed == 0 || _running) return;
        _elapsed += delta;
        if (_elapsed < .65) return;
        _elapsed = 0;
        Advance(_speed);
    }

    private async void Advance(int days)
    {
        if (_atTitle || _running || _simulationFault || _campaign.Finished || _campaign.OnDesk || ElectionCallsPending) return;
        string priorEpisode = _campaign.Current.Title;
        bool fromStory = _page == "Story";
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
        if (count > 0)
        {
            _commands.Add(new Command("advance", count));
            _storyOffset = 0;
        }
        if (count < days || _campaign.DecisionToday || _campaign.StoryToday)
            _speed = 0;
        if (_campaign.Finished || (!fromStory && _campaign.Results.Count > 0 && _calls == 0 && _campaign.Date == _campaign.CurrentElectionDay))
            _page = "Decision Desk";
        else if (!fromStory && (_campaign.OnDesk || _campaign.Date == _campaign.CurrentMidterms || _campaign.PendingOffer != null
            || (_campaign.CanChooseAgenda && _campaign.Current.Title != priorEpisode)
            || (_campaign.PendingConversation is { } conversation && conversation.Id != priorConversation)))
            _page = "Oval Office";
        if (count > 0) Save();
        Render();
    }

    private void Render()
    {
        string view = _atTitle ? (_helpOpen ? "Help" : "Title") : _page;
        if (_renderedPage != view)
        {
            _scroll.ScrollVertical = 0;
            _renderedPage = view;
        }
        Clear(_root);
        if (_running)
        {
            foreach (var button in _chrome.FindChildren("*", "Button", true, false).OfType<Button>())
                button.Disabled = true;
            Text(_root, "Advancing the presidential calendar…", 19, Gold);
            return;
        }

        Clear(_chrome);
        if (_atTitle)
        {
            if (_helpOpen) ShowHelp();
            else TitleScreen();
            return;
        }
        if (_simulationFault)
        {
            Text(_root, "Campaign paused", 34, Ink);
            Text(_root, "The calendar could not recover in this session. Open your campaign again to try its last autosave.", 17, Muted);
            Button(_root, "Campaign menu", OpenTitle);
            return;
        }

        RenderChrome();

        if (_campaign.WorldWarActive)
            Text(_root, "WORLD WAR III · Civilian relief and ceasefire diplomacy are available at the Great Powers desk.", 16, Gold);
        if (_message != "") Text(_root, _message, 15, Gold);
        if (ElectionCallsPending && _page is not ("Decision Desk" or "Story"))
            Text(_root, "The calendar is paused while the Decision Desk calls the election.", 16, Gold);
        if (_campaign.Finished && _page != "Decision Desk")
            Text(_root, "Visit the Decision Desk to review your record and close the term.", 16, Gold);
        if (_campaign.OnDesk && !_campaign.Finished && _page is not ("Oval Office" or "Story"))
            Text(_root, "The calendar is paused. Return to the Oval Office to sign or veto the bill.", 16, Gold);
        if (!_campaign.Finished && _page is not ("Oval Office" or "Campaign Record" or "Decision Desk" or "Story")
            && _campaign.Day > 0 && VisibleBriefings().LastOrDefault(entry => entry.Story && entry.Date == _campaign.Date) is { } newScene)
        {
            var notice = Row(_root);
            Text(notice, $"New story scene · {newScene.SceneTitle}", 16, Gold).SizeFlagsHorizontal = SizeFlags.ExpandFill;
            Button(notice, "Read scene", () => { _page = "Story"; _storyOffset = 0; Render(); });
        }
        switch (_page)
        {
            case "Story": Story(); break;
            case "Capitol Hill": Hill(); break;
            case "Situation Room": Situation(); break;
            case "Pentagon": Pentagon(); break;
            case "Great Powers": GreatPowerDesk(); break;
            case "Decision Desk": Election(); break;
            case "Campaign Record": Archive(); break;
            default: Oval(); break;
        }
    }

    private static void Clear(Container container)
    {
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void RenderChrome()
    {
        var headline = Row(_chrome);
        var brand = Column(headline);
        Text(brand, "THE MANDATE", 29, Ink, wrap: false);
        Text(brand, "PRESIDENT BRIAN BANKS", 12, Gold, wrap: false);
        brand.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var date = Column(headline);
        Text(date, _campaign.Date.ToString("dddd, MMMM d, yyyy"), 18, Ink, wrap: false).HorizontalAlignment = HorizontalAlignment.Right;
        var termDay = (_campaign.Date - _campaign.CurrentTermStart).Days + 1;
        var termLength = (_campaign.CurrentTermEnd - _campaign.CurrentTermStart).Days;
        Text(date, _campaign.Removed ? "REMOVED FROM OFFICE" : _campaign.Finished ? $"TERM {_campaign.Term} COMPLETE" : $"TERM {_campaign.Term}  /  DAY {termDay:N0} OF {termLength:N0}", 12, Gold, wrap: false).HorizontalAlignment = HorizontalAlignment.Right;
        Button(headline, "Menu", OpenTitle).SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var metrics = Row(_chrome);
        Metric(metrics, "APPROVAL", $"{_campaign.Approval:F1}%");
        Metric(metrics, "POLITICAL CAPITAL", $"{_campaign.Capital}");
        Metric(metrics, "INFLATION", $"{_campaign.Inflation:F1}%");
        Metric(metrics, "FED RATE", $"{_campaign.Rate:F2}%");
        Metric(metrics, "UNEMPLOYMENT", $"{_campaign.Unemployment:F1}%");

        var nav = Row(_chrome);
        foreach (string page in new[] { "Story", "Oval Office", "Capitol Hill", "Situation Room", "Decision Desk", "Campaign Record" })
        {
            bool active = _page == page;
            var tab = Button(nav, page, () => { _page = page; _message = ""; Render(); }, active);
            if (active)
            {
                tab.AddThemeColorOverride("font_disabled_color", Gold);
                tab.AddThemeStyleboxOverride("disabled", Box(new Color("#392e25"), 8));
            }
        }
        Control spacer = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        nav.AddChild(spacer);
        bool paused = _campaign.OnDesk || _campaign.Finished || ElectionCallsPending;
        Button(nav, _speed == 0 ? "Play" : "Pause", () => { _speed = _speed == 0 ? 1 : 0; Render(); }, paused);
        Button(nav, "+1 day", () => Advance(1), paused);
        Button(nav, "+7 days", () => Advance(7), paused);
        if (_page != "Story")
            Button(nav, "Next decision", () => Advance(120), paused);
    }

    private void Story()
    {
        var layout = Row(_root);
        var stage = Card(layout, 2.1f, StoryBackdrop, 20);
        stage.AddThemeConstantOverride("separation", 16);
        var scenes = VisibleBriefings().Where(entry => entry.Story).ToList();
        if (ElectionCallsPending)
            scenes.Add(new Briefing(_campaign.CurrentElectionDay, "Nora sets a second chair beside the television. Staff are marking off the states whose polls have closed. The Decision Desk is waiting for enough returns to start calling them.", false, "Election night"));
        _storyOffset = Math.Clamp(_storyOffset, 0, scenes.Count - 1);
        var scene = scenes[scenes.Count - 1 - _storyOffset];

        StoryTitle(stage, scene);
        var script = new Label { Text = scene.Text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        script.AddThemeFontOverride("font", new SystemFont { FontNames = ["Georgia", "Liberation Serif", "DejaVu Serif"] });
        script.AddThemeFontSizeOverride("font_size", 23);
        script.AddThemeColorOverride("font_color", Ink);
        script.AddThemeConstantOverride("line_spacing", 6);
        stage.AddChild(script);
        if (scenes.Count > 1)
        {
            Rule(stage);
            var navigation = Row(stage);
            Button(navigation, "Earlier scene", () => { _storyOffset++; Render(); }, _storyOffset >= scenes.Count - 1);
            if (_storyOffset > 1)
                Button(navigation, "Later scene", () => { _storyOffset--; Render(); });
            if (_storyOffset > 0)
                Button(navigation, "Current scene", () => { _storyOffset = 0; Render(); });
        }

        var decision = Card(layout, 1, PanelInk, 20);
        if (_storyOffset > 0)
        {
            Text(decision, "From the campaign record", 19, Ink);
            Text(decision, "Return to the current scene to pick up the story.", 15, Muted);
            return;
        }

        if (_campaign.Finished)
        {
            Text(decision, "The presidency has reached a stopping point.", 19, Ink);
            Button(decision, "Review the campaign outcome", () => { _page = "Decision Desk"; Render(); });
            return;
        }
        if (ElectionCallsPending)
        {
            Text(decision, "The first returns are coming in.", 21, Ink);
            Button(decision, "Open the Decision Desk", () =>
            {
                _storyElection = true;
                _page = "Decision Desk";
                Render();
            });
            return;
        }
        if (_campaign.OnDesk)
        {
            Text(decision, _campaign.PackageName, 21, Ink);
            Text(decision, "Congress sent a bill to your desk. The calendar is paused for your signature or veto.", 15, Muted);
            Action(decision, "Sign into law", () => Perform(new Command("sign")));
            Action(decision, "Veto · 5 capital", () => Perform(new Command("veto")), _campaign.Capital < 5);
            return;
        }
        if (_campaign.PendingOffer != null)
            OversightPanel(decision);
        if (_campaign.PendingConversation is { } conversation)
            ConversationPanel(decision, showScene: scene.SceneTitle != conversation.Title);
        if (_campaign.CanChooseAgenda)
            AgendaPanel(decision);
        else if (_campaign.CanEdit)
        {
            Text(decision, "BEFORE THE FLOOR VOTE", 13, Gold, wrap: false);
            Text(decision, _campaign.PackageName, 21, Ink);
            Text(decision, _campaign.BillStatus, 15, Muted);
            Button(decision, "Open Capitol Hill", () => { _page = "Capitol Hill"; Render(); });
            if (_campaign.Day < 64 && _campaign.CanTrade)
                Button(decision, "Review trade options", () => { _page = "Situation Room"; Render(); });
            PledgePanel(decision);
            Rule(decision);
        }

        var briefing = VisibleBriefings().LastOrDefault(entry => !entry.Story && entry.Decision && entry.Date == _campaign.Date);
        if (briefing != null && _campaign.Day > 0) Text(decision, briefing.Text, 15, Muted);
        bool waitingForReply = _campaign.PendingConversation != null;
        Button(decision, waitingForReply ? "Continue without answering" : "Continue story", () => Advance(120),
            primary: !waitingForReply);
    }

    private VBoxContainer OfficeHero(VBoxContainer stage, int height)
    {
        var hero = new Panel { CustomMinimumSize = new Vector2(0, height), ClipContents = true };
        hero.AddThemeStyleboxOverride("panel", Box(DarkInk, 0));
        stage.AddChild(hero);
        var image = new TextureRect
        {
            Texture = _officeArt,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(.72f, .72f, .75f)
        };
        hero.AddChild(image);
        image.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var scrim = new ColorRect { Color = new Color(0, 0, 0, .23f), MouseFilter = MouseFilterEnum.Ignore };
        hero.AddChild(scrim);
        scrim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var inset = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            inset.AddThemeConstantOverride("margin_" + side, 22);
        hero.AddChild(inset);
        inset.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var title = Column(inset);
        title.Alignment = BoxContainer.AlignmentMode.Center;
        return title;
    }

    private void StoryTitle(VBoxContainer stage, Briefing scene)
    {
        var title = OfficeHero(stage, 172);
        Text(title, scene.Date.ToString("MMMM d, yyyy"), 14, Gold);
        Text(title, scene.SceneTitle!, 35, Ink);
        if (scene.Speaker is { } speaker) Text(title, speaker, 16, Ink);
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
        if (_campaign.PendingTradePartner != null || _campaign.Episode == null || _campaign.Episode.Tools.HasFlag(AgencyTools.Trade))
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
        PledgePanel(desk);
        Rule(desk);
        bool offersOrder = _campaign.Episode == null || _campaign.Episode.Tools.HasFlag(AgencyTools.Order);
        var orderExpiry = _campaign.EmergencyOrderExpiry;
        if (offersOrder || orderExpiry != null)
        {
            Text(desk, $"Attorney General  /  {_campaign.OrderStatus}", 15, Muted);
            if (orderExpiry is { } expiry)
                Text(desk, $"Emergency order expires {expiry:MMMM d}.", 15, Muted);
            if (_campaign.PendingCourtReview is { } courtReview)
                Text(desk, $"Court decision expected {courtReview:MMMM d}.", 15, Muted);
        }
        if (offersOrder)
            Action(desk, "Issue emergency order · 9", () => Perform(new Command("order")), !_campaign.CanOrder || _campaign.Capital < 9);
        bool offersRule = _campaign.Episode == null || _campaign.Episode.Tools.HasFlag(AgencyTools.Rule);
        if (_campaign.PendingAgencyReview is { } agencyReview)
            Text(desk, $"Agency review in progress. Rule takes effect {agencyReview:MMMM d}.", 15, Muted);
        else if (offersRule)
            Text(desk, "Agency review depends on your cabinet appointment.", 15, Muted);
        if (offersRule)
            Action(desk, "Start agency rulemaking · 4", () => Perform(new Command("rule")), !_campaign.CanRule || _campaign.Capital < 4);
        Action(desk, "Appoint policy expert · 7", () => Perform(new Command("expert")), !_campaign.CanAppoint || _campaign.Capital < 7);
        Action(desk, "Appoint party loyalist · +5", () => Perform(new Command("loyalist")), !_campaign.CanAppoint);
        Rule(desk);
        Text(desk, $"{_campaign.LawsSigned} laws signed  /  {_campaign.LawsVetoed} vetoes  /  {_campaign.Losses} stalled votes", 15, Muted);
    }

    private void PledgePanel(VBoxContainer desk)
    {
        if (_campaign.CanMakeCommitment)
        {
            Rule(desk);
            Text(desk, "What will you promise the country?", 19, Ink);
            Action(desk, "Pledge worker relief · −5 capital", () => Perform(new Command("pledge-jobs")), _campaign.Capital < 5);
            Action(desk, "Pledge a $130B cap · +4 capital", () => Perform(new Command("pledge-budget")));
            Text(desk, "A kept pledge earns 2 approval and 4 capital. Breaking it costs the same. A pledge is optional.", 14, Muted);
        }
        else if (_campaign.Commitment != OpeningCommitment.None && _campaign.Day < 64)
            Text(desk, _campaign.OpeningRecord, 15, Gold);
    }

    private void ConversationPanel(VBoxContainer desk, bool showScene = true)
    {
        if (_campaign.PendingConversation is not { } conversation) return;
        if (showScene)
        {
            Text(desk, conversation.Speaker, 13, Gold);
            Text(desk, conversation.Title, 25, Ink);
            Text(desk, conversation.Scene, 16, Ink);
        }
        Text(desk, $"Reply by {conversation.Deadline:MMMM d}.", 14, Gold);
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

        var rollCall = _campaign.FloorRollCall(_senate);
        var votes = rollCall ?? _campaign.WhipCount(_senate);
        int yes = votes.Count(vote => vote.Yes);
        bool recorded = rollCall != null;
        int target = _senate ? _campaign.SenateTarget : 218;
        Text(chamber, recorded ? "CAPITOL ROLL CALL" : "CAPITOL WHIP COUNT", 13, Gold);
        Text(chamber, recorded ? $"{yes} yes · {votes.Count - yes} no" : $"{yes} / {target} votes needed", 32, Ink);
        Text(chamber, recorded ? "Blue · yes     Red · no" : "Blue · yes     Red · no     Gold · swing", 15, Muted);
        var tabs = Row(chamber);
        Button(tabs, "Senate", () => { _senate = true; Render(); }, _senate);
        Button(tabs, "House", () => { _senate = false; Render(); }, !_senate);
        var delegation = votes.Where(vote => vote.Seat.State == _state).ToArray();
        Text(chamber, $"{_state} delegation selected", 18, Ink);
        int delegationYes = delegation.Count(vote => vote.Yes);
        string delegationSummary = delegation.Length == 0 ? "No voting members in this chamber."
            : recorded ? $"{delegationYes} of {delegation.Length} voted yes"
            : $"{delegationYes} of {delegation.Length} leaning yes · {delegation.Count(vote => vote.Swing)} persuadable";
        Text(chamber, delegationSummary, 15, Muted);
        if (!recorded)
            Action(chamber, $"Offer {_state} funding pledge · 6", () => Perform(new Command("pledge", Code: _state)),
                delegation.Length == 0 || !_campaign.CanEdit || _campaign.Capital < 6 || _campaign.Leverage(_state) > 0);

        var seats = new SeatChart();
        seats.CustomMinimumSize = new Vector2(0, 320);
        seats.Votes = votes;
        seats.Selected = _state;
        seats.OnSeat = code => { _state = code; Render(); };
        chamber.AddChild(seats);

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
            Text(rungs, $" {rung} ", 19, rung <= _campaign.Escalation ? Gold : Muted, wrap: false);
        string? pendingTradePartner = _campaign.PendingTradePartner;
        string supplyStatus = $"Rung {_campaign.Escalation} of 10";
        if (pendingTradePartner == null)
            supplyStatus += $"   /   {_campaign.Intelligence}";
        Text(intelligence, supplyStatus, 16, Muted);
        Rule(intelligence);
        foreach (var briefing in VisibleBriefings().Where(entry => !entry.Story).TakeLast(4).Reverse())
            Text(intelligence, $"{briefing.Date:MMM d}   {briefing.Text}", 15, Muted);
        var diplomacy = Card(layout);
        bool tradeAvailable = _campaign.Episode == null || _campaign.Episode.Tools.HasFlag(AgencyTools.Trade);
        if (pendingTradePartner != null || tradeAvailable)
        {
            Text(diplomacy, "DIPLOMATIC CHANNEL", 13, Gold);
            Text(diplomacy, pendingTradePartner ?? _campaign.Current.Ally, 26, Ink);
            Text(diplomacy, pendingTradePartner != null ? _campaign.Intelligence
                : "A temporary import agreement can ease the shortage. Your counterpart can decline under domestic pressure.", 17, Muted);
            if (_campaign.TradeReadout is { } readout)
                Text(diplomacy, readout, 16, Ink);
            if (pendingTradePartner == null)
                Action(diplomacy, "Open trade talks · 8", () => Perform(new Command("trade")), !_campaign.CanTrade || _campaign.Capital < 8);
            Rule(diplomacy);
        }
        if (!tradeAvailable && _campaign.Episode is { First: not null })
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
            Text(authority, _campaign.DefenseWindowTitle, 22, Ink);
            Text(authority, "Civilian support serves the relief effort. A protective presence commits a carrier group and can trigger War Powers reporting and time limits.", 16, Muted);
            Action(authority, "Authorize civilian support · 6", () => Perform(new Command("defense-support")), !_campaign.CanOrderCivilianSupport);
            Action(authority, "Commit a protective presence · 12", () => Perform(new Command("defense-presence")), !_campaign.CanOrderProtectivePresence);
        }
        if (_campaign.CanWithdraw)
            Action(authority, "Order withdrawal", () => Perform(new Command("defense-withdraw")));
        Rule(authority);
        Text(authority, "Congressional authorization", 20, Ink);
        if (_campaign.DefenseAuthorizationContext.Length > 0)
            Text(authority, _campaign.DefenseAuthorizationContext, 14, Gold);
        if (_campaign.OtherPendingAuthorization is { } pending)
            Text(authority, $"Congress is still considering the authorization for {pending}.", 15, Muted);
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
        bool campaigning = _campaign.Results.Count == 0;
        var forecasts = campaigning ? Campaign.States.Select(_campaign.ForecastFor).ToDictionary(outlook => outlook.State.Code) : null;
        Text(map, "ELECTION DECISION DESK", 13, Gold);
        Text(map, campaigning ? "Road to 270" : "The race for 270", 32, Ink);
        if (forecasts != null)
        {
            int projected = forecasts.Values.Where(outlook => outlook.Margin >= 0).Sum(outlook => outlook.State.Electors);
            int gap = 270 - projected;
            string projection = gap > 0 ? $"Need {gap} more to reach 270"
                : gap == 0 ? "At the 270 threshold" : $"{-gap} above 270";
            Text(map, $"Projected {projected} electoral votes · {projection}", 21, Ink);
            Text(map, "Blue · your party   Red · opposition   Gold · toss-up", 14, Muted);
        }
        var called = _campaign.Results.Take(_calls).ToDictionary(result => result.State.Code);
        Container mapBody = campaigning ? Row(map) : map;
        var grid = new GridContainer { Columns = 13 };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        mapBody.AddChild(grid);
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
            var forecast = forecasts?.GetValueOrDefault(state.Code);
            var color = forecast != null ? ForecastColor(forecast)
                : result == null ? Slate : result.Margin >= 0 ? Blue.Darkened(.42f) : Red.Darkened(.44f);
            var tile = Button(grid, state.Code, () => { _state = state.Code; Render(); }, false, color);
            tile.CustomMinimumSize = new Vector2(37, 42);
            tile.TooltipText = forecast == null
                ? $"{state.Code} · {state.Electors} electoral votes"
                : $"{state.Code} · {state.Electors} electoral votes · {ForecastStatus(forecast.Margin)}";
            if (_state == state.Code) tile.AddThemeColorOverride("font_color", Gold);
        }

        var desk = Card(layout);
        var selected = Campaign.States.First(state => state.Code == _state);
        if (forecasts != null)
        {
            var shortlist = Column(mapBody);
            shortlist.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            CampaignTargets(shortlist, forecasts.Values);
            Text(map, "The projection uses today’s margins. Governing decisions, opposition trips, and election-night swings can change it.", 14, Muted);
            CampaignPlan(desk, forecasts[selected.Code]);
            return;
        }

        Text(desk, "ELECTORAL VOTE", 13, Gold);
        int incumbent = called.Values.Where(result => result.Margin >= 0).Sum(result => result.State.Electors);
        int challenger = called.Values.Where(result => result.Margin < 0).Sum(result => result.State.Electors);
        Text(desk, $"{incumbent}  /  {challenger}", 36, Ink);
        Text(desk, _campaign.Term == 1 ? $"Brian Banks   /   {_campaign.OppositionName}" : $"{CampaignStory.VicePresident}   /   {_campaign.OppositionName}", 15, Muted);
        if (ElectionCallsPending)
            Text(desk, $"{_calls} of {_campaign.Results.Count} races called", 15, Muted);
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
            Button(desk, "Continue governing", () =>
            {
                _page = _storyElection ? "Story" : "Oval Office";
                _storyOffset = 0;
                _storyElection = false;
                Save();
                Render();
            });
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
        Button(desk, "Choose another campaign", OpenTitle);
    }

    private void CampaignTargets(VBoxContainer shortlist, IEnumerable<ElectionOutlook> forecasts)
    {
        Text(shortlist, "Where a visit could make the difference", 17, Ink);
        var targets = forecasts.Where(outlook => outlook.ValueOfVisit > 0)
            .OrderByDescending(outlook => outlook.ValueOfVisit)
            .ThenByDescending(outlook => outlook.State.Electors).Take(4).ToArray();
        foreach (var target in targets)
        {
            string code = target.State.Code;
            Button(shortlist, $"{code} · {target.State.Electors} electoral votes", () => { _state = code; Render(); }, code == _state);
            Text(shortlist, $"{target.Margin:+0.0;-0.0;0.0} → {target.MarginAfterVisit:+0.0;-0.0;0.0} points after a visit", 14, Muted);
        }
        if (targets.Length == 0)
            Text(shortlist, "No available visits would change a state’s chances at today’s margins.", 14, Muted);
    }

    private void CampaignPlan(VBoxContainer desk, ElectionOutlook selected)
    {
        Text(desk, "CAMPAIGN PLAN", 13, Gold);
        Text(desk, $"{selected.State.Code} · {selected.State.Electors} electoral votes", 23, Ink);
        Text(desk, $"Election Day · {_campaign.CurrentElectionDay:MMMM d, yyyy}", 15, Muted);
        if (_campaign.Term == 2)
            Text(desk, $"Your visits support {CampaignStory.VicePresident} in the succession race.", 15, Muted);
        Rule(desk);
        var factors = selected.Factors;
        Text(desk, $"Current margin {selected.Margin:+0.0;-0.0;0.0} points · {ForecastStatus(selected.Margin)}", 16, Ink);
        Text(desk, $"State lean {selected.State.Lean:+0.0;-0.0;0.0} · Approval {factors.ApprovalEffect:+0.0;-0.0;0.0} · Economy {factors.EconomyEffect:+0.0;-0.0;0.0}", 14, Muted);
        if (factors.PolicyReputation != 0 || factors.LocalClimate != 0 || factors.OppositionEffect != 0)
            Text(desk, $"Policy record {factors.PolicyReputation:+0.0;-0.0;0.0} · Local climate {factors.LocalClimate:+0.0;-0.0;0.0} · Opposition {factors.OppositionEffect:+0.0;-0.0;0.0}", 14, Muted);
        if (selected.Visited)
            Text(desk, $"You already visited. The {Campaign.RallyMarginBonus:0.0}-point boost is in the current margin.", 15, Muted);
        else
        {
            Text(desk, $"After a visit {selected.MarginAfterVisit:+0.0;-0.0;0.0} points · {ForecastStatus(selected.MarginAfterVisit)}", 15, Ink);
            Action(desk, $"Campaign in {selected.State.Code} · {Campaign.RallyCost}",
                () => Perform(new Command("rally", Code: selected.State.Code)), _campaign.Capital < Campaign.RallyCost, primary: selected.ValueOfVisit > 0);
            Text(desk, $"One visit per state each term. It costs {Campaign.RallyCost} political capital and adds {Campaign.RallyMarginBonus:0.0} points in that state.", 14, Muted);
        }

        Rule(desk);
        Text(desk, "How to build support", 19, Ink);
        Text(desk, "Finish ahead in a state to claim its electoral votes. Reach 270 to win. Keeping promises and managing crises can raise approval nationwide. Lower inflation and unemployment help, especially in states exposed to the economy.", 15, Muted);
        if (_campaign.OppositionBrief is { } opponent)
            Text(desk, opponent, 14, Ink);
    }

    private static string ForecastStatus(double margin) => margin >= Campaign.ElectionDaySwing ? "favors your party"
        : margin <= -Campaign.ElectionDaySwing ? "favors the opposition" : "toss-up";

    private static Color ForecastColor(ElectionOutlook forecast) => forecast.Margin >= Campaign.ElectionDaySwing ? Blue.Darkened(.42f)
        : forecast.Margin <= -Campaign.ElectionDaySwing ? Red.Darkened(.44f) : Gold.Darkened(.63f);

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
        Button(ending, "Choose another campaign", OpenTitle);
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
        Button(story, "Campaign menu", OpenTitle);

        var log = Card(layout, 2);
        Text(log, "WHITE HOUSE ARCHIVE", 13, Gold);
        var view = Row(log);
        Button(view, "Story scenes", () => { _storyArchive = true; Render(); }, _storyArchive);
        Button(view, "All briefings", () => { _storyArchive = false; Render(); }, !_storyArchive);
        var filters = Row(log);
        Button(filters, "Previous year", () => { _archiveYear = year - 1; Render(); }, year <= Campaign.Inauguration.Year);
        Text(filters, year.ToString(), 20, Ink, wrap: false);
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

    private void TitleScreen()
    {
        var title = OfficeHero(_root, 238);
        Text(title, "THE MANDATE", 45, Ink, wrap: false);
        Text(title, "A presidential story", 19, Gold, wrap: false);
        Text(title, "Take the oath as Brian Banks. Govern through your first term and earn a chance to serve a second.", 17, Ink);

        if (_message != "") Text(_root, _message, 15, Gold);
        var slots = Row(_root);
        for (int slot = 1; slot <= SaveSlotCount; slot++)
        {
            int selectedSlot = slot;
            string path = SavePath(slot);
            var card = Card(slots);
            Text(card, $"Campaign {slot}", 22, Ink, wrap: false);
            if (HasSave(path))
            {
                var summary = ReadSlotSummary(path) ?? ReadSlotSummary(path + ".backup");
                if (summary is { } record)
                {
                    Text(card, record.Date.ToString("MMMM d, yyyy"), 16, Ink);
                    Text(card, $"Term {record.Term} · {record.Status}", 15, Muted);
                }
                else
                    Text(card, "Saved presidency", 16, Muted);
                string resume = summary?.Status is "Term complete" or "Removed from office" ? "Review outcome" : "Continue";
                Button(card, resume, () => OpenCampaign(selectedSlot, fresh: false), primary: true);
                Button(card, "Replace campaign", () => ConfirmReplacement(selectedSlot));
            }
            else
            {
                Text(card, "Available", 16, Muted);
                Button(card, "New presidency", () => OpenCampaign(selectedSlot, fresh: true), primary: true);
            }
        }
        var controls = Row(_root);
        Button(controls, "How to play", () => { _helpOpen = true; Render(); });
        Button(controls, _fullscreen ? "Switch to windowed" : "Switch to fullscreen", ToggleFullscreen);
        Button(controls, "Quit", () => GetTree().Quit());
    }

    private void ShowHelp()
    {
        var guide = Card(_root, background: StoryBackdrop, padding: 24);
        Text(guide, "Your first days in office", 34, Ink);
        Text(guide, "Story", 23, Gold);
        Text(guide, "Start with Elena’s call in Story. Make an optional pledge before the opening bill, then use Continue story to reach the next scene or decision. The calendar pauses at bills and election night.", 17, Ink);
        Text(guide, "Capitol Hill", 23, Gold);
        Text(guide, "Review the Industrial Bridge Act before the vote. Check the House and Senate counts. Add a deficit offset to bring the opening package from $246B to $128B, or use the whip count to decide where to bargain.", 17, Ink);
        Text(guide, "Situation Room and Decision Desk", 23, Gold);
        Text(guide, "Use the Situation Room to see foreign responses and reach the Pentagon or Great Powers desks. At the Decision Desk, campaign in close states and build a path to 270 electoral votes.", 17, Ink);
        Text(guide, "Saving and returning", 23, Gold);
        Text(guide, "Each slot has its own autosave. Press Esc or choose Menu to return to campaign selection. You can reopen a finished campaign to review its outcome.", 17, Ink);
        Button(guide, "Back to campaigns", () => { _helpOpen = false; Render(); }, primary: true);
    }

    private static string SavePath(int slot) => ProjectSettings.GlobalizePath(slot == 1
        ? "user://campaign.json" : $"user://campaign-{slot}.json");

    private static bool HasSave(string path) => File.Exists(path) || File.Exists(path + ".backup");

    private static CampaignSummary? ReadSlotSummary(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<SaveData>(File.ReadAllText(path))?.Summary;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private void OpenTitle()
    {
        _speed = 0;
        if (!_simulationFault && !Save())
        {
            Render();
            return;
        }
        ReleaseSaveSlot();
        _message = "";
        _atTitle = true;
        _helpOpen = false;
        Render();
    }

    private void ReleaseSaveSlot()
    {
        _slotWriteLease?.Dispose();
        _slotWriteLease = null;
    }

    private void OpenCampaign(int slot, bool fresh, bool replace = false)
    {
        if (!_atTitle) return;
        string path = SavePath(slot);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Keep the slot locked until the in-memory campaign is closed.
            _slotWriteLease = new FileStream(path + ".lock", FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _message = $"Campaign {slot} is open in another window or its files are unavailable.";
            Render();
            return;
        }

        if (fresh && !replace && HasSave(path))
        {
            ReleaseSaveSlot();
            _message = $"Campaign {slot} now has a save. Continue it or confirm replacement.";
            Render();
            return;
        }

        _saveSlot = slot;
        _oversightSeed = Random.Shared.Next();
        _seed = Random.Shared.Next();
        _campaign = new Campaign(_seed, oversightSeed: _oversightSeed, variableWorld: true);
        _saveVersion = CurrentSaveVersion;
        _commands.Clear();
        _calls = 0;
        _speed = 0;
        _elapsed = 0;
        _page = "Story";
        _storyOffset = 0;
        _storyElection = false;
        _state = "MI";
        _greatPower = "Russia";
        _archiveYear = 0;
        _storyArchive = true;
        _senate = true;
        _simulationFault = false;
        _message = "";
        _atTitle = false;
        _helpOpen = false;
        _primarySaveValid = false;
        if (fresh && !Save(newCampaign: true))
        {
            ReleaseSaveSlot();
            _atTitle = true;
            _message = $"Campaign {slot} could not start. Check that the save folder is writable.";
        }
        else if (!fresh && !Restore())
        {
            ReleaseSaveSlot();
            _atTitle = true;
            _message = $"Campaign {slot} has no readable autosave. Its files were preserved.";
        }
        Render();
    }

    private void ConfirmReplacement(int slot)
    {
        var dialog = new ConfirmationDialog
        {
            Title = $"Replace campaign {slot}",
            DialogText = $"Start a new presidency in campaign {slot}? This replaces the active autosave in that slot.",
            OkButtonText = "Replace campaign"
        };
        AddChild(dialog);
        dialog.Confirmed += () => OpenCampaign(slot, fresh: true, replace: true);
        dialog.VisibilityChanged += () => { if (!dialog.Visible) dialog.QueueFree(); };
        dialog.PopupCentered();
    }

    private void LoadDisplayPreference()
    {
        var settings = new ConfigFile();
        if (settings.Load(SettingsFile) == Error.Ok)
            _fullscreen = settings.GetValue("display", "fullscreen", false).AsBool();
        ApplyDisplayMode();
    }

    private void ToggleFullscreen()
    {
        _fullscreen = !_fullscreen;
        ApplyDisplayMode();
        var settings = new ConfigFile();
        settings.SetValue("display", "fullscreen", _fullscreen);
        if (settings.Save(SettingsFile) != Error.Ok) _message = "Display preference could not be saved.";
        Render();
    }

    private void ApplyDisplayMode()
    {
        if (DisplayServer.GetName() != "headless")
            DisplayServer.WindowSetMode(_fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
    }

    private void Provision(Container parent, Measure measure, string title)
    {
        Button(parent, (_campaign.HasMeasure(measure) ? "✓  " : "○  ") + title,
            () => { Perform(new Command("provision", (int)measure)); Render(); }, !_campaign.CanEdit);
    }

    private void Action(Container parent, string title, Func<bool> action, bool disabled = false, bool primary = false)
    {
        Button(parent, title, () =>
        {
            if (!action()) _message = "That action is currently unavailable.";
            Render();
        }, disabled, primary: primary);
    }


    private bool Perform(Command command)
    {
        _message = "";
        if (!Apply(_campaign, command)) return false;
        _commands.Add(command);
        if (command.Kind == "second-term")
        {
            _calls = 0;
            _storyOffset = 0;
            _page = _saveVersion >= 6 || _storyElection ? "Story" : "Oval Office";
            _storyElection = false;
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
            case "enable-locked-trade-terms": return campaign.EnableLockedTradeTerms();
            case "enable-scoped-wartime-story": return campaign.EnableScopedWartimeStory();
            case "great-power-pressure": return campaign.PressGreatPower(command.Code);
            case "great-power-channel": return campaign.OpenCrisisHotline(command.Code);
            case "great-power-relief": return campaign.FundWartimeRelief();
            case "great-power-summit": return campaign.CallCeasefireSummit();
            case "enable-defense-scopes": return campaign.ScheduleDefenseScopeUpgrade();
            case "defense-support": return campaign.OrderCivilianSupport();
            case "defense-presence": return campaign.OrderProtectivePresence();
            case "defense-authorize": return campaign.RequestDefenseAuthorization();
            case "defense-withdraw": return campaign.WithdrawDefenseMission();
            case "enable-extended-story": return campaign.EnableExtendedStory();
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
        new(_saveVersion, _seed, _calls, _commands, _oversightSeed, _page, _state, _greatPower, _archiveYear, _storyArchive,
            _storyElection, new CampaignSummary(_campaign.Date, _campaign.Term,
                _campaign.Removed ? "Removed from office" : _campaign.CanBeginSecondTerm ? "Second oath available"
                    : _campaign.Finished ? "Term complete" : ElectionCallsPending ? "Election night" : "In progress"));

    private bool Save(bool newCampaign = false)
    {
        string target = SavePath(_saveSlot);
        string operation = Guid.NewGuid().ToString("N");
        string temporary = target + $".tmp-{operation}";
        string stagedBackup = temporary + ".backup";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(CaptureSave()));
            if (newCampaign)
                CommitNewSave(target, temporary, stagedBackup, operation);
            else if (_primarySaveValid && File.Exists(target))
                File.Replace(temporary, target, target + ".backup");
            else
            {
                if (File.Exists(target))
                    File.Copy(target, target + $".unreadable-{operation}");
                File.Move(temporary, target, true);
            }
            _primarySaveValid = true;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (newCampaign && _primarySaveValid)
            {
                _message = "Campaign saved. The first backup checkpoint could not be written.";
                return true;
            }
            _message = "Autosave failed. The campaign is still running.";
            return false;
        }
        finally
        {
            DeleteTemporarySave(temporary);
            if (newCampaign) DeleteTemporarySave(stagedBackup);
        }
    }

    private void CommitNewSave(string target, string temporary, string stagedBackup, string operation)
    {
        string backup = target + ".backup";
        string previousBackup = backup + $".replaced-{operation}";
        File.Copy(temporary, stagedBackup);
        bool hadBackup = File.Exists(backup);
        if (hadBackup) File.Move(backup, previousBackup);

        try
        {
            if (File.Exists(target))
                File.Replace(temporary, target, target + $".replaced-{operation}");
            else
                File.Move(temporary, target);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (hadBackup) File.Move(previousBackup, backup);
            throw;
        }

        _primarySaveValid = true;
        File.Move(stagedBackup, backup);
    }

    private static void DeleteTemporarySave(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            GD.PrintErr($"Temporary save cleanup failed: {error.Message}");
        }
    }

    private static Campaign Replay(SaveData? save)
    {
        if (save is not { Version: >= 1 and <= CurrentSaveVersion, Commands: not null })
            throw new System.IO.InvalidDataException("Unsupported or incomplete campaign save.");
        var campaign = new Campaign(save.Seed, oversightEnabled: save.Version >= 2, oversightSeed: save.OversightSeed,
            agendaEnabled: save.Version >= 3, variableWorld: save.Version >= 4, conversationsEnabled: save.Version >= 5, extendedStoryEnabled: save.Version >= 6,
            scopedDefenseAuthorization: save.Version >= 7, lockedTradeTerms: save.Version >= 8, scopedWartimeStory: save.Version >= 9);
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

    private bool Restore()
    {
        string target = SavePath(_saveSlot);
        var primary = ReadSave(target);
        _primarySaveValid = primary.HasValue;
        var loaded = primary ?? ReadSave(target + ".backup");
        if (loaded is not { } checkpoint) return false;

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
        _storyElection = save.StoryElection;
        if (campaign.Finished)
            _page = "Decision Desk";
        else if (save.Page == "Story")
            _page = "Story";
        else if (ElectionCallsPending)
            _page = "Decision Desk";
        else if (campaign.OnDesk)
            _page = "Oval Office";
        else if (save.Page is "Oval Office" or "Capitol Hill" or "Situation Room" or "Decision Desk" or "Campaign Record" or "Pentagon" or "Great Powers")
            _page = save.Page;
        else
            _page = campaign.Results.Count > 0 ? "Decision Desk" : save.Version >= 6 ? "Story" : "Oval Office";
        if (!campaign.OversightEnabled)
            Perform(new Command("enable-oversight", _oversightSeed));
        if (!campaign.AgendaEnabled)
            Perform(new Command("enable-agenda"));
        if (!campaign.Finished || campaign.CanBeginSecondTerm)
        {
            if (!campaign.ConversationsEnabled)
                Perform(new Command("enable-conversations"));
            if (!campaign.ExtendedStoryEnabled)
                Perform(new Command("enable-extended-story"));
            if (!campaign.DefenseScopeUpgradeConfigured)
                Perform(new Command("enable-defense-scopes"));
            if (!campaign.LockedTradeTerms)
                Perform(new Command("enable-locked-trade-terms"));
            if (!campaign.ScopedWartimeStory)
                Perform(new Command("enable-scoped-wartime-story"));
        }
        if (primary == null && _message == "")
            _message = "The latest autosave was unreadable. Your previous checkpoint was restored.";
        return true;
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

    private static VBoxContainer Card(Container parent, float weight = 1, Color? background = null, int padding = 18)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = weight };
        panel.AddThemeStyleboxOverride("panel", Box(background ?? new Color("#131e30"), padding));
        parent.AddChild(panel);
        return Column(panel);
    }

    private static void Metric(Container parent, string name, string value)
    {
        var card = Card(parent, padding: 12);
        Text(card, name, 11, Muted, wrap: false);
        Text(card, value, 23, Ink, wrap: false);
    }

    private static void Rule(Container parent) => parent.AddChild(new HSeparator());

    private static Label Text(Container parent, string value, int size, Color color, bool wrap = true)
    {
        var label = new Label { Text = value, AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
        return label;
    }

    private static Button Button(Container parent, string title, System.Action click, bool disabled = false,
        Color? color = null, bool primary = false)
    {
        var button = new Button { Text = title, Disabled = disabled };
        button.AddThemeFontSizeOverride("font_size", 15);
        var background = primary ? Gold : color ?? Slate;
        var foreground = primary ? DarkInk : Ink;
        button.AddThemeColorOverride("font_color", foreground);
        button.AddThemeColorOverride("font_hover_color", foreground);
        button.AddThemeColorOverride("font_pressed_color", foreground);
        button.AddThemeStyleboxOverride("normal", Box(background, 8));
        button.AddThemeStyleboxOverride("hover", Box(background.Lightened(.15f), 8));
        button.AddThemeStyleboxOverride("pressed", Box(background.Darkened(.12f), 8));
        button.AddThemeStyleboxOverride("disabled", Box(new Color("#172235"), 8));
        var focus = Box(Colors.Transparent, 8);
        focus.BorderColor = Gold;
        focus.SetBorderWidthAll(2);
        button.AddThemeStyleboxOverride("focus", focus);
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
        public IReadOnlyList<Vote> Votes = [];
        public string Selected = "";
        public System.Action<string> OnSeat;
        private readonly List<(Vector2 Point, Vote Vote)> _points = [];

        public override void _Draw()
        {
            _points.Clear();
            int rows = Votes.Count > 100 ? 12 : 6;
            float spacing = Votes.Count > 100 ? 5.1f : 7f;
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
