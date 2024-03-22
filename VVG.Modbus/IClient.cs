using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VVG.Modbus
{
    public interface IClient
    {
        /// <summary>
        /// Read a single coil register
        /// </summary>
        /// <param name="addr">Address of the modbus slave</param>
        /// <param name="coilNo">Coil register number (register 0000..9998 = address 0001..9999)</param>
        /// <returns>Coil value read</returns>
        Task<bool> ReadCoil(byte addr, UInt16 coilNo);

        /// <summary>
        /// Read multiple coil registers
        /// </summary>
        /// <param name="addr">Address of the modbus slave</param>
        /// <param name="coilStartNo">First coil register number to read (register 0000..9998 = address 0001..9999)</param>
        /// <param name="len">Number of coils to read</param>
        /// <returns>Coil values read</returns>
        Task<IEnumerable<bool>> ReadCoils(byte addr, UInt16 coilStartNo, UInt16 len);

        /// <summary>
        /// Read single discrete input register
        /// </summary>
        /// <param name="addr">Address of the modbus slave</param>
        /// <param name="inputNo">Input register number (register 0000..9998 = address 0001..9999)</param>
        /// <returns>Input register value read</returns>
        Task<bool> ReadDiscreteInput(byte addr, UInt16 inputNo);

        /// <summary>
        /// Read multiple discrete input registers
        /// </summary>
        /// <param name="addr">Address of the modbus slave</param>
        /// <param name="inputStartNo">First input register number to read (register 0000..9998 = address 0001..9999)</param>
        /// <param name="len">Number of input registers to read</param>
        /// <returns>Input register values read</returns>
        Task<IEnumerable<bool>> ReadDiscreteInputs(byte addr, UInt16 inputStartNo, UInt16 len);

        /// <summary>
        /// Read single holding register
        /// </summary>
        /// <param name="addr">Address of the modbus slave</param>
        /// <param name="regNo">Holding register number (register 0000..9998 = address 0001..9999)</param>
        /// <returns>Holding register value read</returns>
        Task<UInt16> ReadHoldingRegister(byte addr, UInt16 regNo);

        /// <summary>
        /// Read multiple holding registers
        /// </summary>
        /// <param name="addr">Address of the modbus slave</param>
        /// <param name="regStartNo">First holding register number to read (register 0000..9998 = address 0001..9999)</param>
        /// <param name="len">Number of holding registers to read</param>
        /// <returns>Holding register values read</returns>
        Task<IEnumerable<UInt16>> ReadHoldingRegisters(byte addr, UInt16 regStartNo, UInt16 len);

        /// <summary>
        /// Read single input register
        /// </summary>
        /// <param name="addr">Address of the modbus slave</param>
        /// <param name="regNo">Input register number (register 0000..9998 = address 0001..9999)</param>
        /// <returns>Input register value read</returns>
        Task<UInt16> ReadInputRegister(byte addr, UInt16 regNo);

        /// <summary>
        /// Read multiple input registers
        /// </summary>
        /// <param name="addr">Address of the modbus slave</param>
        /// <param name="regStartNo">First input register number to read (register 0000..9998 = address 0001..9999)</param>
        /// <param name="len">Number of input registers to read</param>
        /// <returns>Input register values read</returns>
        Task<IEnumerable<UInt16>> ReadInputRegisters(byte addr, UInt16 regStartNo, UInt16 len);

        /// <summary>
        /// Write to a single coil
        /// </summary>
        /// <param name="addr">Address of the modbus slave</param>
        /// <param name="coilNo">Coil register number (register 0000..9998 = address 0001..9999)</param>
        /// <param name="txCoil">Value to set</param>
        Task WriteCoil(byte addr, UInt16 coilNo, bool txCoil);

        /// <summary>
        /// Write to a single holding register
        /// </summary>
        /// <param name="addr">Address of the modbus slave</param>
        /// <param name="regNo">Holding register number (register 0000..9998 = address 0001..9999)</param>
        /// <param name="txReg">Value to set</param>
        Task WriteHoldingRegister(byte addr, UInt16 regNo, UInt16 txReg);

        /// <summary>
        /// Write to multiple coils
        /// </summary>
        /// <param name="addr">Address of the modbus slave</param>
        /// <param name="coilStartNo">First coil register number to write to (register 0000..9998 = address 0001..9999)</param>
        /// <param name="txCoils">Values to set</param>
        Task WriteCoils(byte addr, UInt16 coilStartNo, bool[] txCoils);

        /// <summary>
        /// Write to multiple holding registers
        /// </summary>
        /// <param name="addr">Address of the modbus slave</param>
        /// <param name="regStartNo">First holding register number to write to (register 0000..9998 = address 0001..9999)</param>
        /// <param name="txRegs">Values to write</param>
        Task WriteHoldingRegisters(byte addr, UInt16 regStartNo, UInt16[] txRegs);

        /// <summary>
        /// Read file records
        /// </summary>
        /// <param name="addr">Address of the modbus slave</param>
        /// <param name="fileNo">File number to read from</param>
        /// <param name="recNo">Record offset to read from (multiples of UInt16)</param>
        /// <param name="len">Number of records to read (multiples of UInt16)</param>
        /// <returns>Bytes read from the file</returns>
        Task<byte[]> ReadFileRecord(byte addr, UInt16 fileNo, UInt16 recNo, UInt16 len);

        /// <summary>
        /// Write to file records
        /// </summary>
        /// <param name="addr">Address of the modbus slave</param>
        /// <param name="fileNo">File number to write to</param>
        /// <param name="recNo">Record offset to write from (multiples of UInt16)</param>
        /// <param name="txFileRecs">Bytes to write</param>
        Task WriteFileRecord(byte addr, UInt16 fileNo, UInt16 recNo, byte[] txFileRecs);
    }
}
