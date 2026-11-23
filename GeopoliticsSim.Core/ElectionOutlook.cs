namespace GeopoliticsSim.Core;

public sealed record ElectionOutlook(StateResult Factors, bool Visited)
{
    public State State => Factors.State;
    public double Margin => Factors.Margin;
    public double MarginAfterVisit => Margin + (Visited ? 0 : Campaign.RallyMarginBonus);
    public double WinChance => SnapshotChance(Margin);
    public double WinChanceAfterVisit => SnapshotChance(MarginAfterVisit);
    public double ValueOfVisit => State.Electors * (WinChanceAfterVisit - WinChance);

    private static double SnapshotChance(double margin) =>
        Math.Clamp((margin + Campaign.ElectionDaySwing) / (2 * Campaign.ElectionDaySwing), 0, 1);
}
