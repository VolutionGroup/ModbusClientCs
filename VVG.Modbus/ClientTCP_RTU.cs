using System;
using System.Collections.Generic;
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
            Timeout = TimeSpan.FromMilliseconds(500);
        }

        public override bool IsConnected
        {
            get
            {
                return _tcpClient.Connected;
            }
        }

        public override TimeSpan Timeout
        {
            get
            {
                return TimeSpan.FromMilliseconds(_tcpClient.ReceiveTimeout);
            }

            set
            {
                _tcpClient.ReceiveTimeout = (int)value.TotalMilliseconds;
                _tcpClient.SendTimeout = (int)value.TotalMilliseconds;
            }
        }

        protected override async Task<byte[]> CommsReceive(int len)
        {
            var stream = _tcpClient.GetStream();
            var data = new byte[len];
            var rxLen = await stream.ReadAsync(data, 0, len); // TBC how this works WRT timeout and requested number of bytes - may need extra timeout handling?
            if (rxLen != len) throw new Exception(String.Format("Only received {0} of {1} requested bytes", rxLen, len));
            return data;
        }

        protected override void CommsSend(byte[] data)
        {
            var stream = _tcpClient.GetStream();
            stream.Flush(); // TBC if this clears the receive buffer?
            if (_tcpClient.Available > 0)
            {
                var scratch = new byte[_tcpClient.Available];
                stream.Read(scratch, 0, scratch.Length);
            }
            stream.Write(data, 0, data.Length);
        }
    }
}
