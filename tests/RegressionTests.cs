using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VVG.Modbus;

namespace VVG.Modbus.Tests
{
    internal sealed class FakeClient : ClientRTU_Base
    {
        internal byte[] Sent;
        internal byte[] Response;
        internal bool OverrideResponse;
        internal Exception SendFailure;
        internal Exception ReceiveFailure;
        internal int SendCalls;
        internal int ReceiveCalls;
        internal int Requested;
        internal TimeSpan ObservedTimeout;
        public override bool IsConnected { get { return true; } }
        public override TimeSpan Timeout { get; set; } = TimeSpan.FromMilliseconds(731);

        protected override void CommsSend(byte[] data)
        {
            SendCalls++;
            if (null != SendFailure)
            {
                throw SendFailure;
            }
            Sent = (byte[])data.Clone();
        }

        protected override Task<byte[]> CommsReceive(int count)
        {
            ReceiveCalls++;
            Requested = count;
            ObservedTimeout = Timeout;
            if (null != ReceiveFailure)
            {
                return Task.FromException<byte[]>(ReceiveFailure);
            }
            return Task.FromResult(OverrideResponse ? Response : RegressionTests.Reply(Sent));
        }
    }

    internal static class RegressionTests
    {
        private static readonly List<KeyValuePair<string, Action>> Cases = new List<KeyValuePair<string, Action>>();

        private static void Check(bool condition, string message)
        {
            if (false == condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        // Independent bitwise CRC oracle, also checked against a fixed known vector.
        internal static byte[] Frame(params byte[] bytes)
        {
            ushort crc = 0xffff;
            foreach (byte value in bytes)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (ushort)((crc >> 1) ^ (((crc & 1) != 0) ? 0xa001 : 0));
                }
            }
            return bytes.Concat(new[] { (byte)crc, (byte)(crc >> 8) }).ToArray();
        }

        internal static byte[] Reply(byte[] tx)
        {
            int function = tx[1];
            if ((function == 5) || (function == 6) || (function == 21))
            {
                return (byte[])tx.Clone();
            }
            if ((function == 15) || (function == 16))
            {
                return Frame(tx.Take(6).ToArray());
            }
            int quantity = (tx[4] << 8) | tx[5];
            if (function == 20)
            {
                int bytes = ((tx[8] << 8) | tx[9]) * 2;
                return Frame(new byte[] { tx[0], 20, (byte)(bytes + 2), (byte)(bytes + 1), 6 }
                    .Concat(Enumerable.Range(0, bytes).Select(index => (byte)(index + 1))).ToArray());
            }
            int dataBytes = function <= 2 ? (quantity + 7) / 8 : quantity * 2;
            return Frame(new byte[] { tx[0], (byte)function, (byte)dataBytes }
                .Concat(Enumerable.Range(0, dataBytes).Select(index => (byte)(0xa5 + index))).ToArray());
        }

        private static object Call(FakeClient client, int function, ushort quantity = 1, byte addr = 0x11)
        {
            switch (function)
            {
                case 1: return client.ReadCoils(addr, 0x13, quantity).GetAwaiter().GetResult().ToArray();
                case 2: return client.ReadDiscreteInputs(addr, 0x13, quantity).GetAwaiter().GetResult().ToArray();
                case 3: return client.ReadHoldingRegisters(addr, 0x13, quantity).GetAwaiter().GetResult().ToArray();
                case 4: return client.ReadInputRegisters(addr, 0x13, quantity).GetAwaiter().GetResult().ToArray();
                case 5: client.WriteCoil(addr, 0x13, true).GetAwaiter().GetResult(); break;
                case 6: client.WriteHoldingRegister(addr, 0x13, 0x1234).GetAwaiter().GetResult(); break;
                case 15: client.WriteCoils(addr, 0x13, new bool[quantity]).GetAwaiter().GetResult(); break;
                case 16: client.WriteHoldingRegisters(addr, 0x13, new ushort[quantity]).GetAwaiter().GetResult(); break;
                case 20: return client.ReadFileRecord(addr, 4, 0x13, quantity).GetAwaiter().GetResult();
                case 21: client.WriteFileRecord(addr, 4, 0x13, new byte[quantity]).GetAwaiter().GetResult(); break;
                default: throw new ArgumentOutOfRangeException(nameof(function));
            }
            return null;
        }

        private static void Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }
            throw new InvalidOperationException("Expected " + typeof(T).Name);
        }

        private static void Add(string name, Action body)
        {
            Cases.Add(new KeyValuePair<string, Action>(name, body));
        }

        private static void DefineCases()
        {
            Add("oracle.known_crc", () => Check(Frame(1,3,0,0,0,10).SequenceEqual(
                new byte[] {1,3,0,0,0,10,0xc5,0xcd}), "CRC oracle"));
            foreach (int function in new[] {1,2,3,4,5,6,15,16,20,21})
            {
                string prefix = "fc" + function + ".";
                Add(prefix + "valid", () =>
                {
                    var client = new FakeClient();
                    object result = Call(client, function, (ushort)(function >= 20 ? 4 : 1));
                    Check(client.SendCalls == 1 && client.ReceiveCalls == 1, "transaction lifecycle");
                    if (function <= 2) Check(((bool[])result)[0], "bit decode");
                    if ((function == 3) || (function == 4)) Check(((ushort[])result)[0] == 0xa5a6, "register decode");
                    if (function == 20) Check(((byte[])result).SequenceEqual(new byte[] {1,2,3,4}), "file decode");
                });
                Add(prefix + "transport_failures", () =>
                {
                    ushort quantity = (ushort)(function >= 20 ? 4 : 1);
                    var client = new FakeClient();
                    var failure = new IOException("send failed");
                    client.SendFailure = failure;
                    Throws<IOException>(() => Call(client, function, quantity));
                    Check(client.ReceiveCalls == 0, "receive attempted after failed send");
                    client.SendFailure = null;
                    Call(client, function, quantity); // Semaphore must have been released.
                    client.ReceiveFailure = new TimeoutException("receive failed");
                    client.Timeout = TimeSpan.FromMilliseconds(419);
                    Throws<TimeoutException>(() => Call(client, function, quantity));
                    Check(client.ObservedTimeout == client.Timeout, "timeout changed at receive boundary");
                    client.ReceiveFailure = null;
                    Call(client, function, quantity);
                });
                if ((function != 5) && (function != 6))
                {
                    Add(prefix + "zero_quantity", () =>
                    {
                        var client = new FakeClient();
                        Throws<ArgumentException>(() => Call(client, function, 0));
                        Check(client.SendCalls == 0 && client.ReceiveCalls == 0, "empty call accessed transport");
                    });
                }
            }
            foreach (int function in new[] {1,2,3,4,20})
            {
                ushort maximum = (ushort)(function <= 2 ? 1960 : function == 20 ? 242 : 122);
                string prefix = "fc" + function + ".";
                Add(prefix + "byte_counts", () =>
                {
                    foreach (ushort quantity in new[] {(ushort)1, maximum})
                    {
                        var valid = new FakeClient();
                        Call(valid, function, quantity);
                        byte[] reply = Reply(valid.Sent);
                        for (int count = 0; count <= 255; count++)
                        {
                            if (count == reply[2]) continue;
                            byte[] body = reply.Take(reply.Length - 2).ToArray();
                            body[2] = (byte)count;
                            var client = new FakeClient {OverrideResponse = true, Response = Frame(body)};
                            Throws<ModbusException>(() => Call(client, function, quantity));
                        }
                    }
                });
                Add(prefix + "truncated_and_null", () =>
                {
                    var valid = new FakeClient();
                    Call(valid, function, 3);
                    byte[] reply = Reply(valid.Sent);
                    for (int length = 0; length < reply.Length; length++)
                    {
                        var client = new FakeClient {OverrideResponse = true, Response = reply.Take(length).ToArray()};
                        Throws<ModbusException>(() => Call(client, function, 3));
                    }
                    Throws<ModbusException>(() => Call(new FakeClient {OverrideResponse = true}, function));
                });
                foreach (string fault in new[] {"crc", "address", "function", "exception", "long"})
                {
                    Add(prefix + "reject_" + fault, () =>
                    {
                        var valid = new FakeClient();
                        Call(valid, function, 2);
                        byte[] reply = Reply(valid.Sent);
                        if (fault == "crc") reply[reply.Length - 1] ^= 1;
                        else if (fault == "long") reply = reply.Concat(new byte[] {0}).ToArray();
                        else if (fault == "exception") reply = Frame(0x11, (byte)(function | 0x80), 2);
                        else
                        {
                            byte[] body = reply.Take(reply.Length - 2).ToArray();
                            body[fault == "address" ? 0 : 1] ^= 1;
                            reply = Frame(body);
                        }
                        Throws<ModbusException>(() => Call(new FakeClient {OverrideResponse = true, Response = reply}, function, 2));
                    });
                }
                Add(prefix + "above_capacity", () =>
                {
                    var client = new FakeClient();
                    Throws<ArgumentException>(() => Call(client, function, (ushort)(maximum + 1)));
                    Check(client.SendCalls == 0, "above-capacity read sent");
                });
            }
            foreach (int field in new[] {3,4})
            {
                Add("fc20.subresponse_" + field, () =>
                {
                    for (int value = 0; value <= 255; value++)
                    {
                        byte[] body = {0x11,20,4,3,6,1,2};
                        if (value == body[field]) continue;
                        body[field] = (byte)value;
                        Throws<ModbusException>(() => Call(new FakeClient {OverrideResponse = true, Response = Frame(body)}, 20, 2));
                    }
                });
            }
            Add("fc20.lengths", () =>
            {
                foreach (ushort quantity in new ushort[] {1,2,3,127,128,241,242})
                {
                    var client = new FakeClient();
                    var result = (byte[])Call(client, 20, quantity);
                    Check(result.Length == quantity, "odd length returned padding");
                    Check(result[quantity - 1] == quantity, "file payload truncated");
                    Check(client.Sent[9] == ((quantity + 1) / 2), "file word count");
                }
                foreach (ushort quantity in new ushort[] {243,244,65534,65535})
                {
                    var client = new FakeClient();
                    Throws<ArgumentException>(() => Call(client, 20, quantity));
                    Check(client.SendCalls == 0, "file length wrapped or exceeded frame limit");
                }
            });
            Add("arrays.null", () =>
            {
                var client = new FakeClient();
                Throws<ArgumentNullException>(() => client.WriteCoils(0x11, 0, null).GetAwaiter().GetResult());
                Throws<ArgumentNullException>(() => client.WriteHoldingRegisters(0x11, 0, null).GetAwaiter().GetResult());
                Throws<ArgumentNullException>(() => client.WriteFileRecord(0x11, 4, 0, null).GetAwaiter().GetResult());
                Check(client.SendCalls == 0, "null buffer sent");
            });
            Add("fc15.spec_example", () =>
            {
                var client = new FakeClient();
                bool[] coils = {true,false,true,true,false,false,true,true,true,false};
                client.WriteCoils(0x11, 0x13, coils).GetAwaiter().GetResult();
                Check(client.Sent.SequenceEqual(Frame(0x11,15,0,0x13,0,10,2,0xcd,1)), "FC15 wire example");
            });
            Add("fc15.all_lengths", () =>
            {
                for (int quantity = 1; quantity <= 1968; quantity++)
                {
                    var client = new FakeClient();
                    bool[] coils = Enumerable.Range(0, quantity).Select(index => (index % 3) == 0).ToArray();
                    client.WriteCoils(0x11, 0, coils).GetAwaiter().GetResult();
                    int bytes = (quantity + 7) / 8;
                    Check(client.Sent.Length == (bytes + 9) && client.Sent[6] == bytes, "FC15 frame/count");
                    for (int index = 0; index < quantity; index++)
                    {
                        Check((((client.Sent[7 + index / 8] >> (index % 8)) & 1) != 0) == coils[index], "FC15 bit order");
                    }
                    if ((quantity % 8) != 0)
                        Check((client.Sent[6 + bytes] >> (quantity % 8)) == 0, "FC15 padding");
                    Check(client.Sent.SequenceEqual(Frame(client.Sent.Take(client.Sent.Length - 2).ToArray())), "FC15 CRC");
                }
            });
            Add("existing_write_policy", () =>
            {
                foreach (int function in new[] {15,21})
                {
                    var client = new FakeClient();
                    Call(client, function, 2, 0);
                    Check(client.SendCalls == 1 && client.ReceiveCalls == 0, "broadcast waited");
                    Throws<ArgumentOutOfRangeException>(() => Call(client, function, 2, 248));
                }
                var file = new FakeClient();
                Throws<ArgumentOutOfRangeException>(() => Call(file, 21, 3));
                Check(file.SendCalls == 0, "odd file write sent");
                Call(file, 21, 244);
                Check(file.Sent.Length == 256, "maximum file write frame");
            });
        }

        private static int Main()
        {
            DefineCases();
            int failures = 0;
            foreach (var test in Cases)
            {
                try
                {
                    test.Value();
                    Console.WriteLine("PASS " + test.Key);
                }
                catch (Exception exception)
                {
                    failures++;
                    Console.WriteLine("FAIL " + test.Key + ": " + exception.GetType().Name + " " + exception.Message);
                }
            }
            Console.WriteLine("RESULT " + (Cases.Count - failures) + "/" + Cases.Count + " passed; " + failures + " failed");
            return failures == 0 ? 0 : 1;
        }
    }
}
