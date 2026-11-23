using GeopoliticsSim.Core;

internal static class TradeHandoffSmoke
{
    public static void Run()
    {
        string submittedWithMandate = Reply(sourcingInOffer: true, sourcingInNextBill: false);
        Require(submittedWithMandate.StartsWith("Canada declined"), "Canada evaluates the sourcing mandate in the submitted offer.");
        Require(Reply(sourcingInOffer: true, sourcingInNextBill: true) == submittedWithMandate,
            "Amending Brazil’s bill leaves Canada’s reply unchanged.");

        string submittedWithoutMandate = Reply(sourcingInOffer: false, sourcingInNextBill: true);
        Require(submittedWithoutMandate.StartsWith("Canada limited"), "Adding a mandate in Brazil’s bill does not amend Canada’s offer.");
        Require(Reply(sourcingInOffer: true, sourcingInNextBill: false, legacy: true) == submittedWithoutMandate,
            "An offer already pending in a legacy save retains its original rules after upgrading.");
        Require(Reply(sourcingInOffer: true, sourcingInNextBill: false, legacy: true, upgradeBeforeOffer: true) == submittedWithMandate,
            "An offer opened after upgrading a legacy save uses its submitted terms.");

        var handover = new Campaign(2029, oversightSeed: 2029, agendaEnabled: false, variableWorld: true);
        int inaugurationDay = (Campaign.TermEnd - Campaign.Inauguration).Days;
        AdvanceTo(handover, inaugurationDay - 1);
        string partner = handover.Current.Ally;
        Require(handover.Trade(), "The outgoing president can submit an offer before the end of the term.");
        AdvanceTo(handover, inaugurationDay);
        Require(handover.Finished && handover.Briefings.Any(entry => entry.SceneTitle == "Open work on the desk"
            && entry.Text.Contains($"the trade reply from {partner}")), "The handover records the pending trade reply.");

        Console.WriteLine("Trade replies and briefings follow the original offer across crises, legacy save upgrades, and the handover.");
    }

    private static string Reply(bool sourcingInOffer, bool sourcingInNextBill, bool legacy = false, bool upgradeBeforeOffer = false)
    {
        var campaign = new Campaign(2045, oversightSeed: 2029, variableWorld: true, lockedTradeTerms: !legacy);
        AdvanceTo(campaign, 208);
        Require(campaign.Current.Ally == "Canada" && campaign.CanEdit, "The Canadian crisis opens with a bill.");
        if (sourcingInOffer) campaign.Toggle(Measure.DomesticSourcing);
        AdvanceTo(campaign, 304);
        if (upgradeBeforeOffer) Require(campaign.EnableLockedTradeTerms(), "The old campaign can upgrade before submitting an offer.");
        Require(campaign.Trade(), "The embassy can submit the offer before the crisis changes.");
        string? readout = campaign.TradeReadout;
        AdvanceTo(campaign, 305);
        Require(campaign.Current.Ally == "Brazil" && campaign.CanEdit && !campaign.HasMeasure(Measure.DomesticSourcing),
            "The next crisis opens with a separate bill and partner.");
        Require(campaign.PendingTradePartner == "Canada" && campaign.TradeReadout == readout && !campaign.CanTrade,
            "The diplomatic channel follows the pending partner until the reply arrives.");
        if (legacy && !upgradeBeforeOffer)
            Require(campaign.EnableLockedTradeTerms() && !campaign.EnableLockedTradeTerms(), "The old campaign upgrades once while its offer is pending.");
        if (sourcingInNextBill) campaign.Toggle(Measure.DomesticSourcing);
        while (campaign.PendingTradePartner != null)
        {
            Require(campaign.Day < 316, "The trade delegation replies within its promised window.");
            AdvanceTo(campaign, campaign.Day + 1);
        }
        Require(campaign.TradeReadout != readout, "The embassy readout returns to the active crisis after the reply.");
        return campaign.Briefings.Last(entry => entry.Date == campaign.Date && entry.Text.StartsWith("Canada ")).Text;
    }

    private static void AdvanceTo(Campaign campaign, int day)
    {
        while (campaign.Day < day)
        {
            if (campaign.OnDesk) Require(campaign.ResolveBill(true), "Sign the bill before advancing.");
            Require(campaign.Advance(Math.Min(120, day - campaign.Day)) > 0, "The campaign calendar must advance.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
