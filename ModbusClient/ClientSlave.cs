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
        
        public async Task<bool> ReadCoil(UInt16 coilNo)
        {
            return await Client.ReadCoil(Address, coilNo);
        }

        public async Task<bool[]> ReadCoils(UInt16 coilStartNo, UInt16 len)
        {
            return await Client.ReadCoils(Address, coilStartNo, len);
        }

        public async Task WriteCoil(UInt16 coilNo, bool txCoil)
        {
            await Client.WriteCoil(Address, coilNo, txCoil);
        }

        public async Task WriteCoils(UInt16 coilStartNo, bool[] txCoils)
        {
            await Client.WriteCoils(Address, coilStartNo, txCoils);
        }

        public async Task<bool> ReadDiscreteInput(UInt16 inputNo)
        {
            return await Client.ReadDiscreteInput(Address, inputNo);
        }

        public async Task<bool[]> ReadDiscreteInputs(UInt16 inputStartNo, UInt16 len)
        {
            return await Client.ReadDiscreteInputs(Address, inputStartNo, len);
        }

        public async Task<UInt16> ReadHoldingReg(UInt16 regNo)
        {
            return await Client.ReadHoldingReg(Address, regNo);
        }

        public async Task<UInt16[]> ReadHoldingRegs(UInt16 regStartNo, UInt16 len)
        {
            return await Client.ReadHoldingRegs(Address, regStartNo, len);
        }

        public async Task WriteHoldingReg(UInt16 regNo, UInt16 txReg)
        {
            await Client.WriteHoldingReg(Address, regNo, txReg);
        }

        public async Task WriteHoldingRegs(UInt16 startRegNo, UInt16[] txRegs)
        {
            await Client.WriteHoldingRegs(Address, startRegNo, txRegs);
        }

        public async Task<UInt16> ReadInputReg(UInt16 regNo)
        {
            return await Client.ReadInputReg(Address, regNo);
        }

        public async Task<UInt16[]> ReadInputRegs(UInt16 regStartNo, UInt16 len)
        {
            return await Client.ReadInputRegs(Address, regStartNo, len);
        }

        public async Task<byte[]> ReadFileRecord(UInt16 fileNo, UInt16 recNo, UInt16 len)
        {
            return await Client.ReadFileRecord(Address, fileNo, recNo, len);
        }
        public async Task WriteFileRecord(UInt16 fileNo, UInt16 recNo, byte[] fileRecs)
        {
            await Client.WriteFileRecord(Address, fileNo, recNo, fileRecs);
        }
    }
}
