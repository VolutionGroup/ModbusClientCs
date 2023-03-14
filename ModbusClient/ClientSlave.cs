using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VVG.Modbus
{
    public class ClientSlave
    {
        public ClientRTU Client { get; set; }
        public byte Address { get; set; }
        
        public bool ReadCoil(UInt16 coilNo)
        {
            return Client.ReadCoil(Address, coilNo);
        }

        public bool[] ReadCoils(UInt16 coilStartNo, UInt16 len)
        {
            return Client.ReadCoils(Address, coilStartNo, len);
        }

        public void WriteCoil(UInt16 coilNo, bool txCoil)
        {
            Client.WriteCoil(Address, coilNo, txCoil);
        }

        public void WriteCoils(UInt16 coilStartNo, bool[] txCoils)
        {
            Client.WriteCoils(Address, coilStartNo, txCoils);
        }

        public bool ReadDiscreteInput(UInt16 inputNo)
        {
            return Client.ReadDiscreteInput(Address, inputNo);
        }

        public bool[] ReadDiscreteInputs(UInt16 inputStartNo, UInt16 len)
        {
            return Client.ReadDiscreteInputs(Address, inputStartNo, len);
        }

        public UInt16 ReadHoldingReg(UInt16 regNo)
        {
            return Client.ReadHoldingReg(Address, regNo);
        }

        public UInt16[] ReadHoldingRegs(UInt16 regStartNo, UInt16 len)
        {
            return Client.ReadHoldingRegs(Address, regStartNo, len);
        }

        public void WriteHoldingReg(UInt16 regNo, UInt16 txReg)
        {
            Client.WriteHoldingReg(Address, regNo, txReg);
        }

        public void WriteHoldingRegs(UInt16 startRegNo, UInt16[] txRegs)
        {
            Client.WriteHoldingRegs(Address, startRegNo, txRegs);
        }

        public UInt16 ReadInputReg(UInt16 regNo)
        {
            return Client.ReadInputReg(Address, regNo);
        }

        public UInt16[] ReadInputRegs(UInt16 regStartNo, UInt16 len)
        {
            return Client.ReadInputRegs(Address, regStartNo, len);
        }

        public byte[] ReadFileRecord(UInt16 fileNo, UInt16 recNo, UInt16 len)
        {
            return Client.ReadFileRecord(Address, fileNo, recNo, len);
        }
        public void WriteFileRecord(UInt16 fileNo, UInt16 recNo, byte[] fileRecs)
        {
            Client.WriteFileRecord(Address, fileNo, recNo, fileRecs);
        }
    }
}
