# Direct Integration Verification

Verifies the Somtel integration platform by **calling its real functions against the real provider**.

```
this project  →  Somtel.Integration.Hlr.dll  →  real public function  →  real Huawei HSS
```

The same relationship `AllInOne` has with `DahabEndpoints`: a separate application that references the
integration project and calls it. There is **no XML, no transport, no parser and no market logic in here**
— anything needed to talk to the HSS lives in the integration project and is called from here. A
verification project that reimplements what it verifies proves only that two pieces of code written by
the same person agree with each other.

Standard: [`DIV-STD-001`](../telecom-integration-platform/docs/standards/DIV-STD-001.md).

---

## 1. Before you start — read this

**The provider is production.** There is no test HLR for any market. Anything you run here happens to a
real subscriber on the real network.

Three rules follow from that, and they are enforced by the tool, not just by this document:

1. **Read groups change nothing** and are safe at any time.
2. **Everything else needs `--allow-mutation`.** Without it the run refuses to start.
3. **You always type the subscriber.** There is no option to find one — not disabled, *absent*. An
   automated picker could choose a real customer, so a human names every identifier that gets touched.

Every state-changing check follows **apply → read back → restore**, and the restore runs even when the
read-back disagrees. A restore that fails is reported as a failure, never hidden.

---

## 2. One-time setup

### 2.1 Prerequisites

- .NET 10 SDK
- The platform repository checked out **beside** this one:

```
SomtelProjects/
├── telecom-integration-platform/     ← the product
└── Somtel.Integration.Verification/  ← you are here
```

The project reference is relative, so the folder names matter.

### 2.2 Credentials

Credentials never live in a file in either repository. They go in the machine's secret store, which this
project shares with the platform host — provision them once and both can use them:

```bash
dotnet user-secrets --id somtel-tip-host set "Hlr:Markets:SS:Credentials:Username" "<username>"
```

```bash
dotnet user-secrets --id somtel-tip-host set "Hlr:Markets:SS:Credentials:Password" "<password>"
```

Environment variables work too, if you prefer them for a one-off run:

```powershell
$env:Hlr__Markets__SS__Credentials__Username = Read-Host 'SS HLR operator username'
$env:Hlr__Markets__SS__Credentials__Password = Read-Host -MaskInput 'SS HLR operator password'
```

Clear them when you are done:

```powershell
Remove-Item Env:Hlr__Markets__SS__Credentials__Username, Env:Hlr__Markets__SS__Credentials__Password
```

Market endpoints and profiles come from the platform's own `config/markets/*/hlr.json`, copied next to
the binary at build time. You do not configure anything here.

### 2.3 Build

```bash
dotnet build Somtel.Integration.Verification.slnx
```

---

## 3. Running it

```bash
dotnet run --project src/Somtel.Integration.Hlr.Verification -- <group> --market SS --msisdn <number> [options]
```

### 3.1 The groups

| Group | What it verifies | Changes state? |
| --- | --- | --- |
| `read` | Get Subscriber Info · Get Lock State · Get Multi-IMSI · Get Authentication Profile | **No** |
| `lifecycle` | Register Subscriber · Change IMSI (SIM swap) · Remove Subscriber | Yes |
| `barring` | Bar/Unbar calls · Bar/Unbar roaming | Yes |
| `services` | Activate/Deactivate data · six supplementary services · call forwarding | Yes |
| `provisioning` | Data / EPS / 5G profiles · multi-SIM | Yes |
| `all` | every group, ordered to create the subscriber first and clean up last | Yes |

### 3.2 Options

| Option | When you need it |
| --- | --- |
| `--market SS\|SL\|PL` | always |
| `--msisdn <number>` | always — the subscriber you are acting on |
| `--imsi <imsi>` | reading authentication data, and registering a subscriber |
| `--secondary-imsi <imsi>` | SIM swap and multi-SIM — must be a **different** SIM from `--imsi` |
| `--forward-to <number>` | call forwarding — must **not** be the subscriber's own number |
| `--allow-mutation` | every group except `read` |
| `--keep` | leave a subscriber this run created instead of deleting it |

**On IMSIs:** an IMSI is **15 digits**. The 19–20 digit number printed on the SIM is the **ICCID** — a
different identifier for the same card, and one cannot be derived from the other. If you pass an ICCID the
tool tells you so rather than just complaining about length.

### 3.3 Everyday examples

Check a live subscriber, changing nothing:

```bash
dotnet run --project src/Somtel.Integration.Hlr.Verification -- read --market SS --msisdn 252628863830
```

Verify barring works, on a subscriber you may modify:

```bash
dotnet run --project src/Somtel.Integration.Hlr.Verification -- barring --market SS --msisdn 252629001441 --allow-mutation
```

Verify service activation, including call forwarding:

```bash
dotnet run --project src/Somtel.Integration.Hlr.Verification -- services --market SS --msisdn 252629001441 --forward-to 252628863830 --allow-mutation
```

Full sweep on a disposable number, creating and deleting it:

```bash
dotnet run --project src/Somtel.Integration.Hlr.Verification -- all --market SS --msisdn 252629001441 --imsi 637820015314178 --secondary-imsi <second spare> --forward-to 252628863830 --allow-mutation
```

Same, but leave the subscriber behind for later use:

```bash
dotnet run --project src/Somtel.Integration.Hlr.Verification -- all --market SS --msisdn 252629001441 --imsi 637820015314178 --forward-to 252628863830 --allow-mutation --keep
```

---

## 4. Reading the output

```
  ✓ setLockState (bar)                         [135 ms]
     applied incoming=True outgoing=True
  ✓ setLockState (read back)                   [129 ms]
     confirmed — the subscriber now reads as blocked
  ✓ setLockState (restore)                     [136 ms]
     restored to incoming=False outgoing=False
```

| Mark | Meaning |
| --- | --- |
| `✓` | the real function was called and returned what was expected |
| `✗` | it failed — the detail line carries the canonical error code and the provider's code |
| `–` | skipped, with the reason (usually a missing input like `--secondary-imsi`) |

**`applied` is not the same as `confirmed`.** "Applied" means the provider accepted the request.
"Confirmed" means a follow-up read proved the change took effect. Where an operation has no verified
read-back command, the line says so rather than implying more than was proven.

Every run ends with rows ready to paste into
`telecom-integration-platform/docs/verification/hlr.md`, with identifiers masked:

```
| operation         | market | target       | result   |
| getSubscriberInfo | SS     | 25262*****30 | VERIFIED |
```

An operation is `VERIFIED` for a market **only** when it has been run against that market's real provider
and recorded. A green test suite in the platform repository does not make anything verified.

---

## 5. Provider result codes seen so far

Recorded from observation, not from vendor documentation. Only `0` and `3001` are established; the rest
are notes for whoever gets the real code list.

| Code | Seen on | What was observed |
| --- | --- | --- |
| `0` | everything that succeeded | success |
| `1010` | `MOD_EPS` with `PROV=FALSE` | EPS could be turned on but not off |
| `1004` | `MOD_MIMSI` `DEL` | removing an IMSI that was never added |
| `3001` | most commands | **subscriber not found** — established by controlled comparison |
| `3006` | `MOD_OPTGPRS` | on a subscriber that already has a PDP context |
| `3007` | `MOD_NAM`, `MOD_ODBROAM` | these answer `3007` where others answer `3001` for an absent subscriber |
| `3015` | `REG_CFB`, `REG_CFNRC`, `REG_CFU` | when the matching forwarding service is not provisioned |
| `3020` | `MOD_HOLD` with `PROV=FALSE` | CallHold can be provided but not withdrawn |
| `3033` | `REG_CFU` | forwarding a subscriber to **its own number** |
| `3048` | `CHK_KI` | IMSI not present in the AuC |
| `3111` | `MOD_MIMSI` `ADD` | attaching a subscriber's own IMSI as its secondary |

---

## 6. Known limits on SS — expected failures, not bugs

These fail every run and the cause is understood. Do not chase them.

| What fails | Why |
| --- | --- |
| `setSupplementaryService[CallHold] (withdraw)` | The HLR refuses `MOD_HOLD PROV=FALSE` (`3020`). CallHold is effectively one-way. Do not offer customers a "turn call hold off" option. |
| `registerCallForwarding[Busy]` and `[NotReachable]` | CFB/CFNRC must be provisioned on the subscriber. The SS creation template does not provision them and **no discovered command can** — missed-call-alert flows need an HLR template change. |
| `applyFiveGProfile` | SS has no configured 5G profile. The platform refuses rather than borrowing SL/PL values. This refusal is correct behaviour. |
| `applyDataProfile` on an existing subscriber | `3006` — the template already gave it a PDP context. |
| `applyEpsProfile (off)` | `1010` — EPS appears to be one-way like CallHold. Not yet fully established. |

---

## 7. Troubleshooting

**`MARKET_OPERATION_NOT_VERIFIED`** — the market has no endpoint or credentials configured, or the
operation has no configured values for it. For SL/PL this is expected today: their credentials are still
pending.

**`AUTHENTICATION_FAILURE`** — the operator credentials are wrong or missing. Re-check §2.2.

**`VALIDATION_ERROR: ... looks like an ICCID`** — you passed the number printed on the SIM. Read the
15-digit IMSI from the SIM record instead.

**"--msisdn is required"** — by design. The tool will not pick a subscriber for you.

**A restore failed** — the subscriber is left in the changed state. The output says which operation and
what state it is in. Fix it by hand before re-running.

---

## 8. Adding a capability

When OCS, SMS or eDahab are built, each gets a sibling project with the same four-file shape:

```
src/Somtel.Integration.<Capability>.Verification/
├── Program.cs              CLI, composes the platform's own DI, routes to a group
├── VerificationTarget.cs   who is acted on and what may be done to them
├── VerificationRun.cs      calls, times, prints, reports — no integration logic
└── Operations/             one file per operation group
```

The rule that matters: **reference the production project and call its public functions.** If you find
yourself writing a request builder, a parser, or anything that looks like integration logic, it belongs in
the integration project instead.
