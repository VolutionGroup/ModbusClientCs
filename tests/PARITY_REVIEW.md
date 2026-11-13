# C# / C++ RTU parity review — 13 November 2026

The corrected C# protocol layer passes **76/76 automated checks**. The original
local C# source passed **56/76** of the same cases, reproducing the missing
guards and parser failures. Both versions were compiled with Roslyn, C# 7.3 and
warning level 4 against the actual client sources and log4net; no compiler warnings
were emitted by the regression build. The comparison C++ revision is
`3495e14bfc59b4d65ee9fbb34ceaa864d7b55417` in `VolutionGroup/ModbusMaster`.
The actual `VVG.Modbus.csproj` Release build also succeeded with Visual Studio
MSBuild targeting .NET Framework 4.8, with output/intermediate files redirected
to the task workspace. `git diff --check` passed in the C# checkout.

The C# checkout already had uncommitted changes to `ClientRTU_Base.cs`, chiefly
corrected coil/file writes and a register-echo loop fix. Those changes were used
as the baseline and preserved. In particular, `WriteCoils()` and
`WriteFileRecord()` were retained byte-for-byte from the existing working copy.
No commits, pushes or package-version changes are part of this parity update.

## Fix comparison

| Area | C++ after recent fixes | C# before this update | C# after this update |
| --- | --- | --- | --- |
| FC15 coil bit order / byte count / padding | Correct LSB-first encoding | Already fixed in the local working copy | Preserved; tested against `CD 01` and every length 1–1968 |
| FC01/02 read byte counts | Validated before CRC access | Counts already checked | Shared bounds-first header/CRC helpers; null responses rejected |
| FC03/04 read byte counts | Must equal twice the requested count | Untrusted count could become a CRC/index bound; managed arrays could throw `IndexOutOfRangeException` | Counts validated before CRC, indexes derived from expected frame length |
| FC20 outer / inner response fields | Validated before CRC/copy | Nested fields unchecked; surplus bytes accepted | Exact frame length, outer count, subresponse length and reference type validated before CRC/copy |
| File-read rounding and copy length | Rounded wire length; exactly requested bytes copied | Odd `UInt16` length increment could wrap; padding returned to callers | Integer rounding after argument checks; exactly requested bytes returned |
| Zero-length calls | Rejected before UART access | Writes rejected empty arrays, but all five read functions accepted zero | All eight variable-length calls reject empty requests before transport access |
| FC04 configured timeout | Previously hard-coded; now forwarded | Already delegated to the transport's `Timeout` property | Preserved; no hard-coded FC04 timeout was present |
| Send failure | Checks UART's boolean result | `CommsSend` is void and transport failures propagate as exceptions | Contract documented; tests verify no receive follows a failed send and semaphore release permits retry |

Additional consistency fixes: input-register reads now enforce their existing
declared 122-register receive budget, and null register-write arrays produce
`ArgumentNullException` before transport access.

## Intentional differences retained

- C# uses exceptions and returned collections; C++ returns `bool` and writes to
  caller-owned buffers. Shared failures therefore have different public forms.
- C# has no C++ `Enable`/`Disable` gate. Both serialize transport transactions,
  using a semaphore in C# and a mutex in C++.
- C# supports larger transactions: 1960-bit reads, 122-register reads, 1968-coil
  writes and 120-register writes, compared with C++'s 216, 29, 184 and 27.
  These C# limits were not reduced to the embedded C++ buffer capacities.
- C# file reads are now limited to 242 requested bytes. This follows the other
  reads' approximate 250-byte transaction budget and keeps the rounded payload
  plus subresponse headers within the FC20 length-field maximum. C++ retains its
  128-byte buffer limit. FC20 field limits are defined in section 6.14 of the
  [Modbus Application Protocol V1.1b3](https://ptzprotocols.com/2%20Misc%20Protocols/Modbus/Modbus_Application_Protocol_V1_1b3.pdf).
- Existing C# FC15/21 code rejects addresses above 247 and handles address-zero
  writes without receiving a reply. The C++ policy permits all slave-address
  bytes and still has outstanding broadcast handling. This update preserves the
  existing C# write policy instead of removing those local improvements.
- Existing C# FC15 code validates the full 16-bit coil span. C++ retains its
  restricted register map. C# register reads still allow addresses above 9998.
- Existing C# file writes validate file/record ranges, reject odd byte lengths
  and allow up to 244 data bytes; those C++ issues remain outstanding.

## Compatibility and remaining work

Odd-length file reads now return exactly the requested number of bytes. A caller
that previously consumed the extra padding byte must adjust. Lengths above 242
now fail before transmission, including 65535, which previously could wrap during
rounding. Applications requiring larger file transfers should split requests at
word boundaries. No broad address-filtering changes were introduced.

The 76-case suite exercises the protocol layer with transport doubles, including
all outer count values at small and maximum sizes and all possible nested field
values for file reads. Transmission and receive exceptions are propagated and
the semaphore can be reused afterwards. It does not exercise real serial/network
I/O; a fake timeout check establishes delegation, not measured wall-clock accuracy.

Follow-up items outside the requested C++ fix parity:

- Broadcast reads and FC05/06/16 broadcast writes still need a consistent policy;
  only the pre-existing FC15/21 broadcast improvements were retained.
- `ClientTCP_RTU.CommsReceive` ignores the byte count returned by `Stream.Read`;
  real partial reads require transport-level handling and tests.
- `ClientRTU` uses a receive `List<byte>` from its serial event and receive path;
  event-buffer concurrency and serial framing require transport/hardware tests.
- Read address-span policy and consistency with existing write validation need
  a separate decision. Full protocol-maximum quantities are not assumed to be
  mandatory for this application.
- `ModbusException.Message` overrides the base message but string constructors do
  not populate its backing field, so some error messages are blank. This existing
  diagnostic issue was not changed as part of protocol parity.
