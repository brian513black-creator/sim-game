using GeopoliticsSim.Core;

internal static class WartimeStorySmoke
{
    public static void Run()
    {
        var renewed = NewCampaign();
        AwaitWorldWar(renewed);
        int firstWarDay = renewed.Day;
        NegotiateCeasefire(renewed);
        Require(renewed.Day < firstWarDay + 14, "The first war ends before the address becomes available.");

        AwaitWorldWar(renewed);
        int renewedWarDay = renewed.Day;
        Require(renewed.WorldWarBegan == renewed.Date && !HasAddress(renewed), "A new war starts a new address countdown.");
        while (renewed.Day < renewedWarDay + 13) Advance(renewed, 1);
        Require(!HasAddress(renewed), "The address stays unavailable through the thirteenth day of the new war.");
        Advance(renewed, 1);
        Require(HasAddress(renewed), "The address opens on the fourteenth day of the new war.");

        var peace = ReachAddressAndCeasefire(legacy: false);
        Require(!HasAddress(peace) && !peace.AnswerConversation(ConversationId.WartimeAddress, 1)
            && peace.Briefings.Any(entry => entry.SceneTitle == "The Address from the Oval Office · Canceled"),
            "A ceasefire closes the unanswered address and records the reason.");

        var savedAtPeace = ReachAddressAndCeasefire(legacy: true);
        Require(HasAddress(savedAtPeace), "Older rules can replay a pending address after the ceasefire.");
        Require(savedAtPeace.EnableScopedWartimeStory() && !savedAtPeace.EnableScopedWartimeStory() && !HasAddress(savedAtPeace),
            "Upgrading an old campaign closes the stale address once.");

        Console.WriteLine("Wartime story checks passed for a renewed war, ceasefire, and legacy save upgrade.");
    }

    private static Campaign ReachAddressAndCeasefire(bool legacy)
    {
        var campaign = NewCampaign(legacy);
        AwaitWorldWar(campaign);
        int firstWarDay = campaign.Day;
        while (!HasAddress(campaign) && campaign.Day < firstWarDay + 40) Advance(campaign, 1);
        Require(HasAddress(campaign), "Reach the unanswered wartime address.");
        NegotiateCeasefire(campaign);
        return campaign;
    }

    private static Campaign NewCampaign(bool legacy = false) =>
        new(2030, variableWorld: true, scopedWartimeStory: !legacy);

    private static bool HasAddress(Campaign campaign) => campaign.PendingConversation?.Id == ConversationId.WartimeAddress;

    private static void AwaitWorldWar(Campaign campaign)
    {
        for (int attempt = 0; !campaign.WorldWarActive && attempt < 50; attempt++)
        {
            string rival = campaign.PowerBrief("Russia").Rung <= campaign.PowerBrief("China").Rung ? "Russia" : "China";
            if (campaign.PressGreatPower(rival)) AwaitReply(campaign);
            else Advance(campaign, 7);
        }
        Require(campaign.WorldWarActive, "Reach the fictional world war branch.");
    }

    private static void NegotiateCeasefire(Campaign campaign)
    {
        Require(campaign.CallCeasefireSummit(), "Open the ceasefire summit.");
        AwaitReply(campaign);
        Require(!campaign.WorldWarActive && campaign.LastCeasefire == campaign.Date, "The summit reaches a ceasefire.");
    }

    private static void AwaitReply(Campaign campaign)
    {
        while (campaign.PendingForeignReply != null) Advance(campaign, 1);
    }

    private static void Advance(Campaign campaign, int days)
    {
        if (campaign.OnDesk) Require(campaign.ResolveBill(true), "Clear the bill before advancing.");
        if (campaign.PendingOffer != null) campaign.RespondToOffer(OfferResponse.Decline);
        if (campaign.PendingConversation is { Id: not ConversationId.WartimeAddress } conversation)
            campaign.AnswerConversation(conversation.Id, 0);
        Require(campaign.Advance(days) > 0, "The campaign calendar advances.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
