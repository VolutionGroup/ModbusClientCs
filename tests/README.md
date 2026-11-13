# RTU regression checks

The WPF monitoring/file-transfer checks are separate:

```text
python tests/run_ui_tests.py
```

They build the actual test application and run 18 checks without hardware. See
[the test-harness guide](../ModbusClientTest/README.md) for the controls, transfer
behaviour and optional rendered-preview output.

Run from the repository root on Windows:

```text
python tests/run_tests.py
```

Requires Python 3.9+, Visual Studio's Roslyn C# compiler, .NET Framework 4.8 and
the project's restored `log4net` package. `CSC` can select another compatible
compiler executable. No additional test framework or NuGet package is required.
The existing `ModbusClientTest` project is an interactive application; this suite
is a separate automated console executable.

The runner compiles every actual top-level `VVG.Modbus/*.cs` production source
with C# 7.3 and warning level 4, references the real log4net assembly, and replaces
only transport I/O with a subclass of `ClientRTU_Base`. Temporary binaries and
dependency copies are removed after the run. All named cases run even if a
previous one fails. Exit 0 means all checks pass; 1 means failed checks; 2 means
runner infrastructure failed. Builds and execution have bounded timeouts.

`--base-source PATH` can substitute an earlier `ClientRTU_Base.cs` for comparison.
For this change, the original working-tree source passed 56 of the same 76 cases;
the updated source passes all 76. That baseline already included the user's coil
and file-write fixes, which this change preserves.

Coverage includes normal responses for all ten functions, all eight zero-length
operations, send/receive failures and semaphore recovery, CRC/header/truncation
checks, every response byte-count value at the supported minimum and maximum
sizes, file nested lengths/reference types, odd file-read lengths, size/overflow
guards, null write arrays, the FC15 specification example and all 1968 supported
coil-write lengths, padding, CRCs and existing broadcast/address write policies.

This suite checks the protocol layer and its transport contract. It does not
measure physical serial-port/network timeout accuracy, serial timing, RS-485
turnaround, event-buffer concurrency or partial TCP stream reads. See
[PARITY_REVIEW.md](PARITY_REVIEW.md) for differences from the C++ implementation
and follow-up items.
