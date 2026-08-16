using Somtel.Integration.BuildingBlocks.Errors;
using Somtel.Integration.BuildingBlocks.Observability;

namespace Somtel.Integration.Hlr.Verification;

/// <summary>
/// What every operation verification is given, and how it reports.
///
/// This class contains no integration logic of any kind — no XML, no transport, no parsing, no market
/// rules. It calls, it times, it prints. Everything it verifies happens inside the production DLL.
/// </summary>
public sealed class VerificationRun
{
    private readonly List<Step> _steps = [];

    public VerificationRun(HlrService hlr, VerificationTarget target)
    {
        Hlr = hlr;
        Target = target;
    }

    /// <summary>The real integration service — the same instance type the platform host resolves.</summary>
    public HlrService Hlr { get; }

    public VerificationTarget Target { get; }

    public bool AnyFailed => _steps.Exists(step => step.Outcome == Outcome.Failed);

    /// <summary>
    /// Calls one real integration function and reports what came back. <paramref name="describe"/> turns
    /// the real result record into the line an operator reads — so what is displayed is the actual
    /// response, not a restatement of the request.
    /// </summary>
    public async Task<T?> CallAsync<T>(string operation, Func<Task<T>> call, Func<T, string> describe)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await call();
            stopwatch.Stop();
            Record(operation, Outcome.Passed, describe(result), stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (IntegrationException exception)
        {
            stopwatch.Stop();
            Record(
                operation,
                Outcome.Failed,
                $"{CanonicalErrorCodes.ToWireCode(exception.Code)}: {exception.Detail}" +
                (exception.ProviderCode is { } code ? $" (providerCode={code})" : ""),
                stopwatch.ElapsedMilliseconds);
            return default;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            Record(operation, Outcome.Failed, exception.Message, stopwatch.ElapsedMilliseconds);
            return default;
        }
    }

    public void Skip(string operation, string why) => Record(operation, Outcome.Skipped, why, null);

    public void Note(string text) => Console.WriteLine($"     {text}");

    private void Record(string operation, Outcome outcome, string detail, long? durationMs)
    {
        _steps.Add(new Step(operation, outcome, detail));

        var mark = outcome switch { Outcome.Passed => "✓", Outcome.Failed => "✗", _ => "–" };
        var timing = durationMs is { } ms ? $" [{ms} ms]" : "";
        Console.WriteLine($"  {mark} {operation,-42}{timing}");
        Console.WriteLine($"     {detail}");
    }

    /// <summary>Prints the summary and the markdown row set for docs/verification/hlr.md.</summary>
    public int Report(string correlationId)
    {
        var passed = _steps.Count(s => s.Outcome == Outcome.Passed);
        var failed = _steps.Count(s => s.Outcome == Outcome.Failed);
        var skipped = _steps.Count(s => s.Outcome == Outcome.Skipped);

        Console.WriteLine();
        Console.WriteLine($"  {passed} passed · {failed} failed · {skipped} skipped");

        if (failed > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Failed — record each of these before changing anything:");
            foreach (var step in _steps.Where(s => s.Outcome == Outcome.Failed))
            {
                Console.WriteLine($"    {step.Operation,-42} {step.Detail}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("  Rows for docs/verification/hlr.md (identifiers masked):");
        Console.WriteLine("  | operation | market | target | result |");
        Console.WriteLine("  | --- | --- | --- | --- |");
        foreach (var step in _steps.Where(s => s.Outcome != Outcome.Skipped))
        {
            var verdict = step.Outcome == Outcome.Passed ? "VERIFIED" : "FAILED";
            Console.WriteLine($"  | {step.Operation} | {Target.Market} | {Sensitive.MaskMsisdn(Target.Msisdn)} | {verdict} |");
        }

        Console.WriteLine();
        Console.WriteLine($"Verification {(failed == 0 ? "PASSED" : "FAILED")}. correlationId={correlationId}");
        return failed == 0 ? 0 : 1;
    }

    private enum Outcome
    {
        Passed,
        Failed,
        Skipped,
    }

    private sealed record Step(string Operation, Outcome Outcome, string Detail);
}
