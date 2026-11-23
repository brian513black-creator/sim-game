namespace GeopoliticsSim.Core;

[Flags]
public enum AgencyTools { None = 0, Trade = 1, Order = 2, Rule = 4, All = Trade | Order | Rule }

public record BillPlan(string Name, string Relief, string Investment, string Offset, string Mandate,
    int ReliefCost, int InvestmentCost, int OffsetSavings, int MandateCost, int FloorLeadDays = 21);

public record AgendaOption(string Label, int CapitalCost, int Days, double Approval, double Relief, double FiscalImpact,
    string Dispatch, int CapitalGain = 0);

public record AgendaEpisode(Crisis Crisis, string Memo, AgencyTools Tools, BillPlan? Bill,
    AgendaOption? First = null, AgendaOption? Second = null, string? Callback = null, int CadenceOffset = 0);

public sealed partial class Campaign
{
    private int _agendaStartIndex = int.MaxValue;
    private int _episodeOpened;
    private int _responseDue;
    private int _agendaDeliveries;
    private AgendaOption? _response;
    private string _responseEpisode = "";
    private readonly Dictionary<string, int> _agendaChoices = [];

    public bool AgendaEnabled => _agendaStartIndex != int.MaxValue;
    public AgendaEpisode? Episode
    {
        get
        {
            if (_crisisIndex < _agendaStartIndex) return null;
            bool secondTermAgenda = Term == 2 && _crisisIndex >= _secondTermCrisis;
            var sequence = secondTermAgenda ? _secondAgendaOrder ?? SecondAgenda : _firstAgendaOrder ?? FirstAgenda;
            int index = secondTermAgenda ? _crisisIndex - _secondTermCrisis : _crisisIndex;
            return index < sequence.Length ? sequence[index] : null;
        }
    }
    public string PackageName => Episode?.Bill?.Name ?? Current.Title;
    public bool HasFloorVote => Episode == null || Episode.Bill != null;
    public bool CanChooseAgenda => !Finished && Episode?.First != null && _response == null
        && !_agendaChoices.ContainsKey(Current.Title) && Day < _episodeOpened + 18;
    public DateTime? PendingAgendaFollowUp => _response == null ? null : Inauguration.AddDays(_responseDue);
    public string AgendaStatus => PendingAgendaFollowUp is { } due
        ? $"Follow-up scheduled for {due:MMM d}."
        : Episode?.First != null && _agendaChoices.ContainsKey(Current.Title) ? "Decision taken. The outcome is in the White House log."
        : Episode?.First != null && Day >= _episodeOpened + 18 ? "The decision window has closed."
        : $"Decision window closes {Inauguration.AddDays(_episodeOpened + 18):MMM d}.";

    private static readonly BillPlan OpeningBill = new("Industrial Bridge Act", "Worker relief", "Production credits", "Deficit offset",
        "Domestic sourcing mandate", 92, 154, 118, 26);

    private BillPlan ActivePlan => Episode?.Bill ?? OpeningBill;

    public bool EnableAgenda()
    {
        if (AgendaEnabled) return false;
        // When upgrading a save, leave the open legislative package exactly as it was.
        _agendaStartIndex = _crisisIndex + 1;
        return true;
    }

    private bool Allows(AgencyTools tool) => Episode == null || Episode.Tools.HasFlag(tool);

    public string ProvisionName(Measure measure) => measure switch
    {
        Measure.WorkerRelief => ActivePlan.Relief,
        Measure.ProductionCredits => ActivePlan.Investment,
        Measure.DeficitOffset => ActivePlan.Offset,
        Measure.DomesticSourcing => ActivePlan.Mandate,
        _ => throw new ArgumentOutOfRangeException(nameof(measure))
    };

    public bool ChooseAgendaOption(int choice)
    {
        if (!CanChooseAgenda) return false;
        var option = choice switch { 1 => Episode?.First, 2 => Episode?.Second, 3 => HardAgendaOption, _ => null };
        if (option == null || !Spend(option.CapitalCost)) return false;
        Capital = Math.Min(100, Capital + option.CapitalGain);
        _agendaChoices.Add(Current.Title, choice);
        _responseEpisode = Current.Title;
        _response = option;
        _responseDue = Day + option.Days;
        AddBrief($"You chose to {option.Label.ToLowerInvariant()}. Follow-up is due {Inauguration.AddDays(_responseDue):MMM d}.");
        return true;
    }

    private void TickAgenda()
    {
        if (Episode?.First != null && Day == _episodeOpened + 18 && !_agendaChoices.ContainsKey(Current.Title))
            AddBrief($"The White House let the decision window pass for {Current.Title}. Agencies continued under existing policy.");
        if (_response == null || Day != _responseDue) return;
        var response = _response;
        _response = null;
        _pressure = Math.Max(.1, _pressure - response.Relief);
        Approval = Math.Clamp(Approval + response.Approval, 15, 85);
        _fiscalImpulse += response.FiscalImpact;
        if (response.Approval >= 0 && (response.Approval > 0 || response.Relief >= .35)) _agendaDeliveries++;
        AddStory($"{_responseEpisode} follow-up", response.Dispatch, decision: true);
    }

    public string? EpisodeMemo => Current.Title switch
    {
        "A Freight Data Agreement" when _agendaChoices.TryGetValue("The War at the Aster Strait", out int wartimeChoice) =>
            wartimeChoice switch
            {
                1 => "The port directors bring the arrival records from the war. The emergency relief lanes worked while everyone was on overtime. They want a data agreement they can keep running during the next disruption.",
                2 => "Several port directors already share schedules under the civilian agreement from the war. They ask whether that arrangement can be extended to ports that were outside it.",
                _ => "Officials from smaller ports bring records of relief cargo that lost its slot during the war. They want the new schedule to show when those shipments are being bumped."
            },
        "The Transition Books" or "The Last Cabinet Meeting" or "The Departing Staff" when Results.Count == 0 =>
            "Career officials ask which program files to prepare for January. The election is still ahead, so the transition office is drawing up material it could give either ticket.",
        "The Transition Books" or "The Last Cabinet Meeting" or "The Departing Staff" when ElectoralVotes == 269 =>
            "With the succession unsettled, agency staff are preparing separate copies of the briefing. The list of deadlines will go to career officials first.",
        _ => Episode?.Memo
    };

    public string? AgendaCallback => Episode?.Callback switch
    {
        "chips" when TookHardChoice("The Inventory Pact") =>
            "The audit shows deliveries arriving first at the largest plants. Elena has brought the independent suppliers’ canceled shift notices to the hearing.",
        "chips" when _agendaChoices.TryGetValue("The Inventory Pact", out int response) => response == 1
            ? "Treasury matched the shared inventory reports to dated delivery logs from individual factories. The auditor asks why those two sets of records were kept separately for so long."
            : "Auditors found paperwork for the emergency reserve you ordered in 2029. They still need shipment data from your trading partners.",
        "chips" => "The audit found that suppliers shared shipment totals during the chip crisis, with no way to verify which factories got them.",
        "water" when TookHardChoice("The River Runs Low") =>
            "Small shippers tell the governors they lost access under the priority policy. The compact negotiations now include a fight over who gets space during the next drought.",
        "water" when _agendaChoices.TryGetValue("The River Runs Low", out int response) => response == 1
            ? "The states brought the voluntary allocation agreement you helped negotiate. They want Congress to put it on a lasting footing."
            : "Governors want the emergency freight grants renewed. The grants kept cargo moving, and the underlying water dispute remains open.",
        "water" => "Governors arrive with separate plans. There is still no federal framework for rationing the river in a dry year.",
        "medicine" when TookHardChoice("Empty Shelves") =>
            "Large hospital networks have stock. Small hospitals bring waiting lists to the hearing and ask whether the next shipment will go to the same buyers.",
        "medicine" when _agendaChoices.TryGetValue("Empty Shelves", out int response) => response == 1
            ? "Hospitals are drawing from the reserve you authorized during the shortage. Auditors found it helps large systems first."
            : "The earlier contract kept several small hospitals supplied until the main distributor recovered. Two regions had to wait.",
        "medicine" => "Hospital networks used their own emergency contracts. Congress still has no consistent measure of how long regional supplies lasted.",
        _ => null
    };

    private double PriorDecisionRelief() => Episode?.Callback switch
    {
        "chips" => _agendaChoices.GetValueOrDefault("The Inventory Pact") switch { 1 => .17, 2 => .27, _ => 0 },
        "water" => _agendaChoices.GetValueOrDefault("The River Runs Low") switch { 1 => .22, 2 => .04, _ => 0 },
        "medicine" when Current.Title == "The Rural Distribution Vote" =>
            _agendaChoices.GetValueOrDefault("Empty Shelves") switch { 1 => .13, 2 => .35, _ => 0 },
        "medicine" => _agendaChoices.GetValueOrDefault("Empty Shelves") switch { 1 => .34, 2 => .18, _ => 0 },
        _ => 0
    };

    private static AgendaEpisode Law(string title, string brief, string ally, double severity, string memo,
        BillPlan plan, AgencyTools tools = AgencyTools.None, string? callback = null, int cadenceOffset = 0) =>
        new(new Crisis(title, brief, ally, severity), memo, tools, plan, Callback: callback, CadenceOffset: cadenceOffset);

    private static AgendaEpisode Decision(string title, string brief, string ally, double severity, string memo,
        AgendaOption first, AgendaOption second, string? callback = null, int cadenceOffset = 0) =>
        new(new Crisis(title, brief, ally, severity), memo, AgencyTools.None, null, first, second, callback, cadenceOffset);

    private static AgendaOption Act(string label, int cost, int days, double approval, double relief, double fiscalImpact,
        string dispatch) => new(label, cost, days, approval, relief, fiscalImpact, dispatch);

    private static BillPlan Plan(string name, string relief, string investment, string offset, string mandate,
        int a, int b, int c, int d, int floorLeadDays = 21) => new(name, relief, investment, offset, mandate, a, b, c, d, floorLeadDays);

    private static readonly AgendaEpisode[] FirstAgenda =
    [
        Law("The Chip Shock", "A semiconductor shortage threatens auto production across the Midwest.", "Taiwan", 2.1,
            "A deputy at State says replacement shipments could buy the plant time. Mercer can move a relief bill if Treasury can tell him who qualifies. Nora puts both of them on the same call and asks them to agree on a date.",
            OpeningBill, AgencyTools.All),
        Decision("The Inventory Pact", "Deliveries have resumed, but suppliers disagree about which inventories are still at risk.", "Taiwan", .95,
            "The Taiwanese negotiator offers weekly counts if U.S. suppliers share theirs. One U.S. supplier says its inventory is a trade secret. Maya has priced a small federal reserve in case the deal breaks down.",
            Act("Join the inventory pact", 7, 22, .9, .42, 0, "Taiwan agreed to share aggregate inventories. Factory buyers adjusted their orders without a new federal outlay."),
            Act("Build a federal reserve", 11, 32, 1.3, .68, .18, "The reserve stabilized orders at several plants. Treasury recorded the carrying cost and its effect on prices.")),
        Decision("A Port Runs Late", "A labor contract expires while freight is already backed up for weeks.", "Mexico", 1.1,
            "The port union agrees to meet a mediator at seven. Shippers need an answer by noon to reroute that week’s cargo. Nora asks who gets to stay at the table if Washington guarantees the detour first.",
            Act("Send a federal mediator", 5, 21, 1.1, .35, 0, "The parties reached an interim labor agreement. Carriers brought back the scheduled sailings."),
            Act("Guarantee substitute freight", 10, 16, .5, .6, .24, "Cargo moved through alternate ports. The underlying contract dispute continued and guarantees added public costs."), cadenceOffset: -17),
        Law("Winter Utility Bills", "Natural gas bills are rising even in counties where payrolls have recovered.", "Canada", 1.42,
            "Maya’s briefing cites a utility in a county whose payrolls just recovered. The utility has offered to wait on late bills if Washington helps with the winter balance. The state commission wants a date when federal support ends.",
            Plan("Home Heating Stabilization Act", "Low-income heating credit", "Weatherization grants", "End expiring fuel subsidy", "Domestic equipment standard", 68, 106, 87, 24), AgencyTools.Trade, cadenceOffset: 18),
        Law("Food Prices at the Checkout", "Poor harvests and expensive freight are pushing grocery prices higher.", "Brazil", 1.26,
            "A farm-state senator brings a ledger of what producers paid to keep grain in storage. An urban member brings a bag with two days of groceries in it. Maya asks which relief can reach people before the next price report.",
            Plan("Food Affordability Act", "Temporary nutrition credit", "Farm storage loans", "Reclaim unused farm guarantees", "Domestic purchase preference", 76, 83, 60, 16), AgencyTools.Rule),
        Decision("The Eastport Review", "A congressional audit asks whether the chip response helped factories or merely protected suppliers.", "Taiwan", .55,
            "The auditor can document which suppliers received support. Several factory names are still missing. Nora wants the hearing in public. Maya asks who will answer if the published spreadsheet points to the wrong plant.",
            Act("Publish the audit and testimony", 6, 18, 1.9, .05, 0, "The audit named shortcomings in the response. Independents in Congress welcomed a public record."),
            Act("Release the verified summary", 3, 12, .3, .12, 0, "Treasury briefed the committee on verified findings. The public summary drew less attention, and questions about factory data persisted."), "chips", cadenceOffset: -13),
        Law("Transformers on Back Order", "Utilities are deferring maintenance because replacement transformers are scarce.", "Japan", 1.3,
            "A rural utility manager tells Mercer their spare transformer has already been lent to a neighboring county. The bill has a match requirement the biggest utilities can meet on day one. Mercer asks whose signature would let the rural managers apply.",
            Plan("Grid Repair Act", "Small utility grants", "Transformer capacity credit", "Retire unused credit authority", "Domestic transformer preference", 70, 148, 103, 28), AgencyTools.Trade, cadenceOffset: 12),
        Decision("The River Runs Low", "Drought drops a major inland shipping route below its normal freight capacity.", "Canada", 1.5,
            "Three governors agree in private that somebody needs to cut water use. Each asks to speak last at the press conference. Barge operators have meanwhile priced a freight detour the federal government could pay for.",
            Act("Broker a water allocation agreement", 8, 29, 1.25, .47, 0, "The governors agreed to share short-term cuts. Some cargo still moved late, but the arrangement held."),
            Act("Authorize emergency freight grants", 11, 14, .8, .64, .2, "The grants moved time-sensitive cargo by road and rail. Governors left the allocation dispute open."), cadenceOffset: -21),
        Decision("An Export License Dispute", "A trading partner delays licenses for battery materials without offering a clear explanation.", "Australia", 1.45,
            "The trade minister will clear cargo already on the books. The next set of orders will face the same rule. State asks whether to bank the waiver now or keep negotiating over what comes after it.",
            Act("Request a temporary waiver", 6, 16, .7, .62, .06, "The waiver covered shipments already contracted. It did not settle what happens to new orders."),
            Act("Negotiate a shared review panel", 9, 30, 1.4, .43, 0, "The review panel published common licensing criteria. Plants had to manage the shortage for longer.")),
        Law("The Credit Hearing", "A tighter credit market is exposing firms that survived on temporary relief.", "European Union", 1.15,
            "Mercer brings a loan officer whose bank is turning down shops that were current a year ago. The members he needs for the bill ask where the guarantee ends and who has to tell those shops when it does.",
            Plan("Community Credit Act", "Small business bridge grants", "Local bank guarantees", "Sunset outstanding guarantees", "Domestic collateral condition", 66, 112, 92, 20), cadenceOffset: 19),
        Law("The Water Compact", "Governors return to Washington to ask for a durable agreement on drought and inland shipping.", "Canada", 1.05,
            "The governors bring a draft with three brackets left in it. Every bracket concerns a county that could lose water in the next dry season. Mercer asks for a roll call before anyone announces a compact.",
            Plan("River Reliability Act", "Drought adjustment grants", "Lock and channel repairs", "Consolidate water programs", "Regional procurement clause", 83, 122, 89, 23, floorLeadDays: 28), callback: "water"),
        Decision("The War at the Aster Strait", "War overseas has displaced civilians and diverted freight. U.S. ports are receiving emergency requests as farmers and manufacturers wait on delayed cargo.", "South Korea", .92,
            "State asks for expedited handling of evacuation arrivals and relief cargo. Treasury proposes a civilian agreement with partner ports that would keep ordinary cargo moving over the longer term. A congressional committee wants an accounting of emergency funds.",
            Act("Prioritize civilian relief and arrivals", 6, 14, .65, .53, .14, "Evacuees reached reception services and relief supplies got through. Maya warns that the special handling costs money and several ordinary shipments are still late."),
            Act("Negotiate a civilian port agreement", 8, 25, 1.1, .38, 0, "Partner ports agreed to set aside space and coordinate schedules for relief and commercial cargo. Several relief shipments had already waited weeks.")),
        Law("The Apprenticeship Hearing", "Returning orders have helped Midwest plants, but employers cannot fill skilled shifts.", "Germany", 1.2,
            "Elena’s apprentice sits in the second row while she testifies. The community college can train another class if it can pay instructors through the summer. Mercer asks the dean to tell the committee how many seats the grant would actually buy.",
            Plan("Industrial Skills Act", "Paid apprentice placements", "Technical college equipment", "Merge overlapping grants", "Local training requirement", 56, 96, 65, 13), AgencyTools.Rule),
        Law("The Emergency Authority Sunset", "Legal authority for several temporary relief programs is about to expire.", "Canada", 1.0,
            "House counsel has marked the exact hour the relief authority expires. A governor’s finance director asks whether deposits already promised for Friday will clear. Treasury wants a bill the agency can use on Monday morning.",
            Plan("Emergency Authority Renewal", "State transition grants", "Reserve procurement authority", "Sunset emergency accounts", "Domestic reserve rule", 77, 101, 89, 15)),
        Decision("Competing Supply Standards", "Two trade blocs are preparing to certify the same industrial parts under incompatible rules.", "European Union", 1.15,
            "A parts maker brings two stamped certificates for the same component. The domestic safety regulator asks to see the inspection records behind the second stamp before the U.S. recognizes it.",
            Act("Open a mutual-recognition pilot", 8, 26, 1.15, .44, 0, "Several categories qualified for a single test. Safety officials kept separate inspections for higher-risk parts."),
            Act("Fund domestic recertification", 6, 15, .55, .53, .12, "Small exporters received reimbursements. Firms still faced two different sets of rules.")),
        Decision("The Plant Reopening", "A Midwest plant announces a restart while former workers are still owed wages.", "Japan", .7,
            "The company has scheduled a ribbon cutting. Elena has a list of workers who will miss it because they’re still owed wages. She wants them in the room. The plant manager says every additional day without parts pushes back new shifts.",
            Act("Hold the worker roundtable", 5, 19, 1.45, .15, 0, "The roundtable put unpaid wages in the public record. The employer agreed to a claim-review schedule."),
            Act("Prioritize the delivery backlog", 7, 13, .45, .52, .04, "Parts cleared and restarts moved ahead. Back-pay claims stayed with the company and labor board.")),
        Law("Temporary Relief, Permanent Costs", "Coalition leaders want to make parts of the relief system permanent before the election.", "Canada", 1.15,
            "Maya brings the permanent-credit estimate with a second column for the next president’s budget. Mercer agrees to move the bill if those figures go out to every member ahead of the vote.",
            Plan("Supply Resilience Reauthorization", "Household stabilization credit", "Resilience grants", "Repeal temporary tax breaks", "Domestic capacity floor", 93, 136, 110, 27)),
        Decision("The Final Budget Brief", "Treasury issues a forecast as the campaign puts the administration’s record under scrutiny.", "United States", .48,
            "Price’s staff has requested the same forecast you’re reading. Nora can schedule a public release by the afternoon. Maya asks for time to take lawmakers through two assumptions people are likely to misread.",
            Act("Release the full budget brief", 5, 12, 1.4, .05, 0, "Reporters compared the forecast to your original relief promises. The numbers drove coverage for several days."),
            Act("Brief both parties in Congress", 4, 18, .75, .12, 0, "Both parties compared notes with Treasury. The joint briefing made the next funding vote easier to explain.")),
        Law("Continuity Funding", "Congress needs to authorize enough funding to keep core programs running during the handover.", "United States", .6,
            "Leadership wants a narrow bill. Agency heads warn that a gap will disrupt payments even while the political transition continues.",
            Plan("Continuity Appropriations Act", "Extend household payments", "Bridge federal operations", "Cancel dormant balances", "Federal purchasing limitation", 55, 94, 78, 17, floorLeadDays: 11)),
        Decision("The Transition Books", "Auditors and agency heads prepare the record for the next inauguration.", "United States", .3,
            "The incoming team asks for the names of the career officials who can keep payments moving. Governors ask for the dates they should tell households. Staff have time to prepare one set of materials first.",
            Act("Publish implementation notes", 4, 12, .9, .02, 0, "Local agencies began planning from the published schedules. The material also became part of the public record."),
            Act("Brief the incoming cabinet", 3, 10, .45, .12, 0, "Department leaders marked every open program with the career official responsible for it on Inauguration Day."))
    ];

    private static readonly AgendaEpisode[] SecondAgenda =
    [
        Decision("Empty Shelves", "A drug ingredient shortage is forcing hospitals to postpone some routine care.", "India", 1.6,
            "Reed has national purchasing agreements ready for review. A hospital director calls during the meeting to ask how many doses will reach her county on that schedule. Her pharmacy has enough for six days.",
            Act("Authorize a national medicine reserve", 11, 30, 1.35, .65, .18, "The reserve reached major distributors first and eased on-hand shortages. Rural providers asked for distribution safeguards."),
            Act("Fund regional hospital contracts", 8, 18, 1.05, .46, .12, "Emergency contracts reached smaller hospitals faster. Two regions remained exposed to their original distributor.")),
        Decision("Fertilizer Before Planting", "Farmers face sharply higher input costs as planting orders come due.", "Canada", 1.5,
            "An Agriculture official has estimates for cheaper imports before planting. A farmer’s co-op sends invoices for orders its members have already placed. They ask the president to decide before the lender calls back.",
            Act("Open a temporary import window", 7, 15, .9, .65, 0, "Import prices fell before planting. Domestic suppliers asked how quickly the window would close."),
            Act("Extend credit through harvest", 9, 25, 1.35, .38, .18, "Credit reached several cash-strapped producers. Input costs stayed high as Treasury assumed more risk.")),
        Law("The Transit Repair Backlog", "Public transit agencies are pulling older vehicles out of service without replacement parts.", "Germany", 1.2,
            "The mayors have a list of fixes. The Senate wants to address parts procurement before underwriting whole fleets.",
            Plan("Transit Reliability Act", "Service continuity grants", "Parts manufacturing credit", "Reallocate dormant transit funds", "Domestic parts condition", 61, 124, 90, 26), AgencyTools.Trade),
        Law("The Critical Minerals Hearing", "A diversion of mineral exports is leaving battery production schedules uncertain.", "Chile", 1.6,
            "Workers from a proposed refinery site are sitting with county officials. They agree it would bring jobs. The county wants the cost of new water treatment in writing before anyone announces a location.",
            Plan("Minerals Processing Act", "Host community assistance", "Refining capacity credit", "Retire overlapping tax credits", "Domestic refining condition", 65, 156, 111, 29), AgencyTools.Trade),
        Decision("The Reserve Stress Test", "A second interruption tests the medicine supply commitments made early in the term.", "India", 1.2,
            "The FDA brings the manifest for the next guaranteed shipment. The supply director can stock the highest-volume warehouses or send a share directly to undersupplied hospitals. She needs the allocation before the trucks are loaded.",
            Act("Prioritize undersupplied hospitals", 8, 19, 1.5, .4, .04, "The revised allocations reached rural hospitals. Several large providers had to defer lower-priority orders."),
            Act("Fill the national reserve first", 6, 26, .55, .64, .12, "Restocking improved national inventory, but individual hospitals waited longer than the aggregate figures suggested."), "medicine", cadenceOffset: -16),
        Law("The Maintenance Guarantee", "Airlines are taking aircraft out of service because replacement parts are late.", "France", 1.25,
            "Regulators reject shortcuts on certification. The House is willing to pay for parts capacity if smaller carriers can qualify.",
            Plan("Commercial Maintenance Act", "Regional route assistance", "Certified repair capacity", "Reclaim unused route guarantees", "Domestic parts requirement", 64, 113, 81, 24), AgencyTools.Rule, cadenceOffset: 16),
        Decision("A Freight Data Agreement", "Shipping lines are filing schedules that local ports can no longer rely on.", "Singapore", 1.1,
            "Trading partners offer to pool port data. Some operators would prefer federal authorities to observe congested routes directly.",
            Act("Join the port data agreement", 7, 27, 1.3, .44, 0, "Ports coordinated arrival windows and congestion eased. Some private carriers still declined to report."),
            Act("Fund domestic port monitoring", 7, 17, .65, .49, .11, "Domestic forecasts improved sooner. Foreign sailing schedules remained difficult to verify.")),
        Decision("The Disclosure Committee", "A congressional commission asks how the administration chose suppliers during the last four years.", "United States", .55,
            "Counsel can send the supplier files to the committee now. Several businesses say the records contain information their competitors could use. Nora asks whether an independent review will earn trust quickly enough to matter.",
            Act("Publish the procurement record", 6, 18, 1.7, .04, 0, "The committee received the public record. Contractors disputed some of the coverage, and public confidence improved."),
            Act("Commission an independent review", 5, 28, 1.05, .08, 0, "Independent reviewers checked supplier records and issued recommendations after a longer wait."), cadenceOffset: -19),
        Law("The Rural Distribution Vote", "The medicine reserve exposed the limits of regional warehouses and hospital purchasing groups.", "India", 1.1,
            "Lawmakers who disagree on the reserve still see a need for more regional storage. The dispute is who can draw from it and when.",
            Plan("Rural Supply Access Act", "Small hospital access grants", "Regional cold-chain facilities", "Consolidate stockpile accounts", "Domestic storage requirement", 74, 110, 88, 22), callback: "medicine", cadenceOffset: 19),
        Law("The Skills Program Renewal", "Technical colleges warn that new training seats will disappear when their grants expire.", "Germany", .94,
            "Elena brings her former apprentice to the hearing. The apprentice asks the committee to count people who are still on the job a year into the program. The colleges ask for enough notice to hire whoever will teach the next class.",
            Plan("Technical Training Renewal Act", "Apprentice completion awards", "Multi-year college grants", "Consolidate training accounts", "Regional labor requirement", 58, 110, 78, 15)),
        Decision("Payment System Delays", "A regional bank disruption leaves some small suppliers waiting on federal payments.", "United States", 1.0,
            "Treasury can speed payment through existing banks or open a narrower process for businesses already at risk of closing.",
            Act("Fund temporary payment capacity", 7, 15, .85, .57, .11, "Treasury processed the backlog faster. Some firms without established banking relationships still waited."),
            Act("Target emergency supplier payments", 6, 22, 1.3, .4, .06, "The most exposed suppliers received payments first. Routine delays took longer to resolve.")),
        Law("Rebuilding the Inland Route", "The next dry season threatens the inland shipping routes that carry grain and components.", "Canada", 1.15,
            "The governors recognize one another’s folders from the first drought meeting. The county engineer brings a new schedule and asks the committee to put grant payments against actual repair dates.",
            Plan("Inland Freight Adaptation Act", "Drought transition aid", "Waterway modernization", "Retire unused loan authority", "Domestic equipment preference", 74, 141, 104, 25), callback: "water"),
        Decision("The Aster Strait Ceasefire", "A fragile ceasefire has reopened some commercial traffic. Insurers and aid organizations want assurances that the reopening will hold.", "European Union", 1.27,
            "State has asked civilian monitors and partner ports to verify the reopened schedules. Treasury has a separate proposal for firms still paying emergency freight costs. The Joint Chiefs ask you to review readiness before taking on any new commitment.",
            Act("Back civilian ceasefire monitoring", 8, 27, 1.3, .52, 0, "Civilian monitors confirmed that several routes remained open. Insurers restored coverage for some shipments while keeping limits on others."),
            Act("Reimburse emergency freight costs", 6, 13, .65, .46, .13, "Reimbursements kept small exporters in the market. Maya warns that the government is absorbing costs as long as the ceasefire remains uncertain.")),
        Decision("Manufacturers Ask for Certainty", "Factories are delaying investments until they know which expiring incentives will survive.", "South Korea", 1.03,
            "Industry requests a short extension. Treasury would rather publish a schedule of deadlines and let companies plan without new spending.",
            Act("Publish the incentive wind-down", 5, 20, 1.05, .26, -.05, "Firms received firm deadlines. Treasury reduced several outstanding commitments, although some projects slipped."),
            Act("Offer bridge guarantees", 9, 17, .95, .59, .18, "Most projects stayed on schedule. The guarantees added costs the next administration will inherit.")),
        Law("The Long-Term Care Vote", "Hospitals say supply stability improved, but long-term care facilities remain exposed to shortages.", "India", 1.12,
            "The committee wants to extend guarantees to care facilities. Some senators want clinics covered under the same bill.",
            Plan("Essential Care Access Act", "Facility continuity grants", "Regional purchasing network", "Unify care supply accounts", "Domestic vendor condition", 72, 119, 92, 19)),
        Law("The Successor’s Budget", "Reed is campaigning while Congress writes the budget the next president will inherit.", "United States", .82,
            "Reed wants to see what funds will be left on day one if they win. The opposition’s policy director asks for the same briefing. Maya suggests giving them the same binder while there’s still time to change it.",
            Plan("Government Continuity Budget", "Extend benefit authority", "Fund core agency operations", "Rescind dormant funding", "Federal purchasing condition", 83, 113, 107, 20)),
        Decision("The Final Data Release", "Independent analysts want a complete account of the administration’s economic and supply records.", "United States", .48,
            "Nora favors releasing the full series now. Agency heads want to publish verified measures first and finish reviewing the rest during transition.",
            Act("Release the full historical series", 5, 14, 1.4, .03, 0, "Analysts compared the published series with household and business surveys. The release clarified the record as the term drew to a close."),
            Act("Release audited measures first", 4, 19, .85, .06, 0, "The audited measures were published first. The final series remained on the transition calendar.")),
        Law("The Final Appropriation", "Congress faces one last funding deadline as department teams prepare for a handover.", "United States", .64,
            "Career staff have drawn up a narrow continuity bill. The floor coalition may seek to attach provisions that would bind the next administration.",
            Plan("Agency Handover Appropriation", "Protect scheduled payments", "Fund transfer operations", "Close temporary accounts", "Federal contract limitation", 63, 99, 87, 21, floorLeadDays: 12)),
        Decision("The Last Cabinet Meeting", "Department leaders bring their lists of programs that need decisions after Inauguration Day.", "United States", .31,
            "There is time to brief the incoming cabinet or attach a public record of each program’s status and owner.",
            Act("Brief the incoming cabinet", 4, 12, .8, .07, 0, "Career staff identified the most urgent decisions and the officials a new cabinet could call about them."),
            Act("Publish an open-program register", 4, 12, 1.1, .02, 0, "The public register identified which programs were complete and who was responsible for the ones still open.")),
        Decision("The Departing Staff", "Career officials prepare their final briefing as political appointees clear their desks.", "United States", .22,
            "Two folders are left on the desk. Career staff have marked the programs still waiting on a decision. Nora sets a blank sheet beside their list in case there’s something you want the next president to hear from you.",
            Act("Deposit the implementation archive", 3, 9, .75, .06, 0, "Agency staff indexed the archive and flagged the decisions still awaiting a signature."),
            Act("Leave a letter for the next president", 2, 8, .65, .02, 0, "The letter waits on the Resolute Desk beside the transition office’s briefing book."))
    ];
}
