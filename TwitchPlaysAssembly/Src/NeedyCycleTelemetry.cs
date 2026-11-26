// Tracks the native needy state each frame and defers terminal reconciliation until
// the component strike callback has had a chance to run.
public sealed class NeedyCycleTelemetry
{
    private const int Running = 2;
    private const int Cooldown = 3;
    private bool openCycle;
    private bool cycleStruck;
    private bool lastCycleCountedSuccess;
    private bool terminalPending;
    private int terminalState;

    public int ActivationCount { get; private set; }
    public int SuccessCount { get; private set; }
    public int StrikeCount { get; private set; }
    public int UnresolvedCount { get; private set; }
    public bool? ActiveAtEnd { get; private set; }
    public bool TerminalReconciled { get; private set; }

    public void Observe(int state)
    {
        if (terminalPending || TerminalReconciled) return;
        if (state == Running && !openCycle)
        {
            ActivationCount++;
            openCycle = true;
            cycleStruck = false;
            lastCycleCountedSuccess = false;
        }
        else if (state == Cooldown && openCycle)
        {
            if (!cycleStruck)
            {
                SuccessCount++;
                lastCycleCountedSuccess = true;
            }
            openCycle = false;
            cycleStruck = false;
        }
    }

    public void Strike()
    {
        StrikeCount++;
        if (openCycle) cycleStruck = true;
        else if (lastCycleCountedSuccess)
        {
            // A component strike callback can arrive after the cooldown state.
            SuccessCount--;
            lastCycleCountedSuccess = false;
        }
    }

    public void End(int state)
    {
        if (terminalPending || TerminalReconciled) return;
        if (state == Running && !openCycle) Observe(state);
        terminalState = state;
        terminalPending = true;
    }

    public void FinalizeEnd()
    {
        if (!terminalPending || TerminalReconciled) return;
        if (openCycle && cycleStruck) ActiveAtEnd = false;
        else if (terminalState == 5)
        {
            // Off can be imposed by the bomb ending; it does not prove whether
            // an unobserved cycle was active. Count only a known open cycle.
            if (openCycle) UnresolvedCount++;
        }
        else if (!openCycle) ActiveAtEnd = false;
        else if (terminalState == Running) ActiveAtEnd = true;
        else if (terminalState == Cooldown)
        {
            SuccessCount++;
            lastCycleCountedSuccess = true;
            ActiveAtEnd = false;
        }
        else UnresolvedCount++;
        terminalPending = false;
        TerminalReconciled = true;
    }
}
