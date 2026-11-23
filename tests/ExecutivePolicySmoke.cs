using GeopoliticsSim.Core;

internal static class ExecutivePolicySmoke
{
    public static void Run()
    {
        CheckCrisisHandoff(2045, stayed: true, expert: false);
        CheckCrisisHandoff(2029, stayed: false, expert: true);

        var handover = new Campaign(2029, oversightSeed: 2029, agendaEnabled: false, variableWorld: true);
        int inaugurationDay = (Campaign.TermEnd - Campaign.Inauguration).Days;
        AdvanceTo(handover, inaugurationDay - 1);
        Require(handover.Order() && handover.Rule(), "Start a court and agency review shortly before the end of the term.");
        AdvanceTo(handover, inaugurationDay);
        Require(handover.Finished && handover.Briefings.Any(entry => entry.SceneTitle == "Open work on the desk"
            && entry.Text.Contains("the federal court’s review of the emergency order")
            && entry.Text.Contains("the agency review scheduled to conclude")),
            "The handover names both pending reviews.");

        Console.WriteLine("Court reviews and agency rules remain tracked across crises, stays, expiration, and the handover.");
    }

    private static void CheckCrisisHandoff(int seed, bool stayed, bool expert)
    {
        var campaign = new Campaign(seed, oversightSeed: 2029, variableWorld: true);
        AdvanceTo(campaign, 62);
        if (expert) Require(campaign.Appoint(true), "Confirm a policy expert before opening the agency review.");
        Require(campaign.Order() && campaign.Rule(), "Open both processes during the chip shortage.");
        DateTime issued = campaign.Date;
        DateTime courtReview = campaign.PendingCourtReview ?? throw new InvalidOperationException("Court review date is missing.");
        DateTime expiry = issued.AddDays(38);
        DateTime ruleEffective = issued.AddDays(expert ? 20 : 42);

        AdvanceTo(campaign, 64);
        Require(campaign.Current.Title == "The Inventory Pact" && !campaign.CanOrder && !campaign.CanRule,
            "The new crisis does not offer either action.");
        Require(campaign.PendingCourtReview == courtReview && campaign.EmergencyOrderExpiry == expiry
            && campaign.PendingAgencyReview == ruleEffective, "The scheduled dates remain available after the crisis changes.");

        AdvanceTo(campaign, (courtReview - Campaign.Inauguration).Days);
        Require(campaign.PendingCourtReview == null && campaign.EmergencyOrderExpiry == (stayed ? null : expiry),
            "A stay ends the order immediately, while an upheld order retains its scheduled expiry.");
        Require(campaign.PendingAgencyReview == ruleEffective, "The court decision leaves the agency review on its own schedule.");

        AdvanceTo(campaign, (ruleEffective - Campaign.Inauguration).Days - 1);
        Require(campaign.PendingAgencyReview == ruleEffective, "The scheduled date remains visible until the rule takes effect.");
        AdvanceTo(campaign, (ruleEffective - Campaign.Inauguration).Days);
        Require(campaign.PendingAgencyReview == null && campaign.Briefings.Any(entry => entry.Date == ruleEffective
            && entry.Text.StartsWith("Agency review concluded")), "The agency rule takes effect on the published date.");

        AdvanceTo(campaign, Math.Max(campaign.Day, (expiry - Campaign.Inauguration).Days));
        Require(campaign.EmergencyOrderExpiry == null && campaign.Briefings.Count(entry => entry.Date == expiry
            && entry.Text.StartsWith("The emergency relief order expired")) == (stayed ? 0 : 1),
            "Only an order that survives review generates an expiry briefing.");
    }

    private static void AdvanceTo(Campaign campaign, int day)
    {
        while (campaign.Day < day)
        {
            if (campaign.OnDesk) Require(campaign.ResolveBill(true), "Sign pending bills while advancing the calendar.");
            Require(campaign.Advance(Math.Min(120, day - campaign.Day)) > 0, "The campaign calendar must advance.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
