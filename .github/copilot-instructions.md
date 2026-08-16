# Repository instructions for AI assistants

This is the **Direct Integration Verification** project for the Somtel integration platform. It is a
consumer of that platform, exactly as `AllInOne` is a consumer of `DahabEndpoints`.

```
this project  →  Somtel.Integration.Hlr.dll  →  real public function  →  real Huawei HSS
```

Read `README.md` before changing anything, and `../telecom-integration-platform/docs/standards/DIV-STD-001.md`
for the standard this implements.

## Ground rules

- **Never add integration logic here.** No XML, no SOAP, no transport, no parsers, no market rules, no
  business logic. If something is needed to talk to the HSS, it belongs in the integration project and is
  called from here. A verification project that reimplements what it verifies proves nothing.
- **Never add a fake, mock, stub or simulated provider.** The entire point is that the real code runs
  against the real provider. There is no test HLR for any market.
- **The provider is production.** Every run touches a real subscriber on the real network.
- **Never let the tool select a subscriber.** No lookup, no search, no "find a test number" — a human types
  every identifier that gets touched, so a human is accountable for it. If you are adding a method that
  finds subscribers, stop.
- **State-changing work requires `--allow-mutation`**, and every such check must follow
  apply → read back → restore, with the restore running even when the read-back disagrees.
- **Never delete a subscriber this run did not create.** Registration returns whether it created one;
  every caller must honour that result. This has been got wrong once already.
- **No credentials in any tracked file.** They come from the shared user-secret store (`somtel-tip-host`)
  or environment variables.

## Layout

- `src/Somtel.Integration.Hlr.Verification/` — the console tool. `Operations/` holds one file per group.
- `tests/` — tests for **this tool's own safety guards only**, never for the platform.

The platform repository must be checked out beside this one: the project reference is relative.
