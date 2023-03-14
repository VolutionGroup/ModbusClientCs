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
            else
            {
                _message = String.Format("TODO - decode {0} bytes rx as exception", len);
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
