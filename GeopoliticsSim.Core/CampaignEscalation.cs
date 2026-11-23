namespace GeopoliticsSim.Core;

public record PowerStandoff(string Country, int Rung, string EmbassyReadout);
public enum CrisisTalk { Pressure, Hotline, Summit }

public sealed partial class Campaign
{
    private sealed record ForeignGovernment(string Country, double Resolve);
    private static readonly string[] Powers = ["Russia", "China", "India", "United Kingdom", "France"];
    private readonly Dictionary<string, ForeignGovernment> _governments = [];
    private readonly Dictionary<string, int> _rungs = [];
    private readonly Dictionary<string, int> _lastContact = [];
    private Random _diplomacyRandom = null!;
    private string _crisisCounterpart = "";
    private CrisisTalk _crisisTalk;
    private int _crisisReplyDay;
    private int _lastSummitDay = -100;
    private int _nextWartimeReview;
    private bool _civilianReliefFunded;

    public static IReadOnlyList<string> GreatPowers { get; } = Array.AsReadOnly(Powers);
    public bool WorldWarActive { get; private set; }
    public DateTime? WorldWarBegan { get; private set; }
    public DateTime? LastCeasefire { get; private set; }
    public PowerStandoff PowerBrief(string country)
    {
        var government = _governments[country];
        return new PowerStandoff(country, _rungs.GetValueOrDefault(country), government.Resolve switch
        {
            > .66 => "The embassy expects the government to resist public pressure. Confidence is moderate.",
            > .36 => "The embassy reports competing views inside the government. Confidence is low.",
            _ => "The embassy believes this government wants a diplomatic exit. Confidence is moderate."
        });
    }

    public bool CanPressurePower(string country) => !Finished && !WorldWarActive && _crisisReplyDay == 0
        && Capital >= 6 && _governments.ContainsKey(country) && _rungs.GetValueOrDefault(country) < 10;
    public bool CanOpenHotline(string country) => !Finished && _crisisReplyDay == 0
        && Capital >= 3 && _rungs.GetValueOrDefault(country) > 0;
    public bool CanCallCeasefire => !Finished && WorldWarActive && _crisisReplyDay == 0
        && Capital >= 8 && Day - _lastSummitDay >= 28;
    public bool CanFundWartimeRelief => !Finished && WorldWarActive && !_civilianReliefFunded && Capital >= 10;
    public string? PendingForeignReply => _crisisReplyDay == 0 ? null
        : _crisisTalk == CrisisTalk.Summit
            ? $"Emergency summit response expected {Inauguration.AddDays(_crisisReplyDay):MMM d}."
            : $"Response from {_crisisCounterpart} expected {Inauguration.AddDays(_crisisReplyDay):MMM d}.";

    private void InitializeEscalation(int seed)
    {
        _diplomacyRandom = new Random(seed ^ unchecked((int)0xA4D1C891));
        foreach (string country in Powers)
            _governments.Add(country, new ForeignGovernment(country, _diplomacyRandom.NextDouble()));
    }

    public bool PressGreatPower(string country)
    {
        if (!CanPressurePower(country) || !Spend(6)) return false;
        _rungs[country] = Math.Min(10, _rungs.GetValueOrDefault(country) + 1);
        BeginForeignReply(CrisisTalk.Pressure, country);
        AddBrief($"You issued a public ultimatum to {country}. State warns that a public deadline gives both governments less room to step back.");
        return true;
    }

    public bool OpenCrisisHotline(string country)
    {
        if (!CanOpenHotline(country) || !Spend(3)) return false;
        BeginForeignReply(CrisisTalk.Hotline, country);
        AddBrief($"You opened an emergency diplomatic channel with {country}. State asked both governments to suspend new public demands.");
        return true;
    }

    public bool CallCeasefireSummit()
    {
        if (!CanCallCeasefire || !Spend(8)) return false;
        _lastSummitDay = Day;
        BeginForeignReply(CrisisTalk.Summit, "");
        AddBrief("You called for an emergency conference on a ceasefire. Congress asked for a separate briefing on humanitarian funding.");
        return true;
    }

    public bool FundWartimeRelief()
    {
        if (!CanFundWartimeRelief || !Spend(10)) return false;
        _civilianReliefFunded = true;
        _pressure = Math.Max(.1, _pressure - .55);
        _fiscalImpulse += .18;
        Approval = Math.Min(85, Approval + .7);
        AddStory("The relief account", "Maya opens a civilian relief account and lays out when Congress will see the first statements. Elena asks whether the assistance will reach people who lost shifts when shipments stopped.");
        return true;
    }

    private void BeginForeignReply(CrisisTalk talk, string country)
    {
        _crisisCounterpart = country;
        _crisisTalk = talk;
        _crisisReplyDay = Day + _diplomacyRandom.Next(6, 12);
        if (country.Length > 0) _lastContact[country] = Day;
    }

    private void TickEscalation()
    {
        if (_crisisReplyDay == Day)
        {
            _crisisReplyDay = 0;
            switch (_crisisTalk)
            {
                case CrisisTalk.Pressure: ResolveUltimatum(); break;
                case CrisisTalk.Hotline: ResolveHotline(); break;
                case CrisisTalk.Summit: ResolveCeasefireSummit(); break;
            }
        }
        if (WorldWarActive && Day == _nextWartimeReview)
        {
            _pressure += .82;
            Approval = Math.Max(15, Approval - .7);
            _nextWartimeReview += 28;
            AddBrief("The wartime review records another month of freight disruption, higher household costs, and requests for civilian assistance.", true);
        }
        if (!WorldWarActive && _crisisReplyDay == 0 && Day % 42 == 0)
            foreach (string country in Powers)
                if (_rungs.GetValueOrDefault(country) > 0 && Day - _lastContact.GetValueOrDefault(country) >= 50)
                    _rungs[country]--;
    }

    private void ResolveUltimatum()
    {
        string country = _crisisCounterpart;
        int rung = _rungs[country];
        double defiance = _governments[country].Resolve;
        double roll = _diplomacyRandom.NextDouble();
        int change = roll < .13 && rung < 8 ? -1 : roll < .7 - defiance * .35 ? 1 : 2;
        _rungs[country] = Math.Clamp(rung + change, 0, 10);
        AddBrief(change < 0
            ? $"{country} made a limited concession and asked for private talks. The standoff eased."
            : $"{country} rejected the ultimatum. The embassy moved its escalation assessment to rung {_rungs[country]} of 10.", true);
        if (_rungs.Values.Contains(10) && _rungs.Values.Count(rung => rung >= 7) >= 2)
        {
            WorldWarActive = true;
            WorldWarBegan ??= Date;
            _nextWartimeReview = Day + 28;
            _civilianReliefFunded = false;
            OpenDefenseWindow("Global wartime response");
            _pressure += 2.3;
            Approval = Math.Max(15, Approval - 5);
            Capital = Math.Max(0, Capital - 10);
            AddStory("The morning the war widens", "The great-power confrontations spread into a global war. Nora clears the schedule for a congressional briefing. Treasury starts calling the port directors, while Reed asks State for the list of civilians still waiting to leave.");
            AddBrief("GLOBAL WAR BEGINS. Multiple great powers are involved. Civilian relief and ceasefire diplomacy are available at the Great Powers desk.", true);
        }
        else if (_rungs[country] == 10)
            AddBrief("The dispute is at its highest escalation rung. A second great-power crisis at rung 7 or above could widen it into a global war.");
    }

    private void ResolveHotline()
    {
        string country = _crisisCounterpart;
        double chance = .8 - _governments[country].Resolve * .25;
        if (_diplomacyRandom.NextDouble() < chance)
        {
            _rungs[country] = Math.Max(0, _rungs[country] - 3);
            AddBrief($"{country} kept the channel open and agreed to put public demands aside. Its escalation assessment fell to rung {_rungs[country]}.", true);
        }
        else
            AddBrief($"{country} kept listening but offered no concessions. The escalation assessment is unchanged.", true);
    }

    private void ResolveCeasefireSummit()
    {
        double chance = .86 - _rungs.Values.Count(rung => rung >= 7) * .20;
        if (_diplomacyRandom.NextDouble() < chance)
        {
            WorldWarActive = false;
            LastCeasefire = Date;
            foreach (string country in Powers)
                if (_rungs.ContainsKey(country)) _rungs[country] = Math.Min(4, _rungs[country]);
            _pressure = Math.Max(.1, _pressure - .75);
            AddBrief("The delegations accepted a ceasefire framework. The global war has paused, and agencies have begun assessing the cost of recovery.", true);
            AddStory("The first quiet morning", "Maya brings the first recovery report. Elena asks about the people who stopped calling back during the war. Nora writes down the names before anyone starts talking about a peace dividend.");
        }
        else
            AddBrief("The summit ended without a ceasefire. State recommends reopening individual diplomatic channels to lower tensions before the next conference.", true);
    }
}
