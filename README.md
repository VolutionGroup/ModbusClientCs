# VVG.Modbus
A simple MODBUS-RTU client in c# - mainly written because other available alternatives didn't include read/write file record support.
Option to create MODBUS-TCP or MODBUS-ASCII (etc) at a later date using interface.

Supported functions:
* Read Coils (1)
* Read Discrete Input (2)
* Read Holding Registers (3)
* Read Input Registers (4)
* Write Coil (5)
* Write Holding Register (6)
* Write Coils (15)
* Write Holding Registers (16)
* Read File Records (20) - single block only
* Write File Records (21) - single block only

No enforced minimum idle time between last response and next command

Based on SharedCode C++ (internal) https://github.com/VolutionGroup/ModbusMaster

## Releases

See [GitHub releases](https://github.com/VolutionGroup/ModbusClientCs/releases)
for the client ZIP, loose binaries and VVG.Modbus NuGet package. Release tags follow
the client assembly version (`V<version>`), as in earlier releases.
See [release notes](RELEASE_NOTES.md) and [test-harness usage](ModbusClientTest/README.md).

To prepare matching release assets from a clean committed checkout on Windows:

```text
python tests/run_tests.py
python tests/run_ui_tests.py
python scripts/build_release.py --nuget C:\path\to\nuget.exe
```

Requires Visual Studio MSBuild with .NET Framework 4.8/WPF targeting tools and the
restored log4net package. Use Microsoft's signed NuGet CLI. The build creates the
NuGet package and complete client ZIP from the same library binary, with a source
commit/version manifest and SHA-256 checksums under `artifacts/release`.
Publishing the source commit, its version tag and release assets is a separate step.
