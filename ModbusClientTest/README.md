# Client test harness: monitoring and transfers

The window now provides an optional **Activity log**, **Auto-refresh**, an interval
in milliseconds, and a command timeout in milliseconds.

## Monitoring

- Expand **Activity log** at the bottom to grow the window and show existing
  log4net messages from both the test app and client library. Existing appenders
  continue to receive messages. The view retains the latest 1000 events, bounds
  individual entries, and has **Clear** and **Save…** controls. It collects while
  collapsed; saving is explicit, with no automatic disk log added.
- Enable **Auto-refresh** to read the selected Coils, Discrete Inputs, Holding
  Registers or Input Registers tab. The default interval is 1000 ms, configurable
  from 100 through 3600000 ms. The next interval starts after the previous command
  completes, so slow requests never accumulate overlapping polls.
- Manual read/write and grid-update buttons remain disabled while auto-refresh
  is enabled on a polling tab, including the delay between requests. Coil/register
  editing is also disabled during polling. Turning it off restores manual commands.
- File Records is excluded from polling. Selecting it pauses auto-refresh and
  enables manual file operations when no command is in progress. Returning to a
  read tab resumes monitoring. Disconnecting pauses polling; reconnecting resumes
  it if the checkbox remains selected.
- Failed automatic polls, including invalid inputs, go to log4net without a modal
  dialogue. The next scheduled poll can recover. Manual command failures retain
  their normal dialogues. In-flight results are discarded if the tab, parameters
  or connection changed, avoiding stale data updates.
- **Timeout (ms)** defaults to 500 and accepts 1 through 600000 ms. It applies to
  both serial and RTU-over-TCP clients before an operation starts. The field is
  locked during commands and file transfers. Invalid timeout/interval values pause
  polling; invalid timeouts also prevent new manual commands.

## File reads and writes

Both directions use 128-byte commands and the same transfer planner. The planner
accounts for the starting record and splits at exactly **128 KiB (131072 bytes)**
per file ID, continuing at record zero of the next adjacent file. A dialogue asks
permission before a transfer crosses file IDs, and transfers that would wrap past
file ID 65535 are rejected before transmission.

Read retries exhaust after five failed attempts at a chunk; writes use ten.
Both then offer **Attempt to continue from this position?**. Continuing resets the
retry count and retries the same chunk without discarding completed progress.
Declining stops the transfer. Failed attempts are logged, with a responsive
250 ms delay between retries. A file read is saved locally only after the entire
transfer succeeds. Writes require an even byte length; odd reads return exactly
the requested byte length.

The 128 KiB layout is a **vendor-specific extension** using all 65536 16-bit
record addresses. The test app enables `AllowExtendedFileRecordAddresses` on its
RTU clients so writes can cross record 9999 where that device extension permits
it. The library's default remains standard-range write validation; other library
consumers must opt in explicitly. Devices with only standard-size files may reject
these extended addresses.

## Verification

From the repository root on Windows:

```text
python tests/run_ui_tests.py
python tests/run_tests.py
```

The UI suite builds the actual .NET Framework 4.8/WPF application and runs **18
checks** covering refresh eligibility, interval validation, non-overlap, failure
recovery, current-tab routing, manual-button/timeout locking, file-tab exclusion,
existing log4net consumers, bounded/concurrent logging, large file rollover,
odd read boundaries, retry/continue/abort in both directions and opt-in extended
addresses. It instantiates the real window without opening a serial/network
connection. `--artifacts <directory>` saves a rendered window preview for inspection.
The existing protocol suite still passes **76/76** checks. Release compilation
also succeeds without compiler warnings.

Real serial/RTU-over-TCP hardware timing and vendor-specific file behaviour still
require device checks. Published client binaries are available from the repository’s tagged GitHub
releases. Each release tag matches the client executable’s assembly version.
