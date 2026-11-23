namespace GeopoliticsSim.Core;

public sealed partial class Campaign
{
    private static readonly IReadOnlyDictionary<string, AgendaOption> HardChoices = new Dictionary<string, AgendaOption>
    {
        ["The Inventory Pact"] = new("Give the largest buyers first claim", 0, 17, -1.6, .39, .03,
            "Large auto firms secured their deliveries. Independent suppliers reported canceling shifts while waiting for what was left.", 8),
        ["A Port Runs Late"] = new("Walk away from the labor talks", 0, 12, -2, -.24, 0,
            "Shippers’ allies backed the White House. With mediation called off, the dispute dragged on and more cargo missed its sailing.", 7),
        ["The Eastport Review"] = new("Withhold the White House findings", 0, 15, -2, 0, 0,
            "Major suppliers welcomed the decision. A House committee held a hearing on the missing records, and the unanswered questions became part of the public record.", 6),
        ["The River Runs Low"] = new("Give the biggest shipping firms priority", 0, 12, -1.8, .29, .08,
            "The largest firms moved more cargo. Small operators lost their slots and asked governors to reopen the allocation deal.", 8),
        ["The War at the Aster Strait"] = new("Give commercial cargo priority", 0, 13, -2.1, .53, .03,
            "Domestic manufacturers received parts ahead of schedule. Relief groups reported longer waits, and senators requested the revised queue at a public hearing.", 8),
        ["The Plant Reopening"] = new("Keep unpaid workers off the program", 0, 10, -1.8, 0, 0,
            "Company executives praised the event. Elena read the list of unpaid workers outside the gate, where reporters had stopped on their way in.", 7),
        ["Empty Shelves"] = new("Give large hospital systems first claim", 0, 14, -2, .5, .04,
            "Large networks restocked quickly. Smaller hospitals lost access to the first deliveries and deferred routine care.", 8),
        ["The Reserve Stress Test"] = new("Leave small hospitals to buy on their own", 0, 11, -2.2, -.16, 0,
            "Treasury avoided a new allocation commitment. Rural administrators reported higher emergency prices and another round of postponed appointments.", 6),
        ["The Disclosure Committee"] = new("Decline to release the supplier record", 0, 14, -2.1, 0, 0,
            "Several suppliers supported the refusal. The committee’s public report listed unanswered questions and urged the next administration to reopen the record.", 7),
        ["Manufacturers Ask for Certainty"] = new("Offer guarantees to the largest firms only", 0, 17, -1.5, .38, .22,
            "Large projects stayed on schedule. Smaller competitors withdrew plans, while Treasury recorded additional costs under the guarantees.", 9),
        ["The Last Cabinet Meeting"] = new("Leave the open-program files unpublished", 0, 9, -1.2, -.08, 0,
            "Department officials kept their own records. The incoming team had to reconstruct the list of programs with pending decisions.", 5)
    };

    public AgendaOption? HardAgendaOption => Episode?.First == null ? null : HardChoices.GetValueOrDefault(Current.Title);

    private bool TookHardChoice(string title) => _agendaChoices.GetValueOrDefault(title) == 3;

    private double AgendaReputation(State state)
    {
        double effect = 0;
        if (state.Code == "MI" && TookHardChoice("The Plant Reopening")) effect -= 1.35;
        if (Term == 2 && state.HouseSeats <= 5 && TookHardChoice("Empty Shelves")) effect -= .6;
        return effect;
    }
}
