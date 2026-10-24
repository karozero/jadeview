# Private Iris callback command-capture harness

`activateur_capture.c` contains the four scenarios, assertions, and JSON-lines reporting. `capture_host.h` defines the synthetic context and intent types; `capture_host.c` owns fixture setup and the closed engine API adapters. The shared context makes the host boundary explicit without copying callback implementations from Iris.

Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/NativeScriptBridge/build-and-test.ps1` from the repository root. The script uses the local Visual Studio 2022 x86 toolchain, compiles the unmodified Iris `Activateur_avec_SF` callback source, links the synthetic host adapters, and runs four assertion-backed fixtures. Binary and JSON-lines output go under ignored `artifacts/native-script-bridge/`.

This is a callback-body proof only. The host provides one controlled callback-key resolver, synthetic AI vars/owner/actor/time/saved flags, and adapters for the seven imports exercised by this callback pair. `FlagsControlSet` and `FlagInactiveSet` are emitted as intents; they are not applied to a world. The harness does not load BF/OFC/OVA resources, schedule an owner, construct EOT tables, tick EVE/ANI/AI, or launch BGE. Do not treat the output as an executed game-world trace.

The build intentionally links the original callback source directly and does not copy its implementation. A newly referenced native function without a matching test adapter causes link failure. This narrow harness is private engineering evidence; resolve source licensing before distribution.

The shared `native_layout_contract.h` rejects incompatible host layouts at compile time (32-bit pointers, measured AI node/function/variable layouts and five instance callback slots). Both the x86 positive build and an intentionally rejected x64 compile were verified. These checks constrain the helper ABI; they do not prove retail resource compatibility.

## Resource-reference queue probe

Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/NativeScriptBridge/test-resource-loader-probe.ps1` to check two real script-owner fixtures. The probe extracts byte-exact Iris AIC/AIM/OVA loader callback bodies, verifies all selected fixture resource sizes and SHA-256 hashes, then executes `AI_ul_CallbackLoadInstance`, `AI_ul_CallbackLoadModel` and `AI_ul_CallbackLoadVars` in a bounded x86 host. It asserts the queued OVA/OFC keys, destination pointer fields, callback addresses, and LOA flags. OVA descriptors, names and raw storage bytes are compared with independent C# summaries. Negative cases check hash mismatch, truncated AIC/OVA and unknown OVA type rejection.

The host explicitly calls the OVA reader on the selected manifest payloads; it does not drain the LOA queue, resolve OFC functions, initialize live instance variables, activate an AI/world owner, build EOT tables, or run a game tick. Queued OFC callback destinations remain unresolved. Generated source and run logs remain under ignored `artifacts/native-script-bridge/resource-loader-probe/`; this is private feasibility evidence, not a distributable runtime.

## Compiled-function metadata probe

Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/NativeScriptBridge/test-ofc-registry-probe.ps1` for the separate OFC stage. It validates the manifest-selected resource, runs the original compact function loader, and checks its callback pointer against one source-selected compiled registry entry. Both real owner fixtures pass; unknown registry keys and invalid resource hashes are rejected before loading.

The compiled callback is linked but never invoked. The repeated-load check uses a controlled one-entry address cache, not the engine cache. This stage does not resolve the earlier probe's queue, initialize instance variables, or apply script effects to JadeView. See [runtime bridge findings](../../docs/game-script-runtime-bridge-feasibility.md) for source locations, observed metadata, and the remaining initialization boundary.
