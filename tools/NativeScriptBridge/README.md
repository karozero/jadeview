# Private Iris callback command-capture harness

`activateur_capture.c` contains the four scenarios, assertions, and JSON-lines reporting. `capture_host.h` defines the synthetic context and intent types; `capture_host.c` owns fixture setup and the closed engine API adapters. The shared context makes the host boundary explicit without copying callback implementations from Iris.

Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/NativeScriptBridge/build-and-test.ps1` from the repository root. The script uses the local Visual Studio 2022 x86 toolchain, compiles the unmodified Iris `Activateur_avec_SF` callback source, links the synthetic host adapters, and runs four assertion-backed fixtures. Binary and JSON-lines output go under ignored `artifacts/native-script-bridge/`.

This is a callback-body proof only. The host provides one controlled callback-key resolver, synthetic AI vars/owner/actor/time/saved flags, and adapters for the seven imports exercised by this callback pair. `FlagsControlSet` and `FlagInactiveSet` are emitted as intents; they are not applied to a world. The harness does not load BF/OFC/OVA resources, schedule an owner, construct EOT tables, tick EVE/ANI/AI, or launch BGE. Do not treat the output as an executed game-world trace.

The build intentionally links the original callback source directly and does not copy its implementation. A newly referenced native function without a matching test adapter causes link failure. This narrow harness is private engineering evidence; resolve source licensing before distribution.
