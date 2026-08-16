using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Somtel.Integration.BuildingBlocks.Correlation;
using Somtel.Integration.BuildingBlocks.Errors;
using Somtel.Integration.BuildingBlocks.Markets;
using Somtel.Integration.BuildingBlocks.Msisdns;
using Somtel.Integration.Hlr;
using Somtel.Integration.Hlr.Verification;
using Somtel.Integration.Hlr.Verification.Operations;

// =====================================================================================================
// Direct Integration Verification — HLR (DIV-STD-001)
//
//     this project  ->  Somtel.Integration.Hlr.dll  ->  real public function  ->  real Huawei HSS
//
// The same relationship AllInOne has with DahabEndpoints: a separate application that references the
// integration project and calls it. Nothing about the integration is reimplemented here — no XML, no
// transport, no parsing, no market rules. If a call fails, the failure is in production code, which is
// the only reason this is worth running.
//
// The provider is production. There is no UAT HLR for any market, so:
//   · read groups are safe at any time and need only credentials;
//   · state-changing groups need --allow-mutation and a subscriber you are permitted to modify;
//   · the target is always typed by a person — this program cannot look one up.
// =====================================================================================================

var parsed = CommandLine.Parse(args);
if (parsed is null)
{
    CommandLine.PrintUsage();
    return 2;
}

var correlationId = Guid.NewGuid().ToString();
Console.WriteLine($"HLR Direct Integration Verification — group={parsed.Group} market={parsed.Market}");
Console.WriteLine();

try
{
    var target = VerificationTarget.Create(
        parsed.Market, parsed.Msisdn, parsed.Imsi, parsed.SecondaryImsi, parsed.AllowMutation, parsed.Keep);

    // Composed with the platform's own DI extension, so this runs against the service wired exactly as
    // the host wires it. Duplicating the wiring would be duplicating production.
    var services = new ServiceCollection();
    services.AddLogging(logging => logging
        .AddSimpleConsole(console => console.IncludeScopes = true)
        .SetMinimumLevel(LogLevel.Warning)
        .AddFilter("Somtel.Integration", LogLevel.Information));

    var configuration = BuildConfiguration();
    var routing = new MarketRoutingOptions();
    configuration.GetSection("MarketRouting").Bind(routing);

    services.AddSingleton(routing);
    services.AddSingleton<MsisdnNormalizer>();
    services.AddSingleton<IMarketResolver, ConfiguredMarketResolver>();
    services.AddHlrCapability(configuration);

    await using var provider = services.BuildServiceProvider();
    var hlr = provider.GetRequiredService<HlrService>();

    var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Somtel.Integration.Hlr.Verification");
    using var scope = logger.BeginScope(new Dictionary<string, object> { [CorrelationDefaults.LogScopeKey] = correlationId });

    var run = new VerificationRun(hlr, target);

    if (target.AllowMutation)
    {
        Console.WriteLine($"  STATE-CHANGING run against {target.Msisdn} in {target.Market}.");
        Console.WriteLine("  Every check restores what it found; a restore that fails is reported as a failure.");
        Console.WriteLine();
    }

    switch (parsed.Group)
    {
        case "read":
            await ReadSubscriberOperations.RunAsync(run, CancellationToken.None);
            break;
        case "lifecycle":
            await SubscriberLifecycleOperations.RunAsync(run, CancellationToken.None);
            break;
        case "barring":
            await BarringOperations.RunAsync(run, CancellationToken.None);
            break;
        case "services":
            await ServiceActivationOperations.RunAsync(run, CancellationToken.None);
            break;
        case "provisioning":
            await ProvisioningOperations.RunAsync(run, CancellationToken.None);
            break;
        case "all":
            // Ordered so the subscriber exists before anything is done to it, and is cleaned up last.
            await SubscriberLifecycleOperations.RegisterAsync(run, CancellationToken.None);
            await ReadSubscriberOperations.RunAsync(run, CancellationToken.None);
            await BarringOperations.RunAsync(run, CancellationToken.None);
            await ServiceActivationOperations.RunAsync(run, CancellationToken.None);
            await ProvisioningOperations.RunAsync(run, CancellationToken.None);
            if (target.KeepSubscriber)
            {
                run.Skip("removeSubscriber", "NOT DELETED by request (--keep) — remove it by hand");
            }
            else
            {
                await SubscriberLifecycleOperations.RemoveAsync(run, CancellationToken.None);
            }

            break;
        default:
            CommandLine.PrintUsage();
            return 2;
    }

    return run.Report(correlationId);
}
catch (IntegrationException exception)
{
    Console.WriteLine($"  ✗ {CanonicalErrorCodes.ToWireCode(exception.Code)}: {exception.Detail}");
    return 1;
}

static IConfiguration BuildConfiguration()
{
    // The market profiles are the platform's own, copied next to the binary at build time. Credentials
    // come only from the machine's secret store or the environment — never from a committed file.
    var configRoot = Path.Combine(AppContext.BaseDirectory, "config");
    var builder = new ConfigurationBuilder();

    foreach (var market in Enum.GetValues<Market>())
    {
        builder.AddJsonFile(
            Path.Combine(configRoot, "markets", market.ToString().ToLowerInvariant(), "hlr.json"), optional: true);
    }

    return builder
        .AddUserSecrets(typeof(VerificationRun).Assembly, optional: true)
        .AddEnvironmentVariables()
        .Build();
}

/// <summary>
/// Argument parsing, kept deliberately dull. Note what is absent: there is no option that finds a
/// subscriber. A target can only be typed in.
/// </summary>
internal sealed record CommandLine(
    string Group,
    string? Market,
    string? Msisdn,
    string? Imsi,
    string? SecondaryImsi,
    bool AllowMutation,
    bool Keep)
{
    private static readonly string[] Groups = ["read", "lifecycle", "barring", "services", "provisioning", "all"];

    public static CommandLine? Parse(string[] args)
    {
        if (args.Length == 0 || !Groups.Contains(args[0], StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        string? market = null, msisdn = null, imsi = null, secondaryImsi = null;
        var allowMutation = false;
        var keep = false;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--market" when i + 1 < args.Length: market = args[++i]; break;
                case "--msisdn" when i + 1 < args.Length: msisdn = args[++i]; break;
                case "--imsi" when i + 1 < args.Length: imsi = args[++i]; break;
                case "--secondary-imsi" when i + 1 < args.Length: secondaryImsi = args[++i]; break;
                case "--allow-mutation": allowMutation = true; break;
                case "--keep": keep = true; break;
                default: return null;
            }
        }

        return new CommandLine(args[0].ToLowerInvariant(), market, msisdn, imsi, secondaryImsi, allowMutation, keep);
    }

    public static void PrintUsage()
    {
        Console.WriteLine("HLR Direct Integration Verification — calls the real integration DLL against the real HSS.");
        Console.WriteLine();
        Console.WriteLine("  verify <group> --market SS|SL|PL --msisdn <number> [options]");
        Console.WriteLine();
        Console.WriteLine("Groups:");
        Console.WriteLine("  read           getSubscriberInfo, getLockState, getMultiImsi, getAuthenticationProfile");
        Console.WriteLine("                 READ-ONLY — safe against production at any time.");
        Console.WriteLine("  lifecycle      registerSubscriber, changeImsi, removeSubscriber");
        Console.WriteLine("  barring        setLockState, setRoamingBarred (bar → read back → restore)");
        Console.WriteLine("  services       setNetworkAccessMode, supplementary services, call forwarding");
        Console.WriteLine("  provisioning   data / EPS / 5G profiles, multi-IMSI");
        Console.WriteLine("  all            everything, in an order that creates and cleans up the subscriber");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --imsi <imsi>            a spare SIM (15 digits — not the 19-20 digit ICCID on the card)");
        Console.WriteLine("  --secondary-imsi <imsi>  a second spare SIM, for SIM swap and multi-IMSI");
        Console.WriteLine("  --allow-mutation         required by every group except 'read'");
        Console.WriteLine("  --keep                   leave a created subscriber in place instead of deleting it");
        Console.WriteLine();
        Console.WriteLine("The target is always supplied here. There is no option to find a subscriber, by design.");
    }
}
