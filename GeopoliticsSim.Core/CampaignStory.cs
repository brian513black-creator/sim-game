namespace GeopoliticsSim.Core;

public record StoryChapter(DateTime Start, string Title, string Speaker, string Scene, string Objective);

public static class CampaignStory
{
    public const string ChiefOfStaff = "Nora Vale, Chief of Staff";
    public const string VicePresident = "Jordan Reed";
    public const string Challenger = "Morgan Price";

    public static IReadOnlyList<StoryChapter> Chapters { get; } = Array.AsReadOnly<StoryChapter>(
    [
        new(Campaign.Inauguration, "The First Promise", ChiefOfStaff,
            "Before you take the oath, Nora slips a photocopied shift schedule behind your speech. Elena Ruiz runs second shift at an auto-parts plant in Michigan. She’s penciled in 38 names. In the margin she’s written the date the plant runs out of chips.",
            "Decide what you can promise the crew. Senate leadership has given you 21 days to get a bill to the floor."),
        new(new DateTime(2030, 1, 1), "The Cost of Staying Open", "Maya Shah, Treasury Secretary",
            "Maya brings two envelopes from the same town. One has a notice that a plant is hiring again. The other has a household’s winter bills. ‘We can have a recovery on paper while the people opening those envelopes are still waiting.’",
            "Watch what happens to prices and borrowing costs as the White House pushes for more relief."),
        new(new DateTime(2031, 1, 1), "The Votes You Inherit", "Senator Ellis Mercer",
            "Mercer comes in through the staff entrance and hands Nora a list of names. Some of them voted for you and lost. ‘Every new member on that list has a promise to keep. Find out what it is before you ask for a vote.’",
            "Read the midterm result and rebuild support for your next bill."),
        new(new DateTime(2032, 1, 1), "The Other Podium", ChiefOfStaff,
            "Morgan Price opens their campaign at a Michigan community college. Price brings the Treasury tables and asks who got paid while workers waited. Nora leaves the television on through the questions. ‘That’s what people are going to ask us, too.’",
            "Decide where to defend your record. An Electoral College majority earns a second term."),
        new(Campaign.TermEnd, "The Second Oath", "Vice President Jordan Reed",
            "While staff set out the second inaugural speech, Reed lays a copy of Elena’s schedule beside it. There’s an apprentice penciled in under the original names. ‘She asked who answers the phone after you leave. I said I’d find out.’",
            "Decide which parts of your record the next administration should be able to carry on."),
        new(new DateTime(2034, 1, 1), "The Last Delivery", "Maya Shah, Treasury Secretary",
            "At a medicine hearing, a rural pharmacist reads the date of the last delivery into the microphone. The distributor’s dashboard still lists the pharmacy as served. Maya circles both dates and asks staff to put them on the same page.",
            "Decide who gets priority when institutions report success and the people relying on them are still waiting."),
        new(new DateTime(2035, 1, 1), "The Names on the Letter", "Senator Ellis Mercer",
            "Mercer says he plans to retire. He gives Nora a draft letter to his successor and asks her to check the names of the towns. There are spaces beside each one for a deadline and the person who agreed to meet it.",
            "Use the last full year before the election to put deadlines, funding, and oversight behind your commitments."),
        new(new DateTime(2036, 1, 1), "The Handoff", "Vice President Jordan Reed",
            "Reed asks for the campaign’s first town hall without you. Elena has agreed to moderate. ‘She’s going to ask what I’d keep, what I’d change, and what I’d have to come back here and explain. I owe her answers of my own.’",
            "Help Reed make a case for the next presidency, then finish yours on Inauguration Day.")
    ]);
}

public enum OpeningCommitment { None, ProtectJobs, ControlCosts }
public enum OpeningOutcome { Pending, Kept, Broken, NoCommitment }

public sealed partial class Campaign
{
    private enum OpeningBillOutcome { Signed, Vetoed, Stalled }

    private readonly Dictionary<DateTime, string> _chapterScenes = [];
    private bool _trainingFunded;


    public string ChapterScene => _chapterScenes.GetValueOrDefault(Chapter.Start) ?? Chapter.Scene;

    private void RecordChapterOpening()
    {
        var chapter = Chapter;
        string scene = OpeningScene(chapter);
        _chapterScenes[chapter.Start] = scene;
        AddStory(chapter.Title, scene);
    }

    private void AddStory(string title, string scene, bool requiresElectionCall = false, bool decision = false) =>
        _briefings.Add(new Briefing(Date, scene, decision, title, requiresElectionCall));

    private void RecordOpeningBillOutcome(OpeningBillOutcome outcome)
    {
        if (_crisisIndex != 0) return;
        _openingWorkerReliefSigned = outcome == OpeningBillOutcome.Signed && HasMeasure(Measure.WorkerRelief);
        string response = outcome switch
        {
            OpeningBillOutcome.Signed when HasMeasure(Measure.WorkerRelief) =>
                "Elena calls from the plant office. She asks whether people already sent home can apply for relief. Nora gets an answer from the agency and writes the number of the caseworker beside the first name on Elena’s schedule.",
            OpeningBillOutcome.Signed =>
                "The plant manager forwards the signing announcement. Elena prints it and circles the provisions the company can use. She asks Nora what number to give the crew when they ask about their missing pay.",
            OpeningBillOutcome.Vetoed =>
                "Elena is waiting for Nora to return a call when the veto comes over the radio. She leaves her number again. In the morning she’ll need to tell the crew what the company can still promise.",
            _ =>
                "After the vote, a member’s office asks whether it can send Elena the roll call. She asks for the date of the next vote. Nora leaves a note to call back when someone on the Hill has given her one."
        };
        AddStory("The call back to Michigan", response);
    }

    private void RecordEpisodeScene()
    {
        if (Current.Title == "The War at the Aster Strait")
            AddStory("The wartime hearing", "At a briefing on the Aster Strait war, Mercer asks State how many evacuees have reached reception centers. Maya has a separate report on how many cargo delays the country can absorb. Nora asks that both figures go to the committee.");
        else if (Current.Title == "The Aster Strait Ceasefire")
            AddStory("The ceasefire briefing", "Reed brings the old wartime folders into the Situation Room. The same port authorities are on the call. This time their first question is who will answer for the promises once the emergency ends.");
    }

    private void RecordMidtermScene()
    {
        string scene = (Term, MidtermShift) switch
        {
            (1, > 0) => "Mercer calls when the last tight race is settled. A member who ran with your support wants to talk tomorrow about next year’s credit programs. Mercer asks Nora to take the call before the victory interviews.",
            (1, _) => "Nora works through the list of members who lost. One district office has three pending calls from Elena’s crew. She asks Mercer for a name in the incoming delegation before anyone changes the office numbers.",
            (2, > 0) => "Reed puts their remarks aside and asks Maya to get the incoming members a calendar of drug shipments. ‘They’re going to have to explain the next shipment. Let’s make sure they can tell people when it’s arriving.’",
            _ => "Reed calls from a county where the winners spent the campaign talking about drug prices. Mercer looks at the new committee list and starts marking the names they’ll have to call about the supply bill."
        };
        AddStory("The day’s last call", scene);
    }

    private string TransitionScene =>
        ElectoralVotes == 269
            ? "The transition office is keeping two sets of briefing books while the incoming House prepares for the contingent election. Maya sends both teams the same payment calendar. Checks come due in January whoever takes the oath."
            : Term == 1 && ElectoralVotes >= 270
                ? "Everyone around the table has worked in the building for four years. Nora still asks each department for the first signature it will need in January. Two offices give her different answers for the same program."
                : Term == 1
                    ? "Price brings a policy aide who used to work in a Michigan district office. She asks which factory records Elena can see without calling Washington. Nora writes the question down for both teams."
                    : ElectoralVotes >= 270
                        ? "Reed brings people who can sign for their departments after the oath. Maya asks whether the supply program should change on day one. Reed asks her to start with the hospitals that are still waiting."
                        : "The president-elect’s team brings a list of programs they intend to review. Two rural hospitals are waiting on guarantees due before then. The teams agree on who will call the hospitals if the dates change.";

    private void RecordEpilogue()
    {
        var unfinished = new List<string>();
        if (OnDesk) unfinished.Add($"the unsigned {PackageName}");
        if (_ruleDate > Day) unfinished.Add("the agency review still underway");
        if (_conversation != null || _conversationReply != null) unfinished.Add("the workers’ correspondence awaiting a reply");
        if (PendingAgendaFollowUp != null)
            unfinished.Add($"the follow-up to {_responseEpisode}");
        if (Mission != DefenseMission.None || Authorization == DefenseAuthorization.Pending)
            unfinished.Add("the Pentagon’s open commitment or authorization request");
        if (PendingForeignReply != null)
            unfinished.Add("the diplomatic reply State is still waiting for");
        if (unfinished.Count > 0)
        {
            string owner = CanBeginSecondTerm ? "your second-term staff" : "the transition team";
            AddStory("Open work on the desk", $"Nora hands {owner} a list before anyone clears the desk. It includes {string.Join(", ", unfinished)}.");
        }
        AddStory(Removed ? "The unexpected handover" : CanBeginSecondTerm ? "Inauguration morning" : "The handover", Epilogue);
        if (ClosingLetter is { } letter)
            AddStory("Elena’s last letter", letter);
    }

    private string OpeningScene(StoryChapter chapter) => chapter.Start.Year switch
    {
        2030 => CommitmentOutcome switch
        {
            OpeningOutcome.Kept when Commitment == OpeningCommitment.ProtectJobs =>
                "Elena’s envelope holds copies of the relief notices that reached her crew. One worker has written a grocery total on the back. Maya sets that copy beside the inflation report and asks for both to go in your briefing.",
            OpeningOutcome.Kept =>
                "Maya sets the signed budget score beside a letter from Elena. The last line thanks you for publishing the cost and asks how long a household should wait before assuming the help has passed them by.",
            OpeningOutcome.Broken =>
                "Elena sends back the original shift schedule. Several names have forwarding addresses written beside them. Maya asks permission to bring the letter into the meeting about how the administration will measure a recovery.",
            _ => chapter.Scene
        },
        2031 when MidtermShift < 0 =>
            "Mercer waits until the room is empty. Two members who helped him count the last vote are out. He opens a list of their replacements. ‘These people won by promising to stop us. If you need their votes, give them something worth explaining back home.’",
        2031 when MidtermShift > 0 =>
            "Mercer brings the names of members elected on promises to protect local employers. ‘They campaigned with you and came back with their seats. When they get home, they’ll be asked what their district got out of it. Give them an answer they can put in writing.’",
        2032 when Impeachments > 0 =>
            "Morgan Price starts the Michigan speech by reading a passage from the House proceedings. Price spends the rest of the hour on wages and prices. Nora marks the moment the room starts asking questions. ‘They came to talk about both.’",
        2032 when TookHardChoice("The Eastport Review") =>
            "Price brings an empty binder to a Michigan press conference and calls it the White House findings. Maya switches off the television after the first question. The committee is asking for those same papers again.",
        2034 when TookHardChoice("Empty Shelves") =>
            "Maya brings two delivery logs to the hearing. A major health system filled its orders in two days. A small hospital on the same road canceled appointments for a week. ‘Both of these are in the same regional average.’",
        2034 when _agendaChoices.TryGetValue("Empty Shelves", out int choice) => choice == 1
            ? "The reserve has medicine in stock. A rural pharmacist tells Maya that the nearest wholesaler with access is still a day’s drive away. Maya asks the committee to compare inventory on the ledger with inventory a patient can actually reach."
            : "At the hearing, a small hospital director brings invoices from the regional contract. A neighboring county never qualified. Maya lays their maps side by side and asks the committee where it would draw the boundary next time.",
        2035 when MidtermShift < 0 =>
            "Mercer hands over the committee calendar. Several chairs have changed hands. ‘Some of the people listed here made a career of opposing your bills. Ask which of your provisions their counties still use. Start there.’",
        2036 when Impeachments > 0 =>
            "Reed brings a marked-up draft for the Michigan town hall. There’s a direct question about the impeachment on page one. ‘Someone in that room will ask it. I want them to hear me answer in my own words.’",
        2036 when _trainingFunded =>
            "Reed asks Elena to moderate the first town hall. She agrees if a new apprentice gets the first question. They want to know how long the training money lasts and who at Washington can tell their class whether another year is funded.",
        _ => chapter.Scene
    };

    public string ElectionNightScene
    {
        get
        {
            var michigan = Results.FirstOrDefault(result => result.State.Code == "MI");
            if (michigan == null) return "";
            string local = michigan.Margin >= 0
                ? "The network calls Michigan for your ticket. Elena turns up the sound in the plant break room."
                : $"Michigan goes to {OppositionName}. Elena asks everyone in the break room to let the county clerk finish talking.";
            string closing = ElectoralVotes switch
            {
                269 => "Nora calls House leadership and asks when the delegation count will be ready.",
                >= 270 when Term == 1 => "Elena texts Nora before the victory speech. ‘Can we keep the number we’ve been calling?’",
                >= 270 => "Reed asks a staffer to save Elena’s number. There’s an open question from the town hall to return to in the morning.",
                _ when Term == 1 => "Price takes your concession call. Nora stays on the line long enough to exchange the numbers for the two transition teams.",
                _ when _trainingFunded => "Reed calls the winning ticket before dawn. Elena leaves a message asking whether the funded training places will keep their next start date.",
                _ => "Reed calls the winning ticket before dawn. Elena leaves a message asking whom the college should call about the next apprentice class."
            };
            string unresolved = TookHardChoice("The Plant Reopening")
                ? " The workers Elena read out at the gate still have their names on her list." : "";
            string hearing = Term == 1 ? Choice(ConversationId.Hearing) switch
            {
                0 => " The committee transcript is on the break-room table. Jules has marked the agency’s latest reply.",
                1 => " Elena lays out the liaison’s latest update. A worker asks whether the office takes calls from outside the district.",
                2 => " Elena has left Mercer’s notice beside the television. The place for the hearing date is still blank.",
                _ => ""
            } : "";
            return local + hearing + " " + closing + unresolved;
        }
    }

    private string OpeningKeepsake => CommitmentOutcome switch
    {
        OpeningOutcome.Kept when Commitment == OpeningCommitment.ProtectJobs =>
            "Elena’s original schedule goes into the archive with a copy of the worker-relief law and the dates the first payments cleared.",
        OpeningOutcome.Kept =>
            "Elena’s schedule goes into the archive beside the budget score you signed under. Her letter asking who was still waiting goes in the same folder.",
        OpeningOutcome.Broken =>
            "Nora files Elena’s annotated schedule with the vote count. She leaves the forwarding addresses visible.",
        _ => "Nora files the 38 names Elena sent on Inauguration Day. The space beside the original deadline still holds Elena’s penciled date."
    };

    public string Epilogue
    {
        get
        {
            if (!Finished) return "";
            if (Removed)
                return "While Jordan Reed takes the oath, Nora inventories the papers left on the Resolute Desk. Elena’s schedule is among them. Nora clips it to the transition briefing and sends the folder upstairs. The crew will have to call a different president.";
            if (WorldWarActive)
                return (CanBeginSecondTerm
                    ? "Nora leaves the civilian relief ledger beside your second inaugural speech. One page holds the promises already made. The next holds the calls you will have to return when you get back from the oath."
                    : "As your term ends, career officials carry the wartime relief ledger upstairs for the people who will assume responsibility for it. Nora has put Elena’s schedule on top so the next team sees who is waiting.")
                    + " " + OpeningKeepsake;
            if (CanBeginSecondTerm)
                return "On the morning of the second inauguration, Elena sends back the schedule with a note from her apprentice tucked inside. They ask how their plant is supposed to get through the next shortage. Nora lays the note beside the speech you are about to give.";
            if (ElectoralVotes == 269)
                return "At noon, the staff take down your photographs. The succession remains unsettled, and the departmental transition leads carry the open work forward. " + OpeningKeepsake;
            if (Term == 1)
                return "Morgan Price’s staff ask for the factory-level records they spent the campaign criticizing. Nora puts the available records on top of the handover box. At the bottom is the speech you carried to your first inauguration. " + OpeningKeepsake;
            if (ElectoralVotes >= 270)
                return "Reed reads from a folder with their own name on it at the Capitol. Back in Michigan, Elena’s apprentice is now running a shift. She has started sending her own reports to Washington. " + OpeningKeepsake;
            return "During the transition, the winning ticket’s staff ask for the list of federal phone numbers governors actually use. Several have corrections in Elena’s handwriting. " + OpeningKeepsake;
        }
    }
}
