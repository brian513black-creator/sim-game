namespace GeopoliticsSim.Core;

public sealed partial class Campaign
{
    private static readonly DateTime LedgerDate = new(2029, 8, 16);
    private static readonly DateTime DebateDate = new(2032, 9, 21);
    private static readonly DateTime FirstTransitionDate = new(2032, 12, 9);
    private static readonly DateTime LastSignatureDate = new(2036, 12, 8);
    private static readonly DateTime[] ExtendedAppointments =
        [LedgerDate, DebateDate, FirstTransitionDate, LastSignatureDate];
    private int _trialConversationNumber;
    private bool _wartimeAddressOpened;

    public bool ExtendedStoryEnabled { get; private set; }
    public bool ScopedWartimeStory { get; private set; }

    public bool EnableExtendedStory()
    {
        if (ExtendedStoryEnabled) return false;
        ExtendedStoryEnabled = true;
        return true;
    }

    public bool EnableScopedWartimeStory()
    {
        if (ScopedWartimeStory) return false;
        ScopedWartimeStory = true;
        if (!WorldWarActive) CloseWartimeAddress();
        return true;
    }

    private void TickExtendedStory()
    {
        if (!ExtendedStoryEnabled) return;
        if (Date == LedgerDate) OpenLedger();
        else if (Date == DebateDate) OpenDebate();
        else if (Date == FirstTransitionDate) OpenFirstTransition();
        else if (Term == 2 && Date == LastSignatureDate) OpenLastSignature();
        else if (_conversation == null && _conversationReply == null
            && !StoryAppointmentSoon())
        {
            if (_trialConversationNumber == 0 && Oversight == OversightStage.SenateTrial && _reviewDay - Day >= 8)
                OpenTrialRecord();
            else if (!_wartimeAddressOpened && WorldWarActive
                && (ScopedWartimeStory ? WorldWarBegan : _firstWorldWarBegan) is { } began
                && (Date - began).Days >= 14 && (CurrentTermEnd - Date).Days >= 35)
                OpenWartimeAddress();
        }
    }

    private bool StoryAppointmentSoon()
    {
        bool Soon(DateTime appointment) => appointment > Date && appointment <= Date.AddDays(34);
        return Soon(Inauguration.AddDays(28)) || Soon(HearingDate) || Soon(EmptyChairDate) || Soon(EndorsementDate)
            || ExtendedAppointments.Any(Soon);
    }

    private void OpenTrialRecord()
    {
        _trialConversationNumber = Impeachments;
        OpenConversation(ConversationId.TrialRecord, "The Record Before the Senate", CampaignStory.ChiefOfStaff,
            "Reed is standing by the window when Nora lays out the Senate schedule. An investigator has asked for the same records the White House offered to brief in private. Reed asks what senators will be able to read on their way into the chamber.",
            new("Release the relevant records", "Counsel can put the documents on record. The vote will still be decided by senators.", -6, .9,
                "Counsel posts the documents and identifies what was redacted. Reed asks for a copy of the public version and takes that copy to the next briefing."),
            new("Answer senators in writing", "Counsel can answer specific questions. The rest of the record stays with the Senate.", -3, .35,
                "Written answers are filed. Nora reads through the pages looking for the question Reed asked. It appears twice, worded differently."),
            new("Denounce the inquiry", "Party allies will rally to the statement. Some undecided senators intend to hear the evidence first.", 7, -.85,
                "The statement leads several broadcasts. Senators who expected documents ask Counsel when they will receive them. Reed’s office holds its own briefing that afternoon."));
    }

    private void OpenWartimeAddress()
    {
        _wartimeAddressOpened = true;
        OpenConversation(ConversationId.WartimeAddress, "The Address from the Oval Office", "Maya Shah, Treasury Secretary",
            "Maya has two pages for the broadcast. One shows the price of a week of disrupted freight. The other is a list of civilian relief requests. Nora asks what the country should hear first. State wants enough room left in the speech to keep talking to foreign governments.",
            new("Lead with civilian relief", "People waiting on assistance will hear where to call. State can keep its options open.", -5, .85,
                "The network repeats the relief number after the address. Elena’s crew passes it around at the plant. Maya asks for more staff to answer the calls."),
            new("Announce a timetable for talks", "A public commitment can give diplomats backing for a ceasefire conference.", -7, .55,
                "Delegations ask State to put the talks on the calendar. Maya keeps the relief figures in the daily brief while the invitations go out."),
            new("Promise there will be no concessions", "Your hardest-line allies will back the speech. A ceasefire conference will be harder to arrange.", 8, -.8,
                "The speech wins cheers from some of your supporters. Several foreign delegations ask State whether the promised statement leaves any room for a conference."));
    }

    private void CloseWartimeAddress()
    {
        if (_conversation is not { Id: ConversationId.WartimeAddress } address) return;
        _conversation = null;
        AddStory($"{address.Title} · Canceled", "Nora puts away the draft wartime address. State is preparing a briefing on the ceasefire the delegations accepted.");
    }

    private int TrialRecordEffect => _trialConversationNumber == Impeachments
        ? Choice(ConversationId.TrialRecord) switch { 0 => -3, 1 => -1, 2 => 3, _ => 0 }
        : 0;

    private double WartimeAddressEffect => Choice(ConversationId.WartimeAddress) switch { 1 => .12, 2 => -.12, _ => 0 };

    private void OpenLedger()
    {
        string opening = Choice(ConversationId.MissingName) switch
        {
            0 => "The caseworker has been checking which addresses received notices. She found a batch marked delivered on the day it was mailed.",
            1 => "Jules brings two stamped envelopes to the inspector general. Both appear on an agency list as resolved. No one at the plant has seen either payment.",
            2 => "Nora has an annotated copy of the company’s reopening announcement. Jules has circled the line about everyone receiving help.",
            _ => "An aide in Mercer’s district has matched the list of unresolved calls to a White House spreadsheet. Everyone on her list is recorded as served."
        };
        OpenConversation(ConversationId.Ledger, "The Duplicate Ledger", "Ruth Okafor, Inspector General",
            opening + "\n\nOkafor puts two ledgers on your desk. ‘I can establish how many notices went out. To establish who was paid, I need the underlying files. Those are in the hands of people who helped get your votes.’",
            new("Ask for an independent audit", "Okafor will preserve the records and report to Congress. Allies will have to account for their own figures.", -7, .9,
                "The inspector general seals copies of the paperwork. Treasury revises the public count. Nora’s phone rings as she hands you the new figures. It is the first supplier to see its name in the report."),
            new("Send the discrepancies back to Treasury", "Maya can reconcile the records quietly. The committee will have to ask for the results later.", -4, .4,
                "Maya’s staff identifies where notices were counted as payments. A revised ledger goes to the agencies. Okafor keeps the original and asks to see the revised figures before the next hearing."),
            new("Leave the official count in place", "Supporters can keep citing the higher figure. Okafor will retain the discrepancy for her own report.", 8, -1.05,
                "The White House repeats the original total. Okafor tells Treasury she has preserved both versions. Two pages of the disagreement make their way into Mercer’s briefing book."));
    }

    private void OpenDebate()
    {
        string issue = WorldWarActive ? "Outside the studio, reporters are shouting questions about freight costs and the war. "
            : Impeachments > 0 ? "The moderator has the impeachment record open on her desk. "
                : "Price has put a copy of the first relief announcement beside their water glass. ";
        bool audited = Choice(ConversationId.Ledger) == 0 || Choice(ConversationId.Hearing) == 0;
        string evidence = audited
            ? "Nora hands you the corrected figure and shows you the page of the public record it came from."
            : "Nora can give you the White House’s figure. Treasury’s case files use a different cutoff, and some requests still have no disposition.";
        OpenConversation(ConversationId.Debate, "The Debate Stage", CampaignStory.ChiefOfStaff,
            issue + evidence + "\n\nThe moderator’s first question is for you. ‘If someone listening tonight was told that their case had been closed, who can they call to find out what happened?’",
            new("Answer with Jules’s case", "Give a specific account of a worker’s experience. Price can ask what the government actually delivered.", -5, audited ? 1.15 : .35,
                audited ? "The campaign produces the hearing page before the broadcast ends. Jules calls Elena after the debate and asks to correct the date on the agency’s last letter."
                    : "Jules hears their name on television. By morning both campaigns have called the plant, and the district office still needs a case number to follow up."),
            new("Put Treasury’s figures on the screen", "Maya can explain the published ledger and its limits. Viewers waiting on a particular case will still want a name.", -4, .7,
                "The network publishes Treasury’s figures. Maya asks for a second graphic telling viewers which office handles disputes. It goes online after the studio has emptied."),
            new("Turn the question back on Price", "Party leaders will rally to the counterattack. The workers’ question will remain on the record.", 8, -.75,
                "The exchange leads the evening news. The next morning, the district office hears from several people whose questions went unanswered. One still has both employee numbers on the card they were holding at the watch party."));
    }

    private void OpenFirstTransition()
    {
        string room = ElectoralVotes switch
        {
            >= 270 => "Nora tells your incoming department heads to take the seats across the table. Several were in the same seats four years ago.",
            269 => "Career officials have prepared the briefing for both teams. The House still has to settle the succession.",
            _ => "Price sends an aide who used to handle casework. She asks for the date of the last worker’s reply before she asks for any of the totals."
        };
        OpenConversation(ConversationId.FirstTransition, "A Call from the Other Office", CampaignStory.ChiefOfStaff,
            room + "\n\nOne folder is still in front of you. Elena asks which name to put on her next letter. For people still waiting on the government, January is another deadline.",
            new("Publish the unresolved deadlines", "Current and incoming offices will both have dates the public can check.", -6, .85,
                "The program deadlines go on the public calendar. An aide calls Elena to check whether the listed date matches the one the crew was given. It does for some of the cases."),
            new("Name a career contact for each case", "The staff who will stay through January can keep the individual cases moving.", -4, .5,
                "Each file is assigned to a career official. Nora sends Elena the number for the person keeping the full list. She asks the official to keep answering after the photographs come down."),
            new("Leave future deadlines open", "Department heads will have room to reconsider the cases. Families may have to ask for dates again.", 5, -.6,
                "The handover folder has a page for each program and no dates beside the unsettled cases. Nora gives the district office a copy and tells them when the transition team will be in the building."));
    }

    private void OpenLastSignature()
    {
        string reason = Choice(ConversationId.FirstTransition) switch
        {
            0 => "Nora has the old public deadline list. Beside some of the entries are dates in a different hand.",
            1 => "The career contact from the first handover is still working. She has brought the remaining files and a list of the calls that never came back.",
            2 => "The first handover folder still has blank spaces for dates. Several departments have returned their own copies with different answers.",
            _ => "The last open files from the first term are back on the desk. Some have three different names for the person supposed to answer."
        };
        OpenConversation(ConversationId.LastSignature, "The Last Signature", "Jules Vega",
            reason + "\n\nJules sends a letter. ‘You asked me to put both numbers on the first form. I’ve attached the new ones for the people whose cases are still open. Who do I tell them is taking this over?’",
            new("Sign a public handover with the open deadlines", "The next administration will inherit a list it can be questioned about on its first day.", -6, .8,
                "The open deadlines are published with the names of the responsible offices. Jules starts writing next to the entries that will need a call after January."),
            new("Send the case files to the career leads", "The cases stay private. The next agency heads will have what they need to answer for each file.", -4, .5,
                "The transition staff acknowledge receipt. The career leads are listed on the first page, and Nora includes a blank sheet for the answers they still owe."),
            new("Use the last address to defend your record", "Your coalition gets the farewell it wants. Jules’s question goes through the ordinary transition queue.", 8, -1,
                "Your farewell runs in full. When the station cuts back to the studio, it reads the transition office’s public number. Jules writes it at the top of the new list."));
    }

    private double DebateReputation(State state) => state.Exposure >= 1
        ? Choice(ConversationId.Debate) switch { 0 => .55, 1 => .35, 2 => -.65, _ => 0 }
        : 0;

    private string LastHandoverCoda()
    {
        return (Term == 1 ? Choice(ConversationId.FirstTransition) : Choice(ConversationId.LastSignature)) switch
        {
            0 => "\n\nJules circles the first open date on the published calendar. They leave enough room below it for a second.",
            1 => "\n\nBefore Nora locks the file cabinet, she slides a fresh copy of the contact list into Elena’s envelope.",
            2 => "\n\nA note from Jules is clipped to the handover. It asks for the name of the person who will be answering calls on Monday.",
            _ => ""
        };
    }
}
