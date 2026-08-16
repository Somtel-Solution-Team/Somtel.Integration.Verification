using Somtel.Integration.BuildingBlocks.Observability;

namespace Somtel.Integration.Hlr.Verification.Operations;

/// <summary>
/// Data provisioning and multi-SIM — the operations whose parameters come entirely from the market's
/// own configuration.
///
/// Nothing here chooses a value. A market with no configured profile is refused by the integration
/// project itself, and that refusal is reported as the result: it is the correct behaviour, not a gap in
/// the verification.
/// </summary>
public static class ProvisioningOperations
{
    public static async Task RunAsync(VerificationRun run, CancellationToken ct)
    {
        run.Target.RequireMutationAllowed("Provisioning verification");

        await DataProfileAsync(run, ct);
        await EpsProfileAsync(run, ct);
        await FiveGProfileAsync(run, ct);
        await MultiImsiAsync(run, ct);
    }

    /// <summary>Applies the market's packet-data profile.</summary>
    public static Task DataProfileAsync(VerificationRun run, CancellationToken ct) =>
        run.CallAsync(
            "applyDataProfile",
            () => run.Hlr.ApplyDataProfileAsync(new ApplyDataProfileCommand(run.Target.Msisdn, run.Target.Market), ct),
            result => $"applied profile={result.ProfileName}");

    /// <summary>Turns EPS (4G) on with the market's AMBR limits, confirms, then restores.</summary>
    public static async Task EpsProfileAsync(VerificationRun run, CancellationToken ct)
    {
        await run.CallAsync(
            "applyEpsProfile (on)",
            () => run.Hlr.ApplyEpsProfileAsync(
                new ApplyEpsProfileCommand(run.Target.Msisdn, run.Target.Market, true), ct),
            result => $"applied provisioned={result.Provisioned} profile={result.ProfileName}");

        await run.CallAsync(
            "applyEpsProfile (off)",
            () => run.Hlr.ApplyEpsProfileAsync(
                new ApplyEpsProfileCommand(run.Target.Msisdn, run.Target.Market, false), ct),
            result => $"applied provisioned={result.Provisioned}");
    }

    /// <summary>
    /// Applies the market's 5G bandwidth profile. Markets without one configured are refused by the
    /// integration project rather than sent another market's limits.
    /// </summary>
    public static Task FiveGProfileAsync(VerificationRun run, CancellationToken ct) =>
        run.CallAsync(
            "applyFiveGProfile",
            () => run.Hlr.ApplyFiveGProfileAsync(new ApplyFiveGProfileCommand(run.Target.Msisdn, run.Target.Market), ct),
            result => $"applied profile={result.ProfileName}");

    /// <summary>
    /// Attaches a second SIM to the number, confirms it, then detaches it. Needs a distinct second SIM —
    /// attaching a subscriber's own IMSI as its secondary is meaningless and the provider refuses it,
    /// which would look like a defect rather than a missing input.
    /// </summary>
    public static async Task MultiImsiAsync(VerificationRun run, CancellationToken ct)
    {
        if (run.Target.SecondaryImsi is null)
        {
            run.Skip("setMultiImsi", "needs --secondary-imsi, distinct from the SIM already on the subscriber");
            return;
        }

        var secondary = run.Target.RequireSecondaryImsi();

        try
        {
            await run.CallAsync(
                "setMultiImsi (attach)",
                () => run.Hlr.SetMultiImsiAsync(
                    new SetMultiImsiCommand(run.Target.Msisdn, run.Target.Market, secondary, MultiImsiChange.Add), ct),
                result => $"attached imsi={Sensitive.MaskMsisdn(result.SecondaryImsi)}");

            await run.CallAsync(
                "setMultiImsi (read back)",
                () => run.Hlr.GetMultiImsiAsync(new GetMultiImsiQuery(run.Target.Msisdn, run.Target.Market), ct),
                result => result.Provisioned
                    ? "confirmed — the subscriber now reports multi-IMSI provisioned"
                    : "NOT APPLIED — the subscriber still reports multi-IMSI as not provisioned");
        }
        finally
        {
            await run.CallAsync(
                "setMultiImsi (detach)",
                () => run.Hlr.SetMultiImsiAsync(
                    new SetMultiImsiCommand(run.Target.Msisdn, run.Target.Market, secondary, MultiImsiChange.Remove), ct),
                _ => "detached again");
        }
    }

    /// <summary>
    /// Provides then withdraws the market's PLMN-specific service (ring-back tone where configured).
    /// Which slot carries which service is HLR configuration, so a market without one is refused.
    /// </summary>
    public static async Task PlmnSpecificServiceAsync(VerificationRun run, CancellationToken ct)
    {
        try
        {
            await run.CallAsync(
                "setPlmnSpecificService (provide)",
                () => run.Hlr.SetPlmnSpecificServiceAsync(
                    new SetPlmnSpecificServiceCommand(run.Target.Msisdn, run.Target.Market, true), ct),
                result => $"provisioned={result.Provisioned}");
        }
        finally
        {
            await run.CallAsync(
                "setPlmnSpecificService (restore)",
                () => run.Hlr.SetPlmnSpecificServiceAsync(
                    new SetPlmnSpecificServiceCommand(run.Target.Msisdn, run.Target.Market, false), ct),
                _ => "withdrawn again");
        }
    }
}
