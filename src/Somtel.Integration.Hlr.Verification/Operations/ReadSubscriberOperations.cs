using Somtel.Integration.BuildingBlocks.Observability;

namespace Somtel.Integration.Hlr.Verification.Operations;

/// <summary>
/// Reading a subscriber. Every operation here is read-only, so it is safe against production at any time
/// given credentials — no mutation flag, nothing to restore.
///
/// Each one calls the real public function on the real integration service and prints what actually came
/// back, with identifiers masked because these runs get pasted into tickets.
/// </summary>
public static class ReadSubscriberOperations
{
    public static async Task RunAsync(VerificationRun run, CancellationToken ct)
    {
        await GetSubscriberInfoAsync(run, ct);
        await GetLockStateAsync(run, ct);
        await GetMultiImsiAsync(run, ct);

        if (run.Target.Imsi is not null)
        {
            await GetAuthenticationProfileAsync(run, ct);
        }
        else
        {
            run.Skip("getAuthenticationProfile", "needs --imsi (the SIM to read authentication data for)");
        }
    }

    /// <summary>Reads the subscriber's whole HLR record.</summary>
    public static Task GetSubscriberInfoAsync(VerificationRun run, CancellationToken ct) =>
        run.CallAsync(
            "getSubscriberInfo",
            () => run.Hlr.GetSubscriberInfoAsync(new GetSubscriberInfoQuery(run.Target.Msisdn, run.Target.Market), ct),
            result =>
            {
                if (!result.RecordFound)
                {
                    // Not an error: a lookup that finds nothing has still answered the question.
                    return $"recordFound=false — no subscriber on {Sensitive.MaskMsisdn(result.Isdn)} in {result.Market}";
                }

                var p = result.Profile;
                return $"recordFound=true  isdn={Sensitive.MaskMsisdn(result.Isdn)}  fields={result.Details.Count}" +
                       $"{Environment.NewLine}     sim={p?.SimType ?? "-"} category={p?.Category ?? "-"} " +
                       $"imsi={Sensitive.MaskMsisdn(p?.Imsi ?? "")}" +
                       $"{Environment.NewLine}     network={p?.NetworkAccess ?? "-"} data={(p?.Data.Provisioned ?? false ? "on" : "off")} " +
                       $"ctx={p?.Data.ContextId ?? "-"} apn={p?.Data.ApnTemplateId ?? "-"}" +
                       $"{Environment.NewLine}     services CFU={p?.Services.CallForwardingUnconditional} CW={p?.Services.CallWaiting} " +
                       $"HOLD={p?.Services.CallHold} MPTY={p?.Services.Multiparty} CLIP={p?.Services.CallerIdDisplay}";
            });

    /// <summary>Reads call barring — what a customer means by "is my line blocked?".</summary>
    public static Task GetLockStateAsync(VerificationRun run, CancellationToken ct) =>
        run.CallAsync(
            "getLockState",
            () => run.Hlr.GetLockStateAsync(new GetLockStateQuery(run.Target.Msisdn, run.Target.Market), ct),
            result =>
                $"blocked={result.Blocked}  incoming={result.IncomingCallsBarred} outgoing={result.OutgoingCallsBarred}" +
                $"{Environment.NewLine}     gprs={result.GprsLocked} eps={result.EpsLocked} non3gpp={result.Non3GppLocked}");

    /// <summary>Reads whether extra SIMs are attached to this number.</summary>
    public static Task GetMultiImsiAsync(VerificationRun run, CancellationToken ct) =>
        run.CallAsync(
            "getMultiImsi",
            () => run.Hlr.GetMultiImsiAsync(new GetMultiImsiQuery(run.Target.Msisdn, run.Target.Market), ct),
            result =>
                $"provisioned={result.Provisioned}  primaryImsi={Sensitive.MaskMsisdn(result.PrimaryImsi ?? "")}  " +
                $"fields={result.Details.Count}");

    /// <summary>Reads a SIM's generation from the AuC. The authentication key is never surfaced.</summary>
    public static Task GetAuthenticationProfileAsync(VerificationRun run, CancellationToken ct) =>
        run.CallAsync(
            "getAuthenticationProfile",
            () => run.Hlr.GetAuthenticationProfileAsync(
                new GetAuthenticationProfileQuery(run.Target.RequireImsi(), run.Target.Market), ct),
            result => $"cardType={result.CardType}  imsi={Sensitive.MaskMsisdn(result.Imsi)}");
}
