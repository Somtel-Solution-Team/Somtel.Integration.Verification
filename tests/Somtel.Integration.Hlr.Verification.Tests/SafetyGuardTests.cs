using Somtel.Integration.BuildingBlocks.Errors;
using Xunit;

namespace Somtel.Integration.Hlr.Verification.Tests;

/// <summary>
/// The rules that stand between a command line and a production subscriber.
///
/// There is no test HLR for any market, so every run of this tool happens to a real person's service.
/// These guards are the whole safety model, and until now they were enforced only by the shape of the
/// code — which is exactly the kind of thing that survives a refactor by accident and then does not.
/// </summary>
public class SafetyGuardTests
{
    private static VerificationTarget Target(bool allowMutation = false, bool keep = false) =>
        VerificationTarget.Create("SS", "252629001441", null, null, null, allowMutation, keep);

    // ------------------------------------------------------------------ the target is always a person's choice

    [Fact]
    public void A_run_without_a_subscriber_is_refused()
    {
        var error = Assert.Throws<IntegrationException>(
            () => VerificationTarget.Create("SS", null, null, null, null, allowMutation: true, keepSubscriber: false));

        Assert.Equal(CanonicalErrorCode.ValidationError, error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_is_not_a_subscriber(string msisdn) =>
        Assert.Throws<IntegrationException>(
            () => VerificationTarget.Create("SS", msisdn, null, null, null, allowMutation: true, keepSubscriber: false));

    [Fact]
    public void A_run_without_a_market_is_refused() =>
        Assert.Throws<IntegrationException>(
            () => VerificationTarget.Create(null, "252629001441", null, null, null, allowMutation: true, keepSubscriber: false));

    /// <summary>
    /// The tool must have no way to *find* a subscriber. This is a design constraint, not a preference:
    /// an automated picker could choose a real customer, so a human names every identifier that gets
    /// touched. If someone ever adds a lookup, this test is what should stop them.
    /// </summary>
    [Fact]
    public void Nothing_on_the_target_can_discover_a_subscriber()
    {
        var discovery = typeof(VerificationTarget)
            .GetMethods()
            .Where(m => m.Name.Contains("Find", StringComparison.OrdinalIgnoreCase)
                     || m.Name.Contains("Search", StringComparison.OrdinalIgnoreCase)
                     || m.Name.Contains("Pick", StringComparison.OrdinalIgnoreCase)
                     || m.Name.Contains("Any", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Name)
            .ToList();

        Assert.True(
            discovery.Count == 0,
            "VerificationTarget must never be able to select a subscriber itself. Found: " +
            string.Join(", ", discovery));
    }

    // ------------------------------------------------------------------ mutation needs an explicit yes

    [Fact]
    public void State_changing_work_is_refused_without_allow_mutation()
    {
        var error = Assert.Throws<IntegrationException>(() => Target().RequireMutationAllowed("Barring verification"));

        Assert.Equal(CanonicalErrorCode.ValidationError, error.Code);
        Assert.Contains("--allow-mutation", error.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void State_changing_work_proceeds_once_it_is_allowed()
    {
        var exception = Record.Exception(() => Target(allowMutation: true).RequireMutationAllowed("Barring verification"));

        Assert.Null(exception);
    }

    // Read-only verification must never require the flag: if it did, people would get used to passing
    // --allow-mutation for everything and the flag would stop meaning anything.
    [Fact]
    public void Reading_never_needs_permission_to_mutate() =>
        Assert.False(Target().AllowMutation);

    // ------------------------------------------------------------------ inputs that must be supplied, not guessed

    [Fact]
    public void A_forwarding_destination_is_never_defaulted()
    {
        var error = Assert.Throws<IntegrationException>(() => Target(allowMutation: true).RequireForwardTo());

        // A default here forwards a real subscriber's calls somewhere. The legacy templates hardcoded one
        // market's number; the provider also refuses forwarding a number to itself (3033).
        Assert.Contains("--forward-to", error.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_spare_sim_is_never_defaulted() =>
        Assert.Throws<IntegrationException>(() => Target(allowMutation: true).RequireImsi());

    [Fact]
    public void A_second_spare_sim_is_never_defaulted() =>
        Assert.Throws<IntegrationException>(() => Target(allowMutation: true).RequireSecondaryImsi());

    [Fact]
    public void The_two_spare_sims_are_kept_distinct()
    {
        var target = VerificationTarget.Create(
            "SS", "252629001441", "637820000000001", "637820000000009", null, allowMutation: true, keepSubscriber: false);

        Assert.NotEqual(target.RequireImsi(), target.RequireSecondaryImsi());
    }

    // ------------------------------------------------------------------ what a run may delete

    [Fact]
    public void Keeping_a_subscriber_is_off_unless_asked_for() =>
        Assert.False(Target(allowMutation: true).KeepSubscriber);

    [Fact]
    public void Keep_is_carried_through_so_a_created_subscriber_survives_the_run() =>
        Assert.True(Target(allowMutation: true, keep: true).KeepSubscriber);
}
