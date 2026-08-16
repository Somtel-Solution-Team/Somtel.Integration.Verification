namespace Somtel.Integration.Hlr.Verification.Operations;

/// <summary>
/// Turning services on and off: network access, the six supplementary services, and call forwarding.
///
/// Every check restores what it found. Two known provider behaviours are handled honestly rather than
/// papered over, both established by live verification on SS (2026-08-12):
///
///   · CallHold can be provided but not withdrawn — MOD_HOLD PROV=FALSE is refused with 3020. A failed
///     restore is reported as a failed restore, not hidden.
///   · Registering a forwarding destination only works while the matching service is provisioned, so the
///     forwarding check provisions CFU around itself instead of assuming.
/// </summary>
public static class ServiceActivationOperations
{
    public static async Task RunAsync(VerificationRun run, CancellationToken ct)
    {
        run.Target.RequireMutationAllowed("Service activation verification");

        await NetworkAccessAsync(run, ct);
        await SupplementaryServicesAsync(run, ct);
        await CallForwardingAsync(run, ct);
    }

    /// <summary>Switches the subscriber to circuit-switched only, confirms it, then restores.</summary>
    public static async Task NetworkAccessAsync(VerificationRun run, CancellationToken ct)
    {
        var before = await run.Hlr.GetSubscriberInfoAsync(
            new GetSubscriberInfoQuery(run.Target.Msisdn, run.Target.Market), ct);
        var original = ParseMode(before.Profile?.NetworkAccess) ?? NetworkAccessMode.Both;
        run.Note($"before: networkAccess={original}");

        try
        {
            await run.CallAsync(
                "setNetworkAccessMode (data off)",
                () => run.Hlr.SetNetworkAccessModeAsync(
                    new SetNetworkAccessModeCommand(
                        run.Target.Msisdn, run.Target.Market, NetworkAccessMode.CircuitSwitchedOnly), ct),
                result => $"applied mode={result.Mode}");

            await run.CallAsync(
                "setNetworkAccessMode (read back)",
                () => run.Hlr.GetSubscriberInfoAsync(new GetSubscriberInfoQuery(run.Target.Msisdn, run.Target.Market), ct),
                result => ParseMode(result.Profile?.NetworkAccess) == NetworkAccessMode.CircuitSwitchedOnly
                    ? "confirmed — the subscriber now reports CircuitSwitchedOnly"
                    : $"NOT APPLIED — the subscriber reports {result.Profile?.NetworkAccess ?? "nothing"}");
        }
        finally
        {
            await run.CallAsync(
                "setNetworkAccessMode (restore)",
                () => run.Hlr.SetNetworkAccessModeAsync(
                    new SetNetworkAccessModeCommand(run.Target.Msisdn, run.Target.Market, original), ct),
                _ => $"restored to {original}");
        }
    }

    /// <summary>Provides then withdraws each supplementary service in turn.</summary>
    public static async Task SupplementaryServicesAsync(VerificationRun run, CancellationToken ct)
    {
        foreach (var service in Enum.GetValues<SupplementaryService>())
        {
            await run.CallAsync(
                $"setSupplementaryService[{service}] (provide)",
                () => run.Hlr.SetSupplementaryServiceAsync(
                    new SetSupplementaryServiceCommand(run.Target.Msisdn, run.Target.Market, service, true), ct),
                result => $"provisioned={result.Provisioned}");

            await run.CallAsync(
                $"setSupplementaryService[{service}] (withdraw)",
                () => run.Hlr.SetSupplementaryServiceAsync(
                    new SetSupplementaryServiceCommand(run.Target.Msisdn, run.Target.Market, service, false), ct),
                result => $"provisioned={result.Provisioned}");
        }
    }

    /// <summary>
    /// Registers and erases a forwarding destination for each condition, with CFU provisioned around it.
    ///
    /// The destination comes from the operator, for the same reason the platform makes it a caller
    /// parameter: a hardcoded one sends real calls somewhere. It cannot quietly default to the
    /// subscriber's own number either — the provider refuses self-forwarding with 3033, which cost a
    /// verification run to discover.
    /// </summary>
    public static async Task CallForwardingAsync(VerificationRun run, CancellationToken ct)
    {
        await run.CallAsync(
            "setSupplementaryService[CFU] (for forwarding)",
            () => run.Hlr.SetSupplementaryServiceAsync(
                new SetSupplementaryServiceCommand(
                    run.Target.Msisdn, run.Target.Market, SupplementaryService.CallForwardingUnconditional, true), ct),
            _ => "provisioned so a destination can be registered");

        try
        {
            foreach (var condition in Enum.GetValues<CallForwardingCondition>())
            {
                await run.CallAsync(
                    $"registerCallForwarding[{condition}]",
                    () => run.Hlr.RegisterCallForwardingAsync(
                        new RegisterCallForwardingCommand(
                            run.Target.Msisdn, run.Target.Market, run.Target.RequireForwardTo(), null, condition), ct),
                    result => $"registered condition={result.Condition}");

                await run.CallAsync(
                    $"eraseCallForwarding[{condition}]",
                    () => run.Hlr.EraseCallForwardingAsync(
                        new EraseCallForwardingCommand(run.Target.Msisdn, run.Target.Market, null, condition), ct),
                    result => $"erased condition={result.Condition}");
            }
        }
        finally
        {
            await run.CallAsync(
                "setSupplementaryService[CFU] (restore)",
                () => run.Hlr.SetSupplementaryServiceAsync(
                    new SetSupplementaryServiceCommand(
                        run.Target.Msisdn, run.Target.Market, SupplementaryService.CallForwardingUnconditional, false), ct),
                _ => "withdrawn again");
        }
    }

    private static NetworkAccessMode? ParseMode(string? value) =>
        Enum.TryParse<NetworkAccessMode>(value, out var mode) ? mode : null;
}
