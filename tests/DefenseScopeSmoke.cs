using GeopoliticsSim.Core;

internal static class DefenseScopeSmoke
{
    public static void Run()
    {
        var baseline = MakeCampaign();
        while (!baseline.DefenseWindowOpen) Advance(baseline, 120);
        int regionalStart = baseline.Day;

        CheckOverlap(regionalStart, legacy: false);
        CheckOverlap(regionalStart, legacy: true);
        Console.WriteLine("Defense authorization is scoped across overlapping missions, including after a legacy save upgrade.");
    }

    private static Campaign MakeCampaign(bool legacy = false) =>
        new(2029, oversightSeed: 2029, variableWorld: true, scopedDefenseAuthorization: !legacy);

    private static void CheckOverlap(int regionalStart, bool legacy)
    {
        var campaign = MakeCampaign(legacy);
        AdvanceTo(campaign, regionalStart - 65);
        while (campaign.PowerBrief("Russia").Rung < 10) ResolvePressure(campaign, "Russia");
        AdvanceTo(campaign, regionalStart);
        Check(campaign.DefenseContext == "The War at the Aster Strait", "Regional request opened.");
        Check(campaign.OrderProtectivePresence() && campaign.RequestDefenseAuthorization(), "Request authority for the regional presence.");
        while (!campaign.WorldWarActive) ResolvePressure(campaign, "China");

        Check(campaign.DefenseContext == "The War at the Aster Strait" && campaign.DefenseWindowTitle == "Global wartime response",
            "Existing mission and new request keep separate contexts.");
        Check(campaign.Authorization == DefenseAuthorization.Denied, "Regional denial still applies to its active mission.");
        if (legacy)
        {
            Check(campaign.ScheduleDefenseScopeUpgrade() && !campaign.ScopedDefenseAuthorization,
                "A legacy campaign keeps its active mission until withdrawal.");
        }

        Check(campaign.WithdrawDefenseMission(), "Withdraw the regional commitment.");
        AdvanceTo(campaign, campaign.Day + 14);
        Check(campaign.ScopedDefenseAuthorization && campaign.Authorization == DefenseAuthorization.None && campaign.CanRequestAuthorization,
            "Regional authorization must not affect the separate global commitment.");
        Check(campaign.OrderProtectivePresence() && campaign.WarPowersDaysRemaining == 60,
            "A separate mission starts its own War Powers clock.");
    }

    private static void ResolvePressure(Campaign campaign, string power)
    {
        Check(campaign.PressGreatPower(power), $"Issue an ultimatum to {power}.");
        while (campaign.PendingForeignReply != null) Advance(campaign, 1);
    }

    private static void AdvanceTo(Campaign campaign, int day)
    {
        while (campaign.Day < day) Advance(campaign, day - campaign.Day);
    }

    private static void Advance(Campaign campaign, int days)
    {
        if (campaign.OnDesk) Check(campaign.ResolveBill(true), "Sign pending bill.");
        if (campaign.PendingOffer != null) Check(campaign.RespondToOffer(OfferResponse.Decline), "Decline improper offer.");
        Check(campaign.Advance(days) > 0, "Calendar advances.");
    }

    private static void Check(bool condition, string context)
    {
        if (!condition) throw new InvalidOperationException(context);
    }
}
