namespace GeopoliticsSim.Core;

public enum ConversationId { MissingName, Hearing, EmptyChair, Endorsement }

public record ConversationOption(string Label, string Stakes, int CapitalChange, double ApprovalChange, string FollowUp);

public record PresidentialConversation(ConversationId Id, string Title, string Speaker, string Scene, DateTime Deadline,
    IReadOnlyList<ConversationOption> Options);

public sealed partial class Campaign
{
    private static readonly DateTime HearingDate = new(2031, 3, 3);
    private static readonly DateTime EmptyChairDate = new(2034, 2, 12);
    private static readonly DateTime EndorsementDate = new(2036, 5, 19);
    private readonly Dictionary<ConversationId, int> _conversationChoices = [];
    private bool _openingWorkerReliefSigned;
    private bool _conversationSeen;
    private PresidentialConversation? _conversation;
    private ConversationOption? _conversationReply;
    private string _conversationReplyTitle = "";
    private int _conversationReplyDay;

    public bool ConversationsEnabled { get; private set; }
    public bool EnableConversations()
    {
        if (ConversationsEnabled) return false;
        ConversationsEnabled = true;
        return true;
    }

    public PresidentialConversation? PendingConversation => !Finished ? _conversation : null;

    public bool AnswerConversation(ConversationId id, int index)
    {
        if (PendingConversation is not { } conversation || conversation.Id != id
            || index < 0 || index >= conversation.Options.Count || Date >= conversation.Deadline) return false;
        var option = conversation.Options[index];
        if (option.CapitalChange < 0 && !Spend(-option.CapitalChange)) return false;
        if (option.CapitalChange > 0) Capital = Math.Min(100, Capital + option.CapitalChange);

        _conversationChoices.Add(id, index);
        _conversationReply = option;
        _conversationReplyTitle = conversation.Title;
        _conversationReplyDay = Day + 12;
        _conversation = null;
        AddStory($"{conversation.Title} · Your decision", $"You chose to {option.Label.ToLowerInvariant()}. The White House will follow up on {Inauguration.AddDays(_conversationReplyDay):MMMM d}.");
        return true;
    }

    private int Choice(ConversationId id) => _conversationChoices.GetValueOrDefault(id, -1);

    private void TickConversations()
    {
        if (!ConversationsEnabled) return;
        if (_conversationReply is { } reply && Day == _conversationReplyDay)
        {
            Approval = Math.Clamp(Approval + reply.ApprovalChange, 15, 85);
            AddStory($"{_conversationReplyTitle} · Follow-up", reply.FollowUp);
            _conversationReply = null;
        }
        if (_conversation is { } conversation && Date >= conversation.Deadline)
        {
            string silence = conversation.Id switch
            {
                ConversationId.MissingName => "Elena calls the district office instead. Jules takes time off to go in person and brings both employee numbers. Nora leaves the unopened list in the briefing folder.",
                ConversationId.Hearing => "Mercer releases his next committee calendar. The workers’ case has no hearing date. Elena asks the district aide to keep calling about the appeals.",
                ConversationId.EmptyChair => "Reed takes a copy of the unresolved cases. When new calls come in, the offices continue deciding one at a time who will answer them.",
                _ => "Reed makes the announcement alone. The campaign prints its own summary of the record. Nora leaves a copy on your desk with the disputed cases clipped inside."
            };
            AddStory($"{conversation.Title} · No reply", silence);
            _conversation = null;
        }
        if (Day == 28) OpenMissingName();
        else if (Date == HearingDate) OpenHearing();
        else if (Term == 2 && Date == EmptyChairDate) OpenEmptyChair();
        else if (Term == 2 && Date == EndorsementDate) OpenEndorsement();
    }

    private void OpenConversation(ConversationId id, string title, string speaker, string scene,
        params ConversationOption[] options)
    {
        _conversationSeen = true;
        _conversation = new PresidentialConversation(id, title, speaker, scene, Date.AddDays(21), Array.AsReadOnly(options));
        AddStory(title, scene);
    }

    private void OpenMissingName()
    {
        string opening = _openingWorkerReliefSigned
            ? "Thirty-seven workers were on the relief list. The thirty-eighth, Jules Vega, was on Elena’s shift schedule the whole time."
            : "The plant has started drawing up a list for when orders return. Jules Vega worked that shift until the shortage, and their name is missing.";
        OpenConversation(ConversationId.MissingName, "The Missing Name", "Elena Ruiz",
            opening + " Elena puts Jules on the phone. ‘The agency says to ask the plant. The plant says to ask the agency. Could somebody in your building tell me whose employee I was?’",
            new("Give Jules a caseworker", "An agency can untangle the employment record. The workers’ names stay private.", -4, .6,
                "Jules gets a name and a number that answers. The caseworker confirms which agency is responsible and starts reviewing the claim. Elena asks for a second appointment for the next worker in the same position."),
            new("Invite workers to give public testimony", "The crew can put the missing names on record. Lawmakers who helped the company expect questions.", -7, 1,
                "Jules reads off both employee numbers in a public hearing. Mercer asks staff to record the gap in coverage. The claim is still pending, and the next crew now has a hearing record to cite."),
            new("Give the company the podium", "Industry allies will reward a clean announcement. Elena and Jules will have to raise their cases on their own.", 8, -.9,
                "The company announces the shift’s return with Banks’s name on the release. Elena finds the photograph in the paper. Jules is standing just outside the frame, holding the letter that sent them back to the staffing agency."));
    }

    private void OpenHearing()
    {
        string evidence = Choice(ConversationId.MissingName) switch
        {
            0 => "The caseworker has brought records from several plants. Jules’s case was the first on her list.",
            1 => "Mercer has the transcript of Jules’s testimony. Staff have flagged the questions two agencies answered differently.",
            2 => "Elena comes in with photocopies. She kept the announcement that put the company at the microphone and the letters it left out.",
            _ => "A district aide brings Elena in after the public session. She has the schedule and a stack of requests her crew sent to different offices."
        };
        OpenConversation(ConversationId.Hearing, "Mercer’s Condition", "Senator Ellis Mercer",
            evidence + " Mercer can hold a hearing if you are willing to bring the agencies in. A narrower deal can get help to the plant sooner. He asks whether you want a public commitment the next Congress can hold you to.",
            new("Put the agency officials under public questioning", "The committee can assign responsibility in public. Some allies will have to explain their own decisions.", -7, 1.2,
                "The hearing ends with two agencies agreeing on which office will answer appeals. Mercer asks his staff to put the dates in the record. Jules leaves with a copy and the name of the person expected back in thirty days."),
            new("Take the one-district agreement", "Mercer can get a liaison for Elena’s district. Workers in similar cases elsewhere will still need their own sponsors.", -2, .7,
                "The district receives a liaison and the calls start moving. At the next markup, an aide from another state asks for the same arrangement. Mercer can only show her the local letter."),
            new("Ask Mercer to set the hearing aside", "Committee allies will free up time for your other bills. The agencies can keep answering the claims separately.", 7, -1.1,
                "Mercer moves the hearing off the calendar. When Elena calls, Nora offers to pass on a message. Elena asks for the public committee number instead."));
    }

    private void OpenEmptyChair()
    {
        string precedent = Choice(ConversationId.Hearing) switch
        {
            0 => "Reed brings the committee transcript. The agency witnesses agreed to return, and someone has to hold them to the date.",
            1 => "Reed puts the Michigan liaison agreement on the table. Other states are calling about the same gap in coverage.",
            2 => "Reed brings the hearing notice with the date crossed out. People are still sending requests to the offices named on it.",
            _ => "Reed asks to see the unresolved cases from the first term. Nora has collected them in a binder with no office listed on its spine."
        };
        OpenConversation(ConversationId.EmptyChair, "The Empty Chair", "Vice President Jordan Reed",
            precedent + " Your staff is about to leave for another meeting. Reed stays seated. ‘Give me a person who has to return these calls, or give me the desk.’",
            new("Put Reed in charge of the follow-up", "Reed gets a public assignment and room to challenge your departments.", -7, 1,
                "Reed’s office starts publishing the dates it gets answers. One of the first reports names a department led by a Banks ally. Nora brings you the draft before the ally’s call comes through."),
            new("Put Maya in charge of the follow-up", "Treasury will track delivery with the agencies. Reed keeps room to work with Congress.", -5, .55,
                "Maya assigns each unresolved case to a department and gives Reed a copy. When several dates slip, Reed asks to be in the room for the next review."),
            new("Keep the work inside the West Wing", "Nora can protect your control of the agenda. Families will still have to ask your staff for status.", 6, -.9,
                "The West Wing keeps the binder. Reed tells Nora to forward any calls she cannot return. Soon the vice president is hearing from several of the same people as the White House."));
    }

    private void OpenEndorsement()
    {
        string record = Choice(ConversationId.EmptyChair) switch
        {
            0 => "Reed brings the status reports from their own office, including several that embarrassed your departments.",
            1 => "Reed brings Maya’s tables and a second list of deadlines that are still open.",
            2 => "Reed brings copies of the calls Nora forwarded. The campaign staff has lined them up against your public progress reports.",
            _ => "Reed brings four years of replies from different departments. A staffer has marked the ones that disagree."
        };
        if (WorldWarActive) record += " The wartime relief cases are in a separate stack.";
        else if (Impeachments > 0) record += " The first question in the town hall binder is about the impeachment.";
        OpenConversation(ConversationId.Endorsement, "The Terms of the Endorsement", "Vice President Jordan Reed",
            record + " ‘I’d like you there when I announce. I need to be able to say what I’ll keep and what I’d do differently.’",
            new("Give Reed room to publish an independent account", "Reed can use redacted records and discuss the disputed cases in public.", -8, 1.2,
                "Reed reads out one deadline the administration met and one it missed. Jules asks the first follow-up from the audience. Afterward, Reed’s staff asks whether you want to hear their answer before the next event."),
            new("Sign a joint pledge with deadlines", "You keep one public message. Maya will issue a calendar both offices have to answer for.", -5, .65,
                "The cameras leave. Maya asks both offices to initial the dates they agreed to. The first review is thirty days after the next inauguration, whoever wins."),
            new("Insist on defending the record together", "Party leaders will back the joint line. Reed will have to answer for decisions they had no part in.", 8, -1.3,
                "Reed delivers the joint statement. In the hallway Jules asks which office should take questions about the cases the statement left out. The aide gives them two separate numbers."));
    }

    private double ConversationReputation(State state)
    {
        double effect = 0;
        if (state.Code == "MI")
        {
            effect += Choice(ConversationId.MissingName) switch { 0 => .45, 1 => .9, 2 => -1.5, _ => 0 };
            effect += Choice(ConversationId.Hearing) switch { 0 => .7, 1 => .4, 2 => -.85, _ => 0 };
        }
        if (Term == 2 && state.Exposure >= 1)
        {
            effect += Choice(ConversationId.EmptyChair) switch { 0 => .35, 1 => .2, 2 => -.45, _ => 0 };
            effect += Choice(ConversationId.Endorsement) switch { 0 => .85, 1 => .35, 2 => -.95, _ => 0 };
        }
        return effect;
    }

    public string? ClosingLetter
    {
        get
        {
            if (!Finished || CanBeginSecondTerm || !_conversationSeen) return null;
            string letter = Choice(ConversationId.Hearing) switch
            {
                0 => "Jules’s copy of the hearing record has a new entry beside each promised reply. Beside the unanswered ones, Elena has left room for another date. Below that she’s written the committee’s number so the next crew knows where to start.",
                1 => "Elena has written the Michigan liaison’s number on Jules’s schedule. On the back there’s a question from a crew out of state. She tucks it into the copy going to the incoming team.",
                2 => "Elena sends Jules a copy of the hearing request with its canceled date visible. ‘Take this with you if they give you a new appointment. It might save you having to tell the whole story again.’",
                _ when Choice(ConversationId.MissingName) == 2 =>
                    "Jules sends Nora a copy of the photograph from the reopening. On the back they have written the missing name and both employee numbers. Nora puts it with the original schedule.",
                _ => "The last note in the folder is from Elena. It has Jules’s name, the name of the next person in line, and a number the incoming office can call before it makes any new promises."
            };
            return letter + (Choice(ConversationId.Endorsement) switch
            {
                0 => "\n\nJules attaches a page from Reed’s public account and marks the reply they still want the next administration to give.",
                1 => "\n\nA copy of the joint pledge is folded inside. Beside the first review date, Elena has written who must be in the room.",
                2 => "\n\nThe joint statement is in the folder, too. Elena underlines a sentence about accountability and writes a phone number in the margin.",
                _ => ""
            });
        }
    }
}
