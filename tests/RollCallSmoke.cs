using GeopoliticsSim.Core;

internal static class RollCallSmoke
{
    public static void Run()
    {
        var stalled = OpeningVote(2029, passed: false);
        CheckChamber(stalled, senate: false, expectedYes: 276, size: 435);
        CheckChamber(stalled, senate: true, expectedYes: 58, size: 100);
        Require(stalled.WhipCount(false).Count(vote => vote.Yes) != 276, "The failed pledge changes preferences after the ballots are cast.");

        foreach (bool signed in new[] { true, false })
        {
            var passed = OpeningVote(2045, passed: true);
            var house = CheckChamber(passed, senate: false, expectedYes: 264, size: 435);
            var senate = CheckChamber(passed, senate: true, expectedYes: 60, size: 100);
            Require(passed.ResolveBill(signed), "The president can decide the passed bill.");
            Require(passed.FloorRollCall(false)!.SequenceEqual(house) && passed.FloorRollCall(true)!.SequenceEqual(senate),
                "Signing or vetoing a bill preserves the ballots cast by all 535 lawmakers.");

            while (passed.Day < 64)
                Require(passed.Advance(64 - passed.Day) > 0, "The campaign can advance to the next crisis.");
            Require(passed.LastFloorVote == null && passed.FloorRollCall(false) == null && passed.FloorRollCall(true) == null,
                "A new crisis clears the prior bill's roll call.");
        }

        Console.WriteLine("House and Senate roll calls stay consistent after a bill stalls, is signed, or is vetoed.");
    }

    private static Campaign OpeningVote(int seed, bool passed)
    {
        var campaign = new Campaign(seed, oversightSeed: 2029, variableWorld: true);
        Require(campaign.MakeOpeningCommitment(OpeningCommitment.ProtectJobs), "Make the jobs pledge before the vote.");
        Require(campaign.FloorRollCall(false) == null && campaign.FloorRollCall(true) == null, "The floor vote begins with no recorded ballots.");
        Require(campaign.Advance(21) == 21 && campaign.OnDesk == passed, "Reach the expected opening vote outcome.");
        return campaign;
    }

    private static Vote[] CheckChamber(Campaign campaign, bool senate, int expectedYes, int size)
    {
        var votes = campaign.FloorRollCall(senate);
        int? recordedYes = senate ? campaign.LastFloorVote?.Senate : campaign.LastFloorVote?.House;
        Require(votes != null && votes.Count == size && votes.Count(vote => vote.Yes) == recordedYes && recordedYes == expectedYes,
            "The recorded ballots match the official tally and chamber size.");
        Require(votes!.All(vote => !vote.Swing), "Every recorded ballot has a final choice.");
        return votes!.ToArray();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
