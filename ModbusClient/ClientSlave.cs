using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VVG.Modbus
{
    public class ClientSlave
    {
        ClientRTU _client;
        byte _address;

        public ClientSlave(ClientRTU client, byte slaveID)
        {
            _client = client;
            _address = slaveID;
        }

        public bool ReadCoil(UInt16 coilNo)
        {
            return _client.ReadCoil(_address, coilNo);
        }

        public bool[] ReadCoils(UInt16 coilStartNo, UInt16 len)
        {
            return _client.ReadCoils(_address, coilStartNo, len);
        }

        public void WriteCoil(UInt16 coilNo, bool txCoil)
        {
            _client.WriteCoil(_address, coilNo, txCoil);
        }

        public void WriteCoils(UInt16 coilStartNo, bool[] txCoils)
        {
            _client.WriteCoils(_address, coilStartNo, txCoils);
        }

        public bool ReadDiscreteInput(UInt16 inputNo)
        {
            return _client.ReadDiscreteInput(_address, inputNo);
        }

        public bool[] ReadDiscreteInputs(UInt16 inputStartNo, UInt16 len)
        {
            return _client.ReadDiscreteInputs(_address, inputStartNo, len);
        }

        public UInt16 ReadHoldingReg(UInt16 regNo)
        {
            return _client.ReadHoldingReg(_address, regNo);
        }

        public UInt16[] ReadHoldingRegs(UInt16 regStartNo, UInt16 len)
        {
            return _client.ReadHoldingRegs(_address, regStartNo, len);
        }

        public void WriteHoldingReg(UInt16 regNo, UInt16 txReg)
        {
            _client.WriteHoldingReg(_address, regNo, txReg);
        }

        public void WriteHoldingRegs(UInt16 startRegNo, UInt16[] txRegs)
        {
            _client.WriteHoldingRegs(_address, startRegNo, txRegs);
        }

        public UInt16 ReadInputReg(UInt16 regNo)
        {
            return _client.ReadInputReg(_address, regNo);
        }

        public UInt16[] ReadInputRegs(UInt16 regStartNo, UInt16 len)
        {
            return _client.ReadInputRegs(_address, regStartNo, len);
        }

        public byte[] ReadFileRecord(UInt16 fileNo, UInt16 recNo, UInt16 len)
        {
            return _client.ReadFileRecord(_address, fileNo, recNo, len);
        }
        public void WriteFileRecord(UInt16 fileNo, UInt16 recNo, byte[] fileRecs)
        {
            _client.WriteFileRecord(_address, fileNo, recNo, fileRecs);
        }
    }
}
