namespace GeopoliticsSim.Core;

public enum DefenseMission { None, CivilianSupport, ProtectivePresence, Withdrawal }
public enum DefenseAuthorization { None, Pending, Approved, Denied }
public record DefenseRollCall(int HouseYes, int SenateYes)
{
    public const int HouseTarget = 218;
    public const int SenateTarget = 60;
    public bool Approved => HouseYes >= HouseTarget && SenateYes >= SenateTarget;
}

public sealed partial class Campaign
{
    private const int SupportCost = 6;
    private const int PresenceCost = 12;
    private const int AuthorizationCost = 5;
    private int _defenseWindowEnds;
    private string _defenseWindowTitle = "";
    private string _missionTitle = "";
    private int _missionDay;
    private int _missionEnds;
    private int _authorizationVoteDay;
    private int _warPowersReportDay;
    private int _defenseWindowId;
    private int _missionWindowId;
    private int _authorizationWindowId;
    private string _authorizationTitle = "";
    private readonly Dictionary<int, DefenseRollCall> _authorizationRollCalls = [];
    private DefenseAuthorization _authorization;
    private DefenseRollCall? _lastDefenseVote;
    private bool _defenseScopeUpgradeScheduled;
    private bool _civilianMissionOrdered;
    private bool _presenceOrdered;

    public DefenseMission Mission { get; private set; }
    public bool ScopedDefenseAuthorization { get; private set; }
    public bool DefenseScopeUpgradeConfigured => ScopedDefenseAuthorization || _defenseScopeUpgradeScheduled;
    private int AuthorizationScope => Mission == DefenseMission.ProtectivePresence ? _missionWindowId : _defenseWindowId;
    public DefenseRollCall? DefenseVote => ScopedDefenseAuthorization
        ? _authorizationRollCalls.GetValueOrDefault(AuthorizationScope) : _lastDefenseVote;
    public DefenseAuthorization Authorization => !ScopedDefenseAuthorization ? _authorization
        : _authorization == DefenseAuthorization.Pending && _authorizationWindowId == AuthorizationScope ? DefenseAuthorization.Pending
        : DefenseVote is { } vote ? (vote.Approved ? DefenseAuthorization.Approved : DefenseAuthorization.Denied)
        : DefenseAuthorization.None;
    public int Readiness { get; private set; } = 86;
    public string DefenseContext => Mission != DefenseMission.None ? _missionTitle : _defenseWindowTitle;
    public string DefenseWindowTitle => _defenseWindowTitle;
    public string DefenseAuthorizationContext => Mission == DefenseMission.ProtectivePresence ? _missionTitle : _defenseWindowTitle;
    public string? OtherPendingAuthorization => ScopedDefenseAuthorization && _authorization == DefenseAuthorization.Pending
        && _authorizationWindowId != AuthorizationScope ? _authorizationTitle : null;
    public bool DefenseWindowOpen => !Finished && Day < _defenseWindowEnds;
    public DateTime? AuthorizationVoteDate => Authorization == DefenseAuthorization.Pending
        ? Inauguration.AddDays(_authorizationVoteDay) : null;
    public DateTime? MissionReviewDate => Mission == DefenseMission.None ? null : Inauguration.AddDays(_missionEnds);
    public int? WarPowersDaysRemaining => Mission == DefenseMission.ProtectivePresence && Authorization != DefenseAuthorization.Approved
        ? Math.Max(0, _missionDay + 60 - Day) : null;
    public bool CanOrderCivilianSupport => DefenseWindowOpen && Mission == DefenseMission.None
        && !_civilianMissionOrdered && Readiness >= 35 && Capital >= SupportCost;
    public bool CanOrderProtectivePresence => DefenseWindowOpen && Mission == DefenseMission.None
        && !_presenceOrdered && Readiness >= 55 && Capital >= PresenceCost;
    public bool CanRequestAuthorization => !Finished && (DefenseWindowOpen || Mission == DefenseMission.ProtectivePresence)
        && _authorization != DefenseAuthorization.Pending && Authorization == DefenseAuthorization.None && Capital >= AuthorizationCost;
    public bool CanWithdraw => !Finished && Mission is DefenseMission.CivilianSupport or DefenseMission.ProtectivePresence;

    public string JointChiefsAssessment => Readiness switch
    {
        >= 80 => "The Joint Chiefs assess that available forces can sustain a limited commitment. Keeping it in place will reduce readiness elsewhere.",
        >= 55 => "The Joint Chiefs warn that a new commitment would leave fewer forces free to answer a second crisis.",
        _ => "The Joint Chiefs advise against new commitments. Units need time to recover before a protective presence is available."
    };

    public bool ScheduleDefenseScopeUpgrade()
    {
        if (DefenseScopeUpgradeConfigured) return false;
        _defenseScopeUpgradeScheduled = true;
        UpgradeDefenseScopeIfIdle();
        return true;
    }

    private void UpgradeDefenseScopeIfIdle()
    {
        // A resumed legacy commitment can finish under the rules used to replay it.
        if (!_defenseScopeUpgradeScheduled || Mission != DefenseMission.None || _authorization == DefenseAuthorization.Pending)
            return;
        ScopedDefenseAuthorization = true;
        _defenseScopeUpgradeScheduled = false;
    }

    private void OpenDefenseWindow(string title)
    {
        _defenseWindowId++;
        _defenseWindowTitle = title;
        _defenseWindowEnds = Day + 48;
        _civilianMissionOrdered = false;
        _presenceOrdered = false;
        if (Mission == DefenseMission.None && _authorization != DefenseAuthorization.Pending)
        {
            _authorization = DefenseAuthorization.None;
            _lastDefenseVote = null;
        }
    }

    public bool OrderCivilianSupport()
    {
        if (!CanOrderCivilianSupport || !Spend(SupportCost)) return false;
        _civilianMissionOrdered = true;
        Mission = DefenseMission.CivilianSupport;
        _missionTitle = _defenseWindowTitle;
        _missionDay = Day;
        _missionEnds = Day + 21;
        Readiness -= 7;
        AddBrief("You approved military support for the civilian relief effort. State remains in charge. The Joint Chiefs expect the commitment to last 21 days.");
        return true;
    }

    public bool OrderProtectivePresence()
    {
        if (!CanOrderProtectivePresence || !Spend(PresenceCost)) return false;
        _presenceOrdered = true;
        Mission = DefenseMission.ProtectivePresence;
        _missionTitle = _defenseWindowTitle;
        _missionWindowId = _defenseWindowId;
        _missionDay = Day;
        _warPowersReportDay = Day + 2;
        _missionEnds = Day + (Authorization == DefenseAuthorization.Approved ? 100 : 60);
        Readiness -= 15;
        AddBrief(Authorization == DefenseAuthorization.Approved
            ? "You committed a carrier group to a limited protective presence under the authority Congress granted. The civilian mission remains under State Department coordination."
            : "You committed a carrier group to a limited protective presence in a situation the administration expects may involve hostilities. The War Powers reporting and withdrawal clocks have started.");
        return true;
    }

    public bool RequestDefenseAuthorization()
    {
        if (!CanRequestAuthorization || !Spend(AuthorizationCost)) return false;
        _authorization = DefenseAuthorization.Pending;
        _authorizationWindowId = AuthorizationScope;
        _authorizationTitle = DefenseAuthorizationContext;
        _authorizationVoteDay = Day + 15;
        AddBrief("You requested a limited authorization for the protective presence. Leadership will bring it to the House and Senate in 15 days.");
        return true;
    }

    public bool WithdrawDefenseMission()
    {
        if (!CanWithdraw) return false;
        BeginDefenseWithdrawal("You instructed the Pentagon to end the commitment. The departments will keep working on civilian arrangements.");
        return true;
    }

    private void BeginDefenseWithdrawal(string briefing, bool decision = false)
    {
        Mission = DefenseMission.Withdrawal;
        _missionDay = Day;
        _missionEnds = Day + 14;
        AddBrief(briefing, decision);
    }

    private void TickDefense()
    {
        if (Day == _warPowersReportDay)
            AddBrief("The White House transmitted its War Powers report to Congress within 48 hours.");
        if (_authorization == DefenseAuthorization.Pending && Day == _authorizationVoteDay)
            ResolveDefenseAuthorization();

        UpgradeDefenseScopeIfIdle();
        if (Mission == DefenseMission.None)
        {
            if (Day % 7 == 0) Readiness = Math.Min(100, Readiness + 1);
            return;
        }

        if (Mission == DefenseMission.ProtectivePresence)
        {
            int deployedDays = Day - _missionDay;
            if (deployedDays % 8 == 0)
                Readiness = Math.Max(0, Readiness - 2);
            if (WarPowersDaysRemaining == 15)
                AddBrief("White House Counsel warns that the 60-day War Powers clock has 15 days left. Continued presence requires authorization.", true);
        }

        if (Day != _missionEnds) return;
        switch (Mission)
        {
            case DefenseMission.CivilianSupport:
                Mission = DefenseMission.None;
                _pressure = Math.Max(.1, _pressure - .32);
                Approval = Math.Min(85, Approval + .9);
                AddStory("Back to civilian agencies", "State closes the military support request. Local reception teams take over the work still underway. Maya’s briefing separates the people already served from the cases that have yet to be closed.");
                AddBrief("The temporary civilian support mission ended. State will manage the remaining requests.", true);
                break;
            case DefenseMission.ProtectivePresence:
                if (Authorization != DefenseAuthorization.Approved)
                {
                    Capital = Math.Max(0, Capital - 6);
                    BeginDefenseWithdrawal("The 60-day War Powers deadline arrived without authorization. The White House certified a short safety extension solely for withdrawal and informed Congress. Congressional leaders criticized the delay.", true);
                }
                else
                    BeginDefenseWithdrawal("The authorized commitment reached its review date. You begin an orderly withdrawal while State continues talks with civilian partners.", true);
                break;
            case DefenseMission.Withdrawal:
                Mission = DefenseMission.None;
                AddBrief("The Pentagon reports that withdrawal is complete. Readiness will recover as the commitment winds down.", true);
                break;
        }
        UpgradeDefenseScopeIfIdle();
    }

    private void ResolveDefenseAuthorization()
    {
        double support = (Approval - 50) * .006 + MidtermShift * .04;
        int house = _seats.Count(seat => !seat.Senator && SupportsDefenseAuthorization(seat, support));
        int senate = _seats.Count(seat => seat.Senator && SupportsDefenseAuthorization(seat, support));
        var vote = new DefenseRollCall(house, senate);
        _lastDefenseVote = vote;
        _authorizationRollCalls[_authorizationWindowId] = vote;
        _authorization = vote.Approved ? DefenseAuthorization.Approved : DefenseAuthorization.Denied;
        if (vote.Approved && Mission == DefenseMission.ProtectivePresence
            && (!ScopedDefenseAuthorization || _missionWindowId == _authorizationWindowId))
            _missionEnds = _missionDay + 100;

        string measure = ScopedDefenseAuthorization ? $"The limited authorization for {_authorizationTitle}" : "The limited authorization";
        AddBrief($"{measure} drew {house} House votes and {senate} Senate votes. "
            + (vote.Approved
                ? "Congress approved the commitment."
                : "The measure fell short. The War Powers deadline still applies to this commitment."), true);
        AddStory("The authorization vote", vote.Approved
            ? "Mercer sends the signed resolution with the review date marked. The members who voted for it want a fresh readiness report before another commitment comes up."
            : "Mercer brings the roll calls. Several members who voted against the authorization ask when State can appear before the committee to discuss what happens next.");
    }

    private static bool SupportsDefenseAuthorization(Seat seat, double support) =>
        seat.Persuadability <= (seat.Government ? .78 : .41) + support;
}
