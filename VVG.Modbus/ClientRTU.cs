using log4net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace VVG.Modbus
{
    /// <summary>
    /// Modbus RTU over SerialPort client
    /// </summary>
    public class ClientRTU : ClientRTU_Base
    {
        private SerialPort _comms;

        public SerialPort Port
        {
            get
            {
                return _comms;
            }
            set
            {
                // TODO - do not use DataReceived https://sparxeng.com/blog/software/must-use-net-system-io-ports-serialport
                if (_comms != null)
                {
                    try
                    {
                        _comms.DataReceived -= comms_DataReceived;
                    }
                    catch (Exception)
                    { }
                }
                
                _comms = value;

                if (_comms != null)
                {
                    _comms.ReadTimeout = 500;
                    _comms.WriteTimeout = 500;
                    _comms.DataReceived += comms_DataReceived;
                }
            }
        }

        public override bool IsConnected
        {
            get
            {
                if (_comms == null) return false;
                return _comms.IsOpen;
            }
        }

        public override TimeSpan Timeout
        {
            get
            {
                return TimeSpan.FromMilliseconds(_comms.ReadTimeout);
            }

            set
            {
                _comms.ReadTimeout = (int)value.TotalMilliseconds;
                _comms.WriteTimeout = (int)value.TotalMilliseconds;
            }
        }

        public ClientRTU(SerialPort port)
        {
            Port = port;
            Timeout = TimeSpan.FromMilliseconds(500);
        }

        #region Receive data async
        private List<byte> _rxData = new List<byte>();
        private AutoResetEvent _dataRx = new AutoResetEvent(false);

        // TODO - do not use DataReceived https://sparxeng.com/blog/software/must-use-net-system-io-ports-serialport
        private void comms_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (e.EventType == SerialData.Chars)
            {
                var rx = new byte[_comms.BytesToRead];
                _comms.Read(rx, 0, rx.Length);
                _rxData.AddRange(rx);
                _dataRx.Set();
            }
        }

        protected override async Task<byte[]> CommsReceive(int count)
        {
            var sw = new Stopwatch();
            sw.Restart();
            _dataRx.Reset();
            while ((sw.Elapsed < Timeout) && (_rxData.Count < count))
            {
                var msRemaining = (int)(Timeout.TotalMilliseconds - sw.ElapsedMilliseconds);
                if (msRemaining <= 0)    // Sanity check
                {
                    break;
                }
                await Task.Run(() => _dataRx.WaitOne(msRemaining));
            }

            var data = _rxData.ToArray();
            _rxData.Clear();
            _log.DebugFormat("Received {0} bytes", data.Length);
            return data;
        }

        protected override void CommsSend(byte[] data)
        {
            _comms.ReadExisting();
            _rxData.Clear();
            _comms.Write(data, 0, data.Length);
        }
        #endregion
    }
}
