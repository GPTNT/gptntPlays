// Counts cycles that were available at a player decision or caused a strike.
// Terminal reconciliation allows a delayed strike callback to settle first.
public sealed class NeedyCycleTelemetry
{
    private const int Running = 2;
    private const int Cooldown = 3;
    private bool openCycle;
    private bool cycleEligible;
    private bool cycleStruck;
    private bool terminalPending;
    private int terminalState;

    public int ActivationCount { get; private set; }
    public int SuccessCount { get; private set; }
    public int StrikeCount { get; private set; }
    public int UnresolvedCount { get; private set; }
    public bool? ActiveAtEnd { get; private set; }
    public bool TerminalReconciled { get; private set; }
    public bool CountsReliable { get; private set; } = true;

    public void Observe(int state)
    {
        if (terminalPending || TerminalReconciled) return;
        if (state == Running && !openCycle)
        {
            openCycle = true;
            cycleEligible = false;
            cycleStruck = false;
        }
        else if (state == Cooldown && openCycle)
        {
            if (cycleEligible && !cycleStruck)
            {
                SuccessCount++;
            }
            openCycle = false;
            cycleStruck = false;
        }
    }

    public void Opportunity(int state)
    {
        Observe(state);
        if (!terminalPending && !TerminalReconciled && openCycle && !cycleEligible)
        {
            cycleEligible = true;
            ActivationCount++;
        }
    }

    public void Strike()
    {
        StrikeCount++;
        if (openCycle)
        {
            if (!cycleEligible) { cycleEligible = true; ActivationCount++; }
            cycleStruck = true;
        }
        // Without an open cycle we cannot distinguish a delayed callback from
        // a strike in a new cycle that state polling did not observe.
        else CountsReliable = false;
    }

    public void End(int state)
    {
        if (terminalPending || TerminalReconciled) return;
        terminalState = state;
        terminalPending = true;
    }

    public void FinalizeEnd()
    {
        if (!terminalPending || TerminalReconciled) return;
        if (openCycle && cycleStruck) ActiveAtEnd = false;
        else if (!openCycle || !cycleEligible) ActiveAtEnd = false;
        else if (terminalState == 5) UnresolvedCount++;
        else if (terminalState == Running) ActiveAtEnd = true;
        else if (terminalState == Cooldown)
        {
            SuccessCount++;
            ActiveAtEnd = false;
        }
        else UnresolvedCount++;
        terminalPending = false;
        TerminalReconciled = true;
    }
}
