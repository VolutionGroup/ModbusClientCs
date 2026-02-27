using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VVG.Modbus
{
    [Serializable]
    public class ModbusException : Exception
    {
        public enum ModbusExceptions
        {
            NoException = 0,
            IllegalFunction,
            IllegalAddress,
            IllegalValue,
            SlaveDeviceFail,
            Acknowledge,
            SlaveDeviceBusy,
            NegativeAcknowledge,
            MemoryParityError,
            // No exception 9
            GatewayUnavailable = 10,
            GatewayTargetUnresponsive
        }

        private string _message;
        public override string Message
        {
            get
            {
                return _message;
            }
        }

        public ModbusException()
        {

        }

        public ModbusException(byte[] buffer)
        {
            DecodeBuffer(buffer, buffer.Length);
        }

        public ModbusException(byte[] buffer, int len)
        {
            DecodeBuffer(buffer, len);
        }

        private void DecodeBuffer(byte[] buffer, int len)
        {
            if (len == 0)
            {
                _message = "Timed out - no bytes received";
            }
            else if (len == 5)
            {
                UInt16 crc = Crc16.Calc(buffer, 3);
                if (((crc & 0x00FF) != buffer[3])
                    || (((crc & 0xFF00) >> 8) != buffer[4]))
                {
                    _message = "Corrupt modbus exception (5 bytes, invalid CRC)";
                }
                else
                {
                    _message = String.Format("Modbus Exception from {0} - fn: {1} code: {2}",
                        buffer[0], (ClientRTU.ModbusCommands)(buffer[1] & 0x7F), (ModbusExceptions)buffer[2]);
                }
            }
            else
            {
                _message = String.Format("Unknown response of {0} bytes rx", len);
            }
        }

        public ModbusException(string msg) : base(msg)
        {
            
        }

        public ModbusException(string msg, Exception inner) : base(msg, inner)
        {

        }
    }
}
