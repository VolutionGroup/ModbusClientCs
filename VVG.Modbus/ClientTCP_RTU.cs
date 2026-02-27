using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace VVG.Modbus
{
    /// <summary>
    /// Modbus RTU over TCP client
    /// </summary>
    public class ClientTCP_RTU : ClientRTU_Base
    {
        private readonly TcpClient _tcpClient;
        ClientTCP_RTU(TcpClient tcpClient)
        {
            _tcpClient = tcpClient;
            //Timeout = TimeSpan.FromSeconds(5); // TBC
        }

        public override bool IsConnected
        {
            get
            {
                return _tcpClient.Connected;
            }
        }

        public override TimeSpan Timeout { get; set; }

        protected override async Task<byte[]> CommsReceive(int len)
        {
            var stream = _tcpClient.GetStream();
            var data = new byte[len];
            var sw = new Stopwatch();
            int rxCount = 0;
            sw.Restart();
            while ((sw.Elapsed < Timeout) && (rxCount < len))
            {
                var rxLen = await stream.ReadAsync(data, rxCount, len - rxCount);
                rxCount += rxLen;
            }
            if (rxCount != len) throw new Exception(String.Format("Only received {0} of {1} requested bytes within {2}", rxCount, len, Timeout));
            return data;
        }

        protected override void CommsSend(byte[] data)
        {
            var stream = _tcpClient.GetStream();
            stream.Flush(); // TBC if this clears the receive buffer?
            if (_tcpClient.Available > 0)
            {
                // Actuall pull from the stream to be sure
                var scratch = new byte[_tcpClient.Available];
                stream.Read(scratch, 0, scratch.Length);
            }
            stream.Write(data, 0, data.Length);
        }
    }
}
