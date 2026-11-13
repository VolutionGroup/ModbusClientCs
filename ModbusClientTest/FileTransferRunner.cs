using log4net;
using System;
using System.IO;
using System.Threading.Tasks;

namespace VVG.Modbus.ClientTest
{
    /// <summary>Plans 128-byte commands over the test harness's vendor-specific 128 KiB files.</summary>
    internal sealed class FileTransferPosition
    {
        public const int BYTES_PER_FILE = 131072;
        public ushort FileNumber { get; private set; }
        public int RecordNumber { get; private set; }
        public int Length { get; private set; }
        public int Offset { get; private set; }
        public int AdditionalFiles { get; private set; }
        public bool Complete { get { return Offset == Length; } }
        public int ChunkLength { get { return Math.Min(128, Math.Min(Length - Offset, (65536 - RecordNumber) * 2)); } }

        public FileTransferPosition(ushort fileNumber, ushort recordNumber, int length)
        {
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
            long firstCapacity = (65536L - recordNumber) * 2;
            long overflow = Math.Max(0, (long)length - firstCapacity);
            AdditionalFiles = (int)((overflow + BYTES_PER_FILE - 1) / BYTES_PER_FILE);
            if ((long)fileNumber + AdditionalFiles > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(length), "Transfer would exceed the last file ID.");
            FileNumber = fileNumber;
            RecordNumber = recordNumber;
            Length = length;
        }

        public void Advance()
        {
            int length = ChunkLength;
            if (length <= 0) throw new InvalidOperationException("No transfer chunk remains.");
            Offset += length;
            RecordNumber += (length + 1) / 2;
            if (!Complete && RecordNumber == 65536)
            {
                FileNumber++;
                RecordNumber = 0;
            }
        }
    }

    /// <summary>Retries the same chunk, asking whether to continue when a retry budget is exhausted.</summary>
    internal static class FileTransferRunner
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(FileTransferRunner));

        public static async Task<bool> RunAsync(ClientSlave slave, byte[] data, FileTransferPosition position,
            bool writing, Func<Exception, FileTransferPosition, Task<bool>> continueAfterFailure,
            Action<float> progress, Func<Task> retryDelay = null)
        {
            if (data == null || data.Length != position.Length) throw new ArgumentException("Transfer buffer size mismatch.");
            if (writing && data.Length % 2 != 0) throw new ArgumentException("File writes require an even number of bytes.");
            int retryLimit = writing ? 10 : 5;
            int retries = 0;
            progress(0);
            while (!position.Complete)
            {
                try
                {
                    int count = position.ChunkLength;
                    if (writing)
                    {
                        var chunk = new byte[count];
                        Array.Copy(data, position.Offset, chunk, 0, count);
                        await slave.WriteFileRecord(position.FileNumber, (ushort)position.RecordNumber, chunk);
                    }
                    else
                    {
                        byte[] chunk = await slave.ReadFileRecord(position.FileNumber, (ushort)position.RecordNumber, (ushort)count);
                        if (chunk == null || chunk.Length != count) throw new IOException("Unexpected file-read byte count.");
                        Array.Copy(chunk, 0, data, position.Offset, count);
                    }
                }
                catch (Exception exception)
                {
                    retries++;
                    Log.Error(string.Format("File {0} failed at {1}/{2} bytes, file {3}, record {4}, attempt {5}/{6}",
                        writing ? "write" : "read", position.Offset, position.Length,
                        position.FileNumber, position.RecordNumber, retries, retryLimit), exception);
                    if (retries >= retryLimit)
                    {
                        if (!await continueAfterFailure(exception, position))
                        {
                            Log.Warn("File transfer stopped after retries were exhausted");
                            return false;
                        }
                        retries = 0;
                        Log.Info("Continuing file transfer from the failed chunk");
                    }
                    if (retryDelay == null) await Task.Delay(250);
                    else await retryDelay();
                    continue;
                }
                position.Advance();
                retries = 0;
                progress(100.0f * position.Offset / position.Length);
            }
            Log.Info(writing ? "File write complete" : "File read complete");
            return true;
        }
    }
}
