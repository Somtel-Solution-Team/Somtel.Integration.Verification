using Somtel.Integration.BuildingBlocks.Observability;

namespace Somtel.Integration.Hlr.Verification.Operations;

/// <summary>
/// Creating, changing and deleting a subscriber — the operations that cannot simply be undone.
///
/// Registration is verified by creating the subscriber and reading it back, because "the provider
/// accepted the request" and "the subscriber now exists" are different claims and only the second one
/// matters. Deletion is verified the same way, in reverse.
/// </summary>
public static class SubscriberLifecycleOperations
{
    public static async Task RunAsync(VerificationRun run, CancellationToken ct)
    {
        run.Target.RequireMutationAllowed("Subscriber lifecycle verification");

        var created = await RegisterAsync(run, ct);

        if (!created)
        {
            // Nothing below can say anything useful about a subscriber that was not created, and firing
            // the rest at a number that does not exist only produces noise against a production HLR.
            run.Skip("removeSubscriber", "not attempted — this run did not create the subscriber");
            return;
        }

        if (run.Target.SecondaryImsi is not null)
        {
            await ChangeImsiAsync(run, ct);
        }
        else
        {
            run.Skip("changeImsi", "needs --secondary-imsi (a second spare SIM to swap to)");
        }

        if (run.Target.KeepSubscriber)
        {
            run.Skip(
                "removeSubscriber",
                $"NOT DELETED by request (--keep) — {Sensitive.MaskMsisdn(run.Target.Msisdn)} is still provisioned " +
                "and must be removed by hand");
            return;
        }

        await RemoveAsync(run, ct);
    }

    /// <summary>
    /// Creates the subscriber, then reads it back. Returns whether it is actually there afterwards — the
    /// caller uses that to decide whether it may delete anything.
    /// </summary>
    public static async Task<bool> RegisterAsync(VerificationRun run, CancellationToken ct)
    {
        // Refuse to touch a number that is already in use: it is not disposable, and a later delete
        // would remove someone's real subscriber.
        var before = await run.Hlr.GetSubscriberInfoAsync(
            new GetSubscriberInfoQuery(run.Target.Msisdn, run.Target.Market), ct);

        if (before.RecordFound)
        {
            run.Skip(
                "registerSubscriber",
                "refused — a subscriber already exists on this number, so it is not a disposable target");
            return false;
        }

        await run.CallAsync(
            "registerSubscriber",
            () => run.Hlr.RegisterSubscriberAsync(
                new RegisterSubscriberCommand(run.Target.Msisdn, run.Target.Market, run.Target.RequireImsi()), ct),
            result => $"created as {result.CardType}  isdn={Sensitive.MaskMsisdn(result.Isdn)}  " +
                      $"imsi={Sensitive.MaskMsisdn(result.Imsi)}");

        var after = await run.Hlr.GetSubscriberInfoAsync(
            new GetSubscriberInfoQuery(run.Target.Msisdn, run.Target.Market), ct);

        await run.CallAsync(
            "registerSubscriber (read back)",
            () => Task.FromResult(after),
            result => result.RecordFound
                ? "the subscriber is there"
                : "THE SUBSCRIBER IS NOT THERE — creation reported success but the record is missing");

        if (after.RecordFound)
        {
            // Only now, and only because a read proved it. "The provider accepted the request" is not
            // enough to earn the right to delete afterwards.
            run.RecordSubscriberCreated();
        }

        return after.RecordFound;
    }

    /// <summary>Rebinds the subscriber to a different SIM, then confirms the new IMSI is the one on record.</summary>
    public static async Task ChangeImsiAsync(VerificationRun run, CancellationToken ct)
    {
        var newImsi = run.Target.RequireSecondaryImsi();

        await run.CallAsync(
            "changeImsi",
            () => run.Hlr.ChangeImsiAsync(new ChangeImsiCommand(run.Target.Msisdn, run.Target.Market, newImsi), ct),
            result => $"swapped to imsi={Sensitive.MaskMsisdn(result.NewImsi)}");

        await run.CallAsync(
            "changeImsi (read back)",
            () => run.Hlr.GetSubscriberInfoAsync(new GetSubscriberInfoQuery(run.Target.Msisdn, run.Target.Market), ct),
            result => string.Equals(result.Profile?.Imsi, newImsi, StringComparison.Ordinal)
                ? $"confirmed — the subscriber now reports imsi={Sensitive.MaskMsisdn(newImsi)}"
                : $"MISMATCH — expected {Sensitive.MaskMsisdn(newImsi)}, provider reports " +
                  $"{Sensitive.MaskMsisdn(result.Profile?.Imsi ?? "")}");
    }

    /// <summary>Deletes the subscriber and confirms it is gone. The authentication key is preserved.</summary>
    public static async Task RemoveAsync(VerificationRun run, CancellationToken ct)
    {
        // Asked here, once, rather than trusted to every caller. See VerificationRun.RequireCreatedByThisRun
        // for why: this used to be control flow in each group, and the `all` group forgot it.
        run.RequireCreatedByThisRun();

        await run.CallAsync(
            "removeSubscriber",
            () => run.Hlr.RemoveSubscriberAsync(new RemoveSubscriberCommand(run.Target.Msisdn, run.Target.Market), ct),
            result => $"deleted  isdn={Sensitive.MaskMsisdn(result.Isdn)}");

        await run.CallAsync(
            "removeSubscriber (read back)",
            () => run.Hlr.GetSubscriberInfoAsync(new GetSubscriberInfoQuery(run.Target.Msisdn, run.Target.Market), ct),
            result => result.RecordFound
                ? "THE SUBSCRIBER IS STILL THERE — delete it by hand before re-running"
                : "gone — the HLR is as this run found it");
    }
}
