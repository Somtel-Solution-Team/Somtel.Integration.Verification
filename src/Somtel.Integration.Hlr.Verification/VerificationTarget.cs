using Somtel.Integration.BuildingBlocks.Errors;

namespace Somtel.Integration.Hlr.Verification;

/// <summary>
/// Who a verification run acts on, and what it is permitted to do to them.
///
/// Every identifier here is supplied by a person on the command line. There is deliberately no way to
/// discover a subscriber: no lookup, no "find me a test number", no scan. An automated picker could
/// choose a real customer, so the only way to name a target is for a human to type it — which keeps a
/// human accountable for every identifier that gets mutated (DIV-STD-001).
/// </summary>
public sealed record VerificationTarget
{
    private VerificationTarget(string market, string msisdn, string? imsi, string? secondaryImsi, bool allowMutation, bool keepSubscriber)
    {
        Market = market;
        Msisdn = msisdn;
        Imsi = imsi;
        SecondaryImsi = secondaryImsi;
        AllowMutation = allowMutation;
        KeepSubscriber = keepSubscriber;
    }

    public string Market { get; }

    /// <summary>The subscriber number, always human-supplied.</summary>
    public string Msisdn { get; }

    /// <summary>A spare SIM's IMSI, needed only by operations that attach one.</summary>
    public string? Imsi { get; }

    /// <summary>A second spare SIM, needed by SIM-swap and multi-IMSI.</summary>
    public string? SecondaryImsi { get; }

    /// <summary>State-changing calls are refused without this. Read-only verification never needs it.</summary>
    public bool AllowMutation { get; }

    /// <summary>When set, a subscriber this run created is left in place rather than deleted.</summary>
    public bool KeepSubscriber { get; }

    public static VerificationTarget Create(
        string? market, string? msisdn, string? imsi, string? secondaryImsi, bool allowMutation, bool keepSubscriber)
    {
        if (string.IsNullOrWhiteSpace(market))
        {
            throw new IntegrationException(CanonicalErrorCode.ValidationError, "--market is required.");
        }

        if (string.IsNullOrWhiteSpace(msisdn))
        {
            // The whole safety model rests on this: no target, no run.
            throw new IntegrationException(
                CanonicalErrorCode.ValidationError,
                "--msisdn is required. Verification never selects a subscriber itself — the target is always " +
                "supplied by a person, so that a human is accountable for every identifier touched.");
        }

        return new VerificationTarget(market, msisdn, imsi, secondaryImsi, allowMutation, keepSubscriber);
    }

    /// <summary>Refuses a state-changing group unless the operator explicitly accepted the consequence.</summary>
    public void RequireMutationAllowed(string what)
    {
        if (!AllowMutation)
        {
            throw new IntegrationException(
                CanonicalErrorCode.ValidationError,
                $"{what} changes real subscriber state. Add --allow-mutation, and point it only at a " +
                "subscriber you are permitted to modify.");
        }
    }

    public string RequireImsi() =>
        Imsi ?? throw new IntegrationException(
            CanonicalErrorCode.ValidationError,
            "--imsi is required for this group: a spare SIM that already exists in the AuC. Note this is the " +
            "15-digit IMSI, not the 19-20 digit ICCID printed on the SIM.");

    public string RequireSecondaryImsi() =>
        SecondaryImsi ?? throw new IntegrationException(
            CanonicalErrorCode.ValidationError,
            "--secondary-imsi is required for this group: a second spare SIM, distinct from --imsi.");
}
