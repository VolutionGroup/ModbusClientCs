using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VVG.Modbus
{
    /// <summary>
    /// Modbus RTU over TCP client
    /// </summary>
    public class ClientTCP_RTU : ClientRTU_Base
    {
        private readonly TcpClient _tcpClient;
        public ClientTCP_RTU(TcpClient tcpClient)
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
                // The trouble is these don't work on TCP sockets...
                _tcpClient.ReceiveTimeout = (int)value.TotalMilliseconds;
                _tcpClient.SendTimeout = (int)value.TotalMilliseconds;
            }
        }

        protected override async Task<byte[]> CommsReceive(int len)
        {
            var sw = new Stopwatch();
            sw.Restart();
            while ((sw.Elapsed < Timeout) && (_tcpClient.Available < len))
            {
                // TBC the best way of dealing with this?
                // - polling stream.Length can't be best practise and setting up a listener thread seems OTT!
                await Task.Delay(1);
            }

            if (_tcpClient.Available < len) throw new Exception(String.Format("Error - {0} of {1} requested bytes received within {2}", _tcpClient.Available, len, Timeout));

            var data = new byte[len];
            var stream = _tcpClient.GetStream();
            stream.Read(data, 0, len);
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
