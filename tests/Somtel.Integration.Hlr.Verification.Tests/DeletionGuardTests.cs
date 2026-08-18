using Somtel.Integration.BuildingBlocks.Errors;
using Xunit;

namespace Somtel.Integration.Hlr.Verification.Tests;

/// <summary>
/// The rule this tool exists to not break: <b>a run may only delete a subscriber it created itself.</b>
///
/// <para>
/// This was the one guard that had no test, and the reason given was that proving it needed something
/// reporting "this subscriber already exists" — which nothing here may pretend to be (DIV-STD-001). That
/// reason was true of the old design and not of the rule. The guard used to be control flow: every group
/// checked the registration result and skipped removal when it was false. Testing *that* really would
/// have needed a provider, because the decision only existed part-way through a live sequence.
/// </para>
///
/// <para>
/// So the design moved instead of the test. The run now knows whether it created the subscriber, and
/// deletion asks it. The refusal happens before any provider call, which makes it ordinary state to
/// assert — no fake, no network, no subscriber.
/// </para>
///
/// <para>
/// It is worth saying what this protects. The <c>all</c> group once discarded the registration result, so
/// a run against a number already in use would have driven every mutating group at a live customer and
/// then deleted them. Every individual guard was present; one call site ignoring them undid all of them.
/// A guard that each caller must remember is a guard that will eventually be forgotten.
/// </para>
/// </summary>
public class DeletionGuardTests
{
    private static VerificationRun Run() =>
        new(hlr: null!, VerificationTarget.Create(
            "SS", "252629001441", null, null, null, allowMutation: true, keepSubscriber: false));

    // The service is deliberately null above: if any of these tests ever reached the integration DLL it
    // would throw a NullReferenceException rather than pass quietly. Nothing here may touch a provider.

    [Fact]
    public void A_fresh_run_has_not_created_anything()
    {
        Assert.False(Run().CreatedTheSubscriber);
    }

    [Fact]
    public void A_run_that_created_nothing_refuses_to_delete()
    {
        var error = Assert.Throws<IntegrationException>(() => Run().RequireCreatedByThisRun());

        Assert.Equal(CanonicalErrorCode.ValidationError, error.Code);
        Assert.Contains("did not create the subscriber", error.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_that_created_the_subscriber_may_delete_it()
    {
        var run = Run();
        run.RecordSubscriberCreated();

        Assert.True(run.CreatedTheSubscriber);
        Assert.Null(Record.Exception(run.RequireCreatedByThisRun));
    }

    /// <summary>
    /// Permission to delete belongs to one run and does not leak to another. Two runs against the same
    /// number are still two different runs, and only the one that provisioned it may remove it.
    /// </summary>
    [Fact]
    public void Permission_earned_by_one_run_does_not_carry_to_another()
    {
        var first = Run();
        first.RecordSubscriberCreated();

        var second = Run();

        Assert.True(first.CreatedTheSubscriber);
        Assert.False(second.CreatedTheSubscriber);
        Assert.Throws<IntegrationException>(() => second.RequireCreatedByThisRun());
    }

    /// <summary>
    /// <c>--allow-mutation</c> permits changing a subscriber. It does not permit deleting one this run did
    /// not create — the two are separate questions, and answering the first yes must not answer the second.
    /// </summary>
    [Fact]
    public void Allowing_mutation_does_not_by_itself_permit_deletion()
    {
        var run = new VerificationRun(
            hlr: null!,
            VerificationTarget.Create(
                "SS", "252629001441", null, null, null, allowMutation: true, keepSubscriber: false));

        // Mutation is allowed…
        Assert.Null(Record.Exception(() => run.Target.RequireMutationAllowed("Barring verification")));

        // …and deletion is still refused.
        Assert.Throws<IntegrationException>(() => run.RequireCreatedByThisRun());
    }

    /// <summary>
    /// The permission is one-way. Nothing revokes it mid-run, and nothing outside registration grants it,
    /// so a run cannot talk itself into a deletion it has not earned.
    /// </summary>
    [Fact]
    public void The_permission_is_granted_once_and_never_withdrawn()
    {
        var run = Run();

        run.RecordSubscriberCreated();
        run.RecordSubscriberCreated();

        Assert.True(run.CreatedTheSubscriber);
    }

    /// <summary>
    /// Nothing but registration may grant the right to delete. If a future change adds another way to set
    /// it, this test is what should make that a deliberate decision rather than an accident.
    /// </summary>
    [Fact]
    public void Only_one_member_can_grant_permission_to_delete()
    {
        // "Record…" grants; "Require…" only checks. The distinction matters: a second granting member is
        // a second way to earn a deletion, while another checker is harmless.
        var granting = typeof(VerificationRun)
            .GetMethods()
            .Where(m => m.Name.StartsWith("Record", StringComparison.Ordinal)
                     && m.ReturnType == typeof(void)
                     && m.GetParameters().Length == 0)
            .Select(m => m.Name)
            .ToList();

        Assert.Single(granting);
        Assert.Equal(nameof(VerificationRun.RecordSubscriberCreated), granting[0]);

        // And it cannot be assigned from outside. A private setter is expected and correct — what must
        // not exist is a public one, which would let any caller declare it had created a subscriber.
        var setter = typeof(VerificationRun)
            .GetProperty(nameof(VerificationRun.CreatedTheSubscriber))!
            .SetMethod;

        Assert.False(setter?.IsPublic ?? false, "CreatedTheSubscriber must not be publicly settable.");
    }
}
