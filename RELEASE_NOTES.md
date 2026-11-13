# VVG.Modbus 1.1.4 and client test release

13 November 2026. Requires Windows with .NET Framework 4.8; uses log4net 3.3.1.
The GitHub tag is `V` followed by the released client executable's assembly version,
following previous releases. The release manifest records that version, the NuGet
version and the exact source commit.

## Client test application

- Expandable Activity log consuming existing log4net events alongside other
  appenders, with a bounded buffer, Clear and Save controls.
- Optional current-tab auto-refresh with configurable intervals. File Records is
  excluded; failed polls are logged without dialogue boxes. Manual commands stay
  disabled throughout automatic polling, including the delay between polls.
- Configurable command timeout for serial and RTU-over-TCP, locked while busy.
- File reads and writes share adjacent-file rollover at 128 KiB boundaries and
  offer continue/abort after retries exhaust, preserving the failed chunk position.

## VVG.Modbus library

- Correct FC15 coil bit order, payload byte count and padding.
- Validate response counts, file subresponse fields and CRC positions before
  indexing; reject empty reads and invalid/null write arrays.
- Bound file-read rounding and return exactly the requested bytes.
- Add opt-in `AllowExtendedFileRecordAddresses` for vendor-specific 128 KiB files.
  The test app enables it; library consumers retain standard-range writes by default.

## Compatibility and validation

Remove any caller/slave compensation for the former reversed FC15 coil encoding.
Odd file reads now return exactly the requested byte count, with no padding byte;
file reads above 242 bytes must be split into word-aligned transactions.

The 128 KiB file layout is a vendor extension, not a change to standard Modbus
file-record limits. Validate it against the target device before using adjacent
file IDs. The library's default extended-address setting remains off.

All 76 protocol checks and 18 UI/transfer checks pass. Release compilation succeeds
without warnings, and the window layout was rendered and inspected. Physical
serial/network timing and device-specific file behaviour still require hardware
validation. Existing TCP partial-read and serial receive-buffer concurrency issues
are recorded in tests/PARITY_REVIEW.md.

The ZIP is the complete client distribution. Loose executable/DLL/config assets
are also supplied, as in previous releases. The `.nupkg` contains the same library
binary as the ZIP; SHA256SUMS.txt and release-manifest.json identify the artifacts.
