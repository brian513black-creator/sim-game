namespace GeopoliticsSim.Core;

public enum OfferResponse { Decline, Report, Accept }
public enum OversightStage { None, UnderReview, Hearings, SenateTrial, Closed, Removed }

public record ImproperOffer(string Source, string Request, bool Foreign, int Capital, DateTime Deadline);
public record OversightVote(int Yes, int Members, int Threshold);

public sealed partial class Campaign
{
    private static readonly (string Source, string Request, bool Foreign)[] InfluenceSources =
    [
        ("A freight industry donor", "A private benefit in exchange for favorable treatment in an upcoming freight rule.", false),
        ("A federal contractor", "A private benefit in exchange for White House intervention in a federal contract.", false),
        ("An energy lobbyist", "A private benefit in exchange for a regulatory exemption.", false),
        ("An intermediary for a foreign ministry", "A private benefit in exchange for favorable treatment in a trade dispute.", true),
        ("A foreign state-owned exporter", "A private benefit in exchange for White House support for its market-access request.", true),
        ("A visiting foreign delegation", "A personal favor tied to a request for preferential import treatment.", true)
    ];

    private Random _oversightRandom = null!;
    private int _nextOffer;
    private int _reviewDay;
    private double _evidence;
    private int _acceptedOffers;

    public bool OversightEnabled { get; private set; }
    public ImproperOffer? PendingOffer { get; private set; }
    public OversightStage Oversight { get; private set; }
    public bool Removed => Oversight == OversightStage.Removed;
    public int Impeachments { get; private set; }
    public OversightVote? LastHouseOversightVote { get; private set; }
    public OversightVote? LastSenateOversightVote { get; private set; }

    public bool EnableOversight(int seed)
    {
        if (OversightEnabled) return false;
        OversightEnabled = true;
        // A separate stream keeps established election and crisis replays stable.
        _oversightRandom = new Random(seed ^ unchecked((int)0xB03AC519));
        ScheduleOffer(65, 250);
        return true;
    }

    public bool RespondToOffer(OfferResponse response)
    {
        if (PendingOffer is not { } offer || Finished || Date >= offer.Deadline) return false;
        switch (response)
        {
            case OfferResponse.Decline:
                AddBrief($"You refused the improper offer. Source on record: {offer.Source}. No agreement was made.");
                break;
            case OfferResponse.Report:
                Approval = Math.Min(85, Approval + .5);
                AddBrief($"White House Counsel logged and referred the attempted inducement. Source on record: {offer.Source}.");
                break;
            case OfferResponse.Accept:
                _acceptedOffers++;
                int gained = Math.Min(100 - Capital, offer.Capital);
                Capital += gained;
                _evidence = Math.Min(1, _oversightRandom.NextDouble() * .85 + (offer.Foreign ? .15 : .05)
                    + Math.Min(3, _acceptedOffers - 1) * .12);
                _reviewDay = Day + _oversightRandom.Next(18, 58);
                Oversight = OversightStage.UnderReview;
                LastHouseOversightVote = null;
                LastSenateOversightVote = null;
                AddBrief($"You accepted the improper offer and gained {gained} political capital. White House Counsel warns that accepting may trigger an investigation.");
                break;
            default:
                return false;
        }
        PendingOffer = null;
        ScheduleOffer(100, 290);
        return true;
    }

    private void ScheduleOffer(int minimumDays, int maximumDays) =>
        _nextOffer = Day + _oversightRandom.Next(minimumDays, maximumDays + 1);

    private void TickOversight()
    {
        if (!OversightEnabled || Finished) return;
        if (PendingOffer is { } pending && Date >= pending.Deadline)
        {
            PendingOffer = null;
            ScheduleOffer(100, 290);
            AddBrief("The improper offer expired without an agreement.");
        }

        if (Day == _reviewDay)
        {
            switch (Oversight)
            {
                case OversightStage.UnderReview:
                    BeginHearings();
                    break;
                case OversightStage.Hearings:
                    VoteOnImpeachment();
                    break;
                case OversightStage.SenateTrial:
                    VoteOnRemoval();
                    break;
            }
        }

        if (Finished || Day < _nextOffer || PendingOffer != null
            || Oversight is OversightStage.UnderReview or OversightStage.Hearings or OversightStage.SenateTrial
            || Date.AddDays(120) >= CurrentTermEnd || Date == CurrentElectionDay)
            return;

        var source = InfluenceSources[_oversightRandom.Next(InfluenceSources.Length)];
        int capital = _oversightRandom.Next(10, 19);
        string sender = source.Foreign && Current.Ally != "United States" ? $"{source.Source} · {Current.Ally}" : source.Source;
        PendingOffer = new ImproperOffer(sender, source.Request, source.Foreign, capital, Date.AddDays(10));
        AddBrief($"A confidential memo reports an improper offer. Source: {sender}. Consult White House Counsel in the Oval Office.", true);
    }

    private void BeginHearings()
    {
        // An offer may never become public. Weak public cases can end before a floor vote.
        if (_evidence < .29)
        {
            Oversight = OversightStage.Closed;
            return;
        }

        Approval = Math.Max(15, Approval - (3 + _evidence * 6));
        Capital = Math.Max(0, Capital - 7);
        if (_evidence < .51)
        {
            Oversight = OversightStage.Closed;
            AddBrief("The improper arrangement became public. The disclosure cost approval and political capital, and the committee closed its inquiry without pursuing an impeachment vote.", true);
            return;
        }

        Oversight = OversightStage.Hearings;
        _reviewDay = Day + _oversightRandom.Next(11, 22);
        AddBrief("A House committee opened televised hearings into President Banks’s acceptance of an improper benefit. The full House will vote on an article of impeachment.", true);
    }

    private void VoteOnImpeachment()
    {
        int votes = Math.Clamp((int)Math.Round(150 + _evidence * 105
            + (50 - Approval) * 2 + _oversightRandom.Next(-16, 17)), 0, 435);
        LastHouseOversightVote = new OversightVote(votes, 435, 218);
        if (votes < 218)
        {
            Oversight = OversightStage.Closed;
            AddBrief($"The House rejected the article of impeachment, {votes}–{435 - votes}. President Banks remains in office.", true);
            return;
        }

        Impeachments++;
        Oversight = OversightStage.SenateTrial;
        Capital = Math.Max(0, Capital - 10);
        _reviewDay = Day + _oversightRandom.Next(13, 26);
        AddBrief($"The House impeached President Banks, {votes}–{435 - votes}. The presidency continues during the Senate trial. With all senators present, conviction requires 67 votes.", true);
    }

    private void VoteOnRemoval()
    {
        int votes = Math.Clamp((int)Math.Round(36 + _evidence * 27
            + (50 - Approval) * .65 + _oversightRandom.Next(-7, 8) + TrialRecordEffect), 0, 100);
        LastSenateOversightVote = new OversightVote(votes, 100, 67);
        Oversight = votes >= 67 ? OversightStage.Removed : OversightStage.Closed;
        AddBrief(Removed
            ? $"The Senate voted {votes}–{100 - votes} to convict. Brian Banks is removed from office, and Vice President Jordan Reed takes the oath."
            : $"The Senate voted {votes}–{100 - votes} to convict, falling short of the required 67. Brian Banks was acquitted and remains in office.", true);
        if (Removed)
            RecordEpilogue();
        else
            AddStory("The morning after the trial", "Nora brings back the stack of decisions that piled up during the trial. One agency head asks whether tomorrow’s meeting is still on. From the office next door comes the sound of the phones ringing again.");
    }
}
