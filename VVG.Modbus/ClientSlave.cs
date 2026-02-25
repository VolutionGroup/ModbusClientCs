using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;
using System.ComponentModel;

namespace VVG.Modbus
{
    public class ClientSlave
    {
        public IClient Client { get; set; }
        public byte Address { get; set; }

        [Obsolete("Instantiate passing IClient and Address preferred."), EditorBrowsable(EditorBrowsableState.Never)]
        public ClientSlave()
        {
            // Assumed caller will initialise Client and Address via the Property accessors
        }

        [Obsolete("Instantiate passing IClient and Address preferred."), EditorBrowsable(EditorBrowsableState.Never)]
        public ClientSlave(SerialPort port, byte address)
        {
            // Instantiate as RTU
            Client = new ClientRTU(port);
            Address = address;
        }

        public ClientSlave(IClient client, byte address)
        {
            // Use the passed IClient directly
            Client = client;
            Address = address;
        }

        /// <summary>
        /// Read single coil
        /// </summary>
        /// <param name="coilNo">Coil address</param>
        /// <returns>Coil value read</returns>
        public async Task<bool> ReadCoil(UInt16 coilNo)
        {
            return await Client.ReadCoil(Address, coilNo);
        }

        /// <summary>
        /// Command 1 - read coils
        /// </summary>
        /// <param name="coilStartNo">Starting coil address</param>
        /// <param name="len">Number of coils to read</param>
        /// <returns>Coils read</returns>
        public async Task<IEnumerable<bool>> ReadCoils(UInt16 coilStartNo, UInt16 len)
        {
            return await Client.ReadCoils(Address, coilStartNo, len);
        }

        /// <summary>
        /// Command 5 - Write Single Coil
        /// </summary>
        /// <param name="coilNo">Coil address</param>
        /// <param name="txCoil">Coil state to set</param>
        public async Task WriteCoil(UInt16 coilNo, bool txCoil)
        {
            await Client.WriteCoil(Address, coilNo, txCoil);
        }

        /// <summary>
        /// Command 15 - Write Coils (multiple)
        /// </summary>
        /// <param name="coilStartNo">Starting coil address</param>
        /// <param name="txCoils">Coil values to write</param>
        public async Task WriteCoils(UInt16 coilStartNo, bool[] txCoils)
        {
            await Client.WriteCoils(Address, coilStartNo, txCoils);
        }

        /// <summary>
        /// Read single discrete input
        /// </summary>
        /// <param name="inputNo">Discrete input address</param>
        /// <returns>Value read</returns>
        public async Task<bool> ReadDiscreteInput(UInt16 inputNo)
        {
            return await Client.ReadDiscreteInput(Address, inputNo);
        }

        /// <summary>
        /// Command 2 - Read Discrete Inputs
        /// </summary>
        /// <param name="inputStartNo">Starting discrete input address</param>
        /// <param name="len">Number of discrete inputs to read</param>
        public async Task<IEnumerable<bool>> ReadDiscreteInputs(UInt16 inputStartNo, UInt16 len)
        {
            return await Client.ReadDiscreteInputs(Address, inputStartNo, len);
        }

        /// <summary>
        /// Read single holding register
        /// </summary>
        /// <param name="regNo">Holding register address</param>
        /// <returns>Register contents read</returns>
        public async Task<UInt16> ReadHoldingRegister(UInt16 regNo)
        {
            return await Client.ReadHoldingRegister(Address, regNo);
        }

        /// <summary>
        /// Command 3 - Read Holding Registers
        /// </summary>
        /// <param name="regStartNo">Starting holding register address</param>
        /// <param name="len">Number of holding registers to read</param>
        /// <returns>Registers read</returns>
        public async Task<IEnumerable<UInt16>> ReadHoldingRegisters(UInt16 regStartNo, UInt16 len)
        {
            return await Client.ReadHoldingRegisters(Address, regStartNo, len);
        }

        /// <summary>
        /// Command 6 - Write Single Holding Register
        /// </summary>
        /// <param name="regNo">Holding register address</param>
        /// <param name="txReg">Register value to set</param>
        public async Task WriteHoldingRegister(UInt16 regNo, UInt16 txReg)
        {
            await Client.WriteHoldingRegister(Address, regNo, txReg);
        }

        /// <summary>
        /// Command 16 - Write Holding Registers (multiple)
        /// </summary>
        /// <param name="regStartNo">Starting holding register address</param>
        /// <param name="txRegs">Register values to write</param>
        public async Task WriteHoldingRegisters(UInt16 startRegNo, UInt16[] txRegs)
        {
            await Client.WriteHoldingRegisters(Address, startRegNo, txRegs);
        }

        /// <summary>
        /// Read single input register
        /// </summary>
        /// <param name="regNo">Input register address</param>
        /// <returns>Register contents read</returns>
        public async Task<UInt16> ReadInputRegister(UInt16 regNo)
        {
            return await Client.ReadInputRegister(Address, regNo);
        }

        /// <summary>
        /// Command 4 - Read Input Registers
        /// </summary>
        /// <param name="regStartNo">Starting input register address</param>
        /// <param name="len">Number of input registers to read</param>
        /// <returns>Registers read</returns>
        public async Task<IEnumerable<UInt16>> ReadInputRegisters(UInt16 regStartNo, UInt16 len)
        {
            return await Client.ReadInputRegisters(Address, regStartNo, len);
        }

        /// <summary>
        /// Command 20 - Read File Record(s)
        /// </summary>
        /// <param name="fileNo">File number to read from</param>
        /// <param name="recNo">Record number(uint16 offset) in the file</param>
        /// <param name="len">Length(in bytes) to read</param>
        /// <returns>Records read from file</returns>
        public async Task<byte[]> ReadFileRecord(UInt16 fileNo, UInt16 recNo, UInt16 len)
        {
            return await Client.ReadFileRecord(Address, fileNo, recNo, len);
        }

        /// <summary>
        /// Command 21 - Write File Record(s)
        /// </summary>
        /// <param name="fileNo">File number to write to</param>
        /// <param name="recNo">Record number (uint16 offset) in the file</param>
        /// <param name="txFileRecs">Records to write to file</param>
        public async Task WriteFileRecord(UInt16 fileNo, UInt16 recNo, byte[] fileRecs)
        {
            await Client.WriteFileRecord(Address, fileNo, recNo, fileRecs);
        }
    }
}
