namespace Somtel.Integration.Hlr.Verification.Operations;

/// <summary>
/// Barring and unbarring — blocking a line and releasing it again.
///
/// Every check here is apply → read back → restore. Verifying that the provider *accepted* a barring
/// request says nothing useful; verifying that the subscriber is actually barred afterwards, and then
/// putting it back, is the whole point. The restore runs even when the read-back disagrees, so a failed
/// verification never leaves a line barred.
/// </summary>
public static class BarringOperations
{
    public static async Task RunAsync(VerificationRun run, CancellationToken ct)
    {
        run.Target.RequireMutationAllowed("Barring verification");

        await BarThenUnbarAsync(run, ct);
        await RoamingBarThenUnbarAsync(run, ct);
    }

    /// <summary>Bars both directions, confirms the subscriber reads as blocked, then clears it.</summary>
    public static async Task BarThenUnbarAsync(VerificationRun run, CancellationToken ct)
    {
        var before = await run.Hlr.GetLockStateAsync(new GetLockStateQuery(run.Target.Msisdn, run.Target.Market), ct);
        run.Note($"before: blocked={before.Blocked} (this run restores it to exactly this)");

        try
        {
            await run.CallAsync(
                "setLockState (bar)",
                () => run.Hlr.SetLockStateAsync(
                    new SetLockStateCommand(run.Target.Msisdn, run.Target.Market, true, true), ct),
                result => $"applied incoming={result.IncomingCallsBarred} outgoing={result.OutgoingCallsBarred}");

            await run.CallAsync(
                "setLockState (read back)",
                () => run.Hlr.GetLockStateAsync(new GetLockStateQuery(run.Target.Msisdn, run.Target.Market), ct),
                result => result.Blocked
                    ? "confirmed — the subscriber now reads as blocked"
                    : "NOT APPLIED — the provider accepted the request but the subscriber still reads as unblocked");
        }
        finally
        {
            await run.CallAsync(
                "setLockState (restore)",
                () => run.Hlr.SetLockStateAsync(
                    new SetLockStateCommand(
                        run.Target.Msisdn, run.Target.Market, before.IncomingCallsBarred, before.OutgoingCallsBarred),
                    ct),
                _ => $"restored to incoming={before.IncomingCallsBarred} outgoing={before.OutgoingCallsBarred}");
        }
    }

    /// <summary>
    /// Bars roaming and releases it. Separate from call barring because it is a separate provider command
    /// and a separate customer-facing decision — a subscriber can be barred from roaming while calling
    /// normally at home. No verified read-back command exists for it, so the result is reported as applied
    /// rather than confirmed.
    /// </summary>
    public static async Task RoamingBarThenUnbarAsync(VerificationRun run, CancellationToken ct)
    {
        try
        {
            await run.CallAsync(
                "setRoamingBarred (bar)",
                () => run.Hlr.SetRoamingBarredAsync(
                    new SetRoamingBarredCommand(run.Target.Msisdn, run.Target.Market, true), ct),
                result => $"applied roamingBarred={result.RoamingBarred} (no verified read-back for this one)");
        }
        finally
        {
            await run.CallAsync(
                "setRoamingBarred (restore)",
                () => run.Hlr.SetRoamingBarredAsync(
                    new SetRoamingBarredCommand(run.Target.Msisdn, run.Target.Market, false), ct),
                _ => "restored to unbarred");
        }
    }
}
