using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static VVG.Modbus.ClientRTU;

namespace VVG.Modbus
{
    public abstract class ClientRTU_Base : IClient
    {
        protected abstract Task<byte[]> CommsReceive(int count);
        /// <summary>Send the complete frame, throwing on transmission failure.</summary>
        /// <remarks>A successful return must not represent a failed or partial send.</remarks>
        protected abstract void CommsSend(byte[] data);
        public abstract bool IsConnected { get; }
        public abstract TimeSpan Timeout { get; set; }

        protected static readonly ILog _log = LogManager.GetLogger(typeof(ClientRTU_Base));

        // Slave addresses pass through unless a specific operation validates them.
        // Existing FC15/21 address and broadcast policies are retained.
        // N.B. Address 0001 = register 0000 (address range SHOULD be 0001..9999 but some devices go full 0x0000..0xffff)
        //const UInt16 MAX_REG_NO = 9998;

        const byte PDU_OVERHEAD = 4;    // address(1), function(1), crc(2)

        const byte READ_COILS_TX_LEN = PDU_OVERHEAD + 4;        // starting coil (2), number of coils (2)
        const byte READ_COILS_RX_OVERHEAD = PDU_OVERHEAD + 1;   // length in bytes (1)

        const byte READ_DI_TX_LEN = PDU_OVERHEAD + 4;       // starting DI (2), number of DI (2)
        const byte READ_DI_RX_OVERHEAD = PDU_OVERHEAD + 1;  // length in bytes (1)

        const byte READ_HR_TX_LEN = PDU_OVERHEAD + 4;       // starting HR (2), number of HR (2)
        const byte READ_HR_RX_OVERHEAD = PDU_OVERHEAD + 1;  // length in bytes (1)

        const byte READ_IR_TX_LEN = PDU_OVERHEAD + 4;       // starting IR (2), number of IR (2)
        const byte READ_IR_RX_OVERHEAD = PDU_OVERHEAD + 1;  // length in bytes (1)

        const byte WRITE_COIL_TX_LEN = PDU_OVERHEAD + 4;    // Coil number (2), value (2)
        const byte WRITE_COIL_RX_LEN = WRITE_COIL_TX_LEN;   // command echoed back

        const byte WRITE_HR_TX_LEN = PDU_OVERHEAD + 4;  // Holding register number (2), value (2)
        const byte WRITE_HR_RX_LEN = WRITE_HR_TX_LEN;   // command echoed back

        const byte WRITE_COILS_TX_OVERHEAD = PDU_OVERHEAD + 5;  // Starting coil (2), number of coils (2), length in bytes (1)
        const byte WRITE_COILS_RX_LEN = PDU_OVERHEAD + 4;       // Starting coil (2), number of coils (2)

        const byte WRITE_HRS_TX_OVERHEAD = PDU_OVERHEAD + 5;    // Starting HR (2), number of HR (2), length in bytes (1)
        const byte WRITE_HRS_RX_LEN = PDU_OVERHEAD + 4;         // Starting HR (2), number of HR (2)

        const byte READ_FILE_RECORD_SUBREC_REQ_LEN = 7; // Reference type (1), file number (2), record number (2), number of records (2)
        const byte READ_FILE_RECORD_TX_LEN = PDU_OVERHEAD + READ_FILE_RECORD_SUBREC_REQ_LEN + 1;    // Length in bytes (1)
        const byte READ_FILE_RECORD_SUBREC_RES_LEN = 2; // Response length (1), reference type (1)
        const byte READ_FILE_RECORD_RX_OVERHEAD = PDU_OVERHEAD + READ_FILE_RECORD_SUBREC_RES_LEN + 1; // Response data len (1)

        const byte WRITE_FILE_RECORD_SUBREC_LEN = 7;    // Reference type (1), file number (2), record number (2), number of records (2)
        const byte WRITE_FILE_RECORD_TX_OVERHEAD = PDU_OVERHEAD + WRITE_FILE_RECORD_SUBREC_LEN + 1; // Length in bytes (1)
        const byte WRITE_FILE_RECORD_RX_LEN = WRITE_FILE_RECORD_TX_OVERHEAD;    // command overhead echoed back

        public enum ModbusCommands
        {
            // No command 0
            ReadCoils = 1,
            ReadDiscreteInput = 2,
            ReadHoldingRegisters = 3,
            ReadInputRegisters = 4,
            WriteCoil = 5,
            WriteHoldingRegister = 6,
            ReadExceptionStatus = 7, /// not implemented
            Diagnostic = 8, /// not implemented
            // No command 9
            // No command 10
            MBCMD_GET_COM_EVT_CTR = 11, /// not implemented
            MBCMD_GET_COM_EVT_LOG = 12, /// not implemented
            // No command 13
            // No command 14
            WriteCoils = 15,
            WriteHoldingRegisters = 16,
            ReportSlaveID = 17, /// not implemented
            // No command 18
            // No command 19
            ReadFileRecords = 20,
            WriteFileRecords = 21,
            MBCMD_MASK_WRITE_REG = 22,
            MBCMD_READ_WRITE_MULTI_REG = 23,
            MBCMD_READ_FIFO_Q = 24,
            // No commands 25..42
            MBCMD_READ_DEV_ID = 43
        }

        private SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Read single coil
        /// </summary>
        /// <param name="addr">Slave address</param>
        /// <param name="coilNo">Coil address</param>
        /// <returns>Coil value read</returns>
        public async Task<bool> ReadCoil(byte addr, UInt16 coilNo)
        {
            var coils = await ReadCoils(addr, coilNo, 1);
            return coils.First();
        }

        /// <summary>
        /// Command 1 - read coils
        /// </summary>
        /// <param name="addr">Address of slave</param>
        /// <param name="coilStartNo">Starting coil address</param>
        /// <param name="len">Number of coils to read</param>
        /// <returns>Coils read</returns>
        public async Task<IEnumerable<bool>> ReadCoils(byte addr, UInt16 coilStartNo, UInt16 len)
        {
            // ~250 bytes assumed max the embedded device will handle
            UInt16 maxCoilRead = (UInt16)((250 - READ_COILS_RX_OVERHEAD) * 8);

            //if ((coilStartNo + len - 1) > MAX_REG_NO)
            //{
            //    throw new ArgumentException("Illegal coilStartNo", "coilStartNo");
            //}
            if ((0 == len) || (len > maxCoilRead))
            {
                throw new ArgumentOutOfRangeException(nameof(len), "Coil count is outside the supported non-empty range.");
            }

            byte[] txData = new byte[READ_COILS_TX_LEN];
            txData[0] = addr;
            txData[1] = (byte)ModbusCommands.ReadCoils;
            txData[2] = (byte)((coilStartNo & 0xFF00) >> 8);
            txData[3] = (byte)(coilStartNo & 0x00FF);
            txData[4] = (byte)((len & 0xFF00) >> 8);
            txData[5] = (byte)(len & 0x00FF);

            UInt16 crc = Crc16.Calc(txData, 6);
            txData[6] = (byte)(crc & 0x00FF);
            txData[7] = (byte)((crc & 0xFF00) >> 8);

            int expectedDataCount = (len / 8);
            if ((len % 8) > 0)
            {
                expectedDataCount++;
            }
            int expectedLen = READ_COILS_RX_OVERHEAD + expectedDataCount;

            byte[] rxData = null;
            await _semaphore.WaitAsync();
            try
            {
                CommsSend(txData);
                _log.DebugFormat("Reading {0} coils from {1}@{2}", len, coilStartNo, addr);
                rxData = await CommsReceive(expectedLen);
            }
            finally { _semaphore.Release(); }

            ValidateReadHeader(rxData, expectedLen, addr, ModbusCommands.ReadCoils, expectedDataCount);
            ValidateReadCrc(rxData, expectedLen);

            // Populate the return buffer
            byte bufferIdx = 3;
            byte bitPos = 0;
            var coils = new List<bool>();
            for (UInt16 i = 0; i < len; i++)
            {
                byte bitMask = (byte)(1 << bitPos);
                coils.Add((rxData[bufferIdx] & bitMask) != 0);
                if (++bitPos >= 8)
                {
                    bitPos = 0;
                    bufferIdx++;
                }
            }

            _log.DebugFormat("Read {0} coils", len);
            return coils;
        }

        /// <summary>
        /// Read single discrete input
        /// </summary>
        /// <param name="addr">Slave address</param>
        /// <param name="inputNo">Discrete input address</param>
        /// <returns>Value read</returns>
        public async Task<bool> ReadDiscreteInput(byte addr, UInt16 inputNo)
        {
            var di = await ReadDiscreteInputs(addr, inputNo, 1);
            return di.First();
        }

        /// <summary>
        /// Command 2 - Read Discrete Inputs
        /// </summary>
        /// <param name="addr">Slave address</param>
        /// <param name="inputStartNo">Starting discrete input address</param>
        /// <param name="len">Number of discrete inputs to read</param>
        public async Task<IEnumerable<bool>> ReadDiscreteInputs(byte addr, UInt16 inputStartNo, UInt16 len)
        {
            // ~250 bytes assumed max the embedded device will handle
            int maxCoilRead = (250 - READ_DI_RX_OVERHEAD) * 8;

            //if ((inputStartNo + len - 1) > MAX_REG_NO)
            //{
            //    throw new ArgumentException("Discrete inputs out of range requested", "inputStartNo");
            //}
            if ((0 == len) || (len > maxCoilRead))
            {
                throw new ArgumentOutOfRangeException(nameof(len), "Discrete-input count is outside the supported non-empty range.");
            }

            byte[] txData = new byte[READ_DI_TX_LEN];
            txData[0] = addr;
            txData[1] = (byte)ModbusCommands.ReadDiscreteInput;
            txData[2] = (byte)((inputStartNo & 0xFF00) >> 8);
            txData[3] = (byte)(inputStartNo & 0x00FF);
            txData[4] = (byte)((len & 0xFF00) >> 8);
            txData[5] = (byte)(len & 0x00FF);

            UInt16 crc = Crc16.Calc(txData, 6);
            txData[6] = (byte)(crc & 0x00FF);
            txData[7] = (byte)((crc & 0xFF00) >> 8);

            int expectedDataCount = (len / 8);
            if ((len % 8) > 0)
            {
                expectedDataCount++;
            }
            int expectedLen = READ_DI_RX_OVERHEAD + expectedDataCount;

            byte[] rxData = null;
            await _semaphore.WaitAsync();
            try
            {
                CommsSend(txData);
                _log.DebugFormat("Reading {0} discrete inputs from {1}@{2}", len, inputStartNo, addr);
                rxData = await CommsReceive(expectedLen);
            }
            finally { _semaphore.Release(); }

            ValidateReadHeader(rxData, expectedLen, addr, ModbusCommands.ReadDiscreteInput, expectedDataCount);
            ValidateReadCrc(rxData, expectedLen);

            // Populate the return buffer
            byte bufferIdx = 3;
            byte bitPos = 0;
            var values = new List<bool>();
            for (UInt16 i = 0; i < len; i++)
            {
                byte bitMask = (byte)(1 << bitPos);
                values.Add((rxData[bufferIdx] & bitMask) != 0);
                if (++bitPos >= 8)
                {
                    bitPos = 0;
                    bufferIdx++;
                }
            }

            _log.DebugFormat("Read {0} discrete inputs", len);
            return values;
        }

        /// <summary>
        /// Read single holding register
        /// </summary>
        /// <param name="addr">Slave address</param>
        /// <param name="regNo">Holding register address</param>
        /// <returns>Register contents read</returns>
        public async Task<UInt16> ReadHoldingRegister(byte addr, UInt16 regNo)
        {
            var hr = await ReadHoldingRegisters(addr, regNo, 1);
            return hr.First();
        }

        /// <summary>
        /// Command 3 - Read Holding Registers
        /// </summary>
        /// <param name="addr">Slave address</param>
        /// <param name="regStartNo">Starting holding register address</param>
        /// <param name="len">Number of holding registers to read</param>
        /// <returns>Registers read</returns>
        public async Task<IEnumerable<UInt16>> ReadHoldingRegisters(byte addr, UInt16 regStartNo, UInt16 len)
        {
            // ~250 bytes assumed max the embedded device will handle
            int maxRegsRead = (250 - READ_HR_RX_OVERHEAD) / 2;

            //if ((regStartNo + len - 1) > MAX_REG_NO)
            //{
            //    throw new ArgumentException("Illegal holiding register requested", "regStartNo");
            //}
            if ((0 == len) || (len > maxRegsRead))
            {
                throw new ArgumentOutOfRangeException(nameof(len), "Register count is outside the supported non-empty range.");
            }

            byte[] txData = new byte[READ_HR_TX_LEN];
            txData[0] = addr;
            txData[1] = (byte)ModbusCommands.ReadHoldingRegisters;
            txData[2] = (byte)((regStartNo & 0xFF00) >> 8);
            txData[3] = (byte)(regStartNo & 0x00FF);
            txData[4] = (byte)((len & 0xFF00) >> 8);
            txData[5] = (byte)(len & 0x00FF);

            UInt16 crc = Crc16.Calc(txData, 6);
            txData[6] = (byte)(crc & 0x00FF);
            txData[7] = (byte)((crc & 0xFF00) >> 8);

            int expectedLen = READ_HR_RX_OVERHEAD + (len * 2);

            byte[] rxData = null;
            await _semaphore.WaitAsync();
            try
            {
                _log.DebugFormat("Reading {0} holding registers from {1}@{2}", len, regStartNo, addr);
                CommsSend(txData);
                rxData = await CommsReceive(expectedLen);
            }
            finally { _semaphore.Release(); }

            ValidateReadHeader(rxData, expectedLen, addr, ModbusCommands.ReadHoldingRegisters, len * 2);
            ValidateReadCrc(rxData, expectedLen);

            var values = new List<UInt16>();
            for (UInt16 i = 0; i < len; i++)
            {
                values.Add((UInt16)(((UInt16)rxData[3 + i * 2] << 8) + rxData[4 + i * 2]));
            }

            _log.DebugFormat("Read {0} holding registers", len);
            return values;
        }

        /// <summary>
        /// Read single input register
        /// </summary>
        /// <param name="addr">Slave address</param>
        /// <param name="regNo">Input register address</param>
        /// <returns>Register contents read</returns>
        public async Task<UInt16> ReadInputRegister(byte addr, UInt16 regNo)
        {
            var ir = await ReadInputRegisters(addr, regNo, 1);
            return ir.First();
        }

        /// <summary>
        /// Command 4 - Read Input Registers
        /// </summary>
        /// <param name="addr">Slave address</param>
        /// <param name="regStartNo">Starting input register address</param>
        /// <param name="len">Number of input registers to read</param>
        /// <returns>Registers read</returns>
        public async Task<IEnumerable<UInt16>> ReadInputRegisters(byte addr, UInt16 regStartNo, UInt16 len)
        {
            // ~250 bytes assumed max the embedded device will handle
            int maxRegsRead = (250 - READ_IR_RX_OVERHEAD) / 2;

            //if ((regStartNo > MAX_REG_NO) || (len > maxRegsRead))
            //{
            //    throw new ArgumentException();
            //}

            if ((0 == len) || (len > maxRegsRead))
            {
                throw new ArgumentOutOfRangeException(nameof(len));
            }

            byte[] txData = new byte[READ_IR_TX_LEN];
            txData[0] = addr;
            txData[1] = (byte)ModbusCommands.ReadInputRegisters;
            txData[2] = (byte)((regStartNo & 0xFF00) >> 8);
            txData[3] = (byte)(regStartNo & 0x00FF);
            txData[4] = (byte)((len & 0xFF00) >> 8);
            txData[5] = (byte)(len & 0x00FF);

            UInt16 crc = Crc16.Calc(txData, 6);
            txData[6] = (byte)(crc & 0x00FF);
            txData[7] = (byte)((crc & 0xFF00) >> 8);
            int expectedLen = READ_IR_RX_OVERHEAD + (len * 2);

            byte[] rxData = null;
            await _semaphore.WaitAsync();
            try
            {
                CommsSend(txData);
                _log.DebugFormat("Reading {0} input registers from {1}@{2}", len, regStartNo, addr);
                rxData = await CommsReceive(expectedLen);
            }
            finally { _semaphore.Release(); }

            ValidateReadHeader(rxData, expectedLen, addr, ModbusCommands.ReadInputRegisters, len * 2);
            ValidateReadCrc(rxData, expectedLen);

            var values = new List<UInt16>();
            for (UInt16 i = 0; i < len; i++)
            {
                values.Add((UInt16)(((UInt16)rxData[3 + i * 2] << 8) + rxData[4 + i * 2]));
            }

            _log.DebugFormat("Read {0} input registers", len);
            return values;
        }

        /// <summary>
        /// Command 5 - Write Single Coil
        /// </summary>
        /// <param name="addr">Slave address</param>
        /// <param name="coilNo">Coil address</param>
        /// <param name="txCoil">Coil state to set</param>
        public async Task WriteCoil(byte addr, UInt16 coilNo, bool txCoil)
        {
            //if (coilNo > MAX_REG_NO)
            //{
            //    throw new ArgumentException();
            //}

            byte[] txData = new byte[WRITE_COIL_TX_LEN];
            txData[0] = addr;
            txData[1] = (byte)ModbusCommands.WriteCoil;
            txData[2] = (byte)((coilNo & 0xFF00) >> 8);
            txData[3] = (byte)(coilNo & 0x00FF);
            txData[4] = (byte)(txCoil ? 0xff : 0x00);
            txData[5] = (byte)0x00;

            UInt16 crc = Crc16.Calc(txData, 6);
            txData[6] = (byte)(crc & 0x00FF);
            txData[7] = (byte)((crc & 0xFF00) >> 8);

            byte[] rxData = null;
            await _semaphore.WaitAsync();
            try
            {
                _log.DebugFormat("Writing coil to {0}@{1}", coilNo, addr);
                CommsSend(txData);
                rxData = await CommsReceive(WRITE_COIL_RX_LEN);
            }
            finally { _semaphore.Release(); }

            if (rxData.Length != WRITE_COIL_RX_LEN)
            {
                throw new ModbusException(rxData);
            }

            // check the command was echoed back as sent
            for (UInt16 i = 0; i < WRITE_COIL_RX_LEN; i++)
            {
                if (txData[i] != rxData[i])
                {
                    // Mismatch - fail :(
                    _log.ErrorFormat("Read-back failed @ {0} - 0x{1:X2} != 0x{2:X2}", i, txData[i], rxData[i]);
                    throw new ModbusException("Failed to validate response");
                }
            }

            // All data matched - success!
            _log.Debug("Coil write OK");
        }

        /// <summary>
        /// Command 6 - Write Single Holding Register
        /// </summary>
        /// <param name="addr">Slave address</param>
        /// <param name="regNo">Holding register address</param>
        /// <param name="txReg">Register value to set</param>
        public async Task WriteHoldingRegister(byte addr, UInt16 regNo, UInt16 txReg)
        {
            //if (regNo > MAX_REG_NO)
            //{
            //    throw new ArgumentException();
            //}

            byte[] txData = new byte[WRITE_HR_TX_LEN];
            txData[0] = addr;
            txData[1] = (byte)ModbusCommands.WriteHoldingRegister;
            txData[2] = (byte)((regNo & 0xFF00) >> 8);
            txData[3] = (byte)(regNo & 0x00FF);
            txData[4] = (byte)((txReg & 0xFF00) >> 8);
            txData[5] = (byte)(txReg & 0x00FF);

            UInt16 crc = Crc16.Calc(txData, 6);
            txData[6] = (byte)(crc & 0x00FF);
            txData[7] = (byte)((crc & 0xFF00) >> 8);

            byte[] rxData = null;
            await _semaphore.WaitAsync();
            try
            {
                CommsSend(txData);
                _log.DebugFormat("Writing holiding register to {0}@{1}", regNo, addr);
                rxData = await CommsReceive(WRITE_HR_RX_LEN);
            }
            finally { _semaphore.Release(); }

            if (rxData.Length != WRITE_HR_RX_LEN)
            {
                throw new ModbusException(rxData);
            }

            // check the command was echoed back as sent
            for (UInt16 i = 0; i < rxData.Length; i++)
            {
                if (txData[i] != rxData[i])
                {
                    // Mismatch - fail :(
                    _log.ErrorFormat("Read-back failed @ {0} - 0x{1:X2} != 0x{2:X2}", i, txData[i], rxData[i]);
                    throw new ModbusException("Failed to validate response");
                }
            }

            // All data matched - success!
            _log.Debug("Holding register write OK");
        }

        /// <summary>
        /// Command 15 - Write Coils (multiple)
        /// </summary>
        /// <param name="addr">Slave address</param>
        /// <param name="coilStartNo">Starting coil address</param>
        /// <param name="txCoils">Coil values to write</param>
        public async Task WriteCoils(byte addr, UInt16 coilStartNo, bool[] txCoils)
        {
            if (addr > 247)
            {
                throw new ArgumentOutOfRangeException(nameof(addr));
            }
            if (txCoils == null)
            {
                throw new ArgumentNullException(nameof(txCoils));
            }
            if ((txCoils.Length == 0) || (txCoils.Length > 1968))
            {
                throw new ArgumentOutOfRangeException(nameof(txCoils));
            }
            if ((int)coilStartNo + txCoils.Length > 65536)
            {
                throw new ArgumentOutOfRangeException(nameof(coilStartNo));
            }

            int dataLength = (txCoils.Length + 7) / 8;
            byte[] txData = new byte[WRITE_COILS_TX_OVERHEAD + dataLength];

            txData[0] = addr;
            txData[1] = (byte)ModbusCommands.WriteCoils;
            txData[2] = (byte)(coilStartNo >> 8);
            txData[3] = (byte)coilStartNo;
            txData[4] = (byte)(txCoils.Length >> 8);
            txData[5] = (byte)txCoils.Length;
            txData[6] = (byte)dataLength;

            for (int i = 0; i < txCoils.Length; i++)
            {
                if (txCoils[i])
                {
                    txData[7 + (i / 8)] |= (byte)(1 << (i % 8));
                }
            }

            UInt16 crc = Crc16.Calc(txData, txData.Length - 2);
            txData[txData.Length - 2] = (byte)crc;
            txData[txData.Length - 1] = (byte)(crc >> 8);

            byte[] rxData;
            await _semaphore.WaitAsync();
            try
            {
                CommsSend(txData);
                if (addr == 0)
                {
                    return;
                }

                rxData = await CommsReceive(WRITE_COILS_RX_LEN);
            }
            finally
            {
                _semaphore.Release();
            }

            if (rxData.Length != WRITE_COILS_RX_LEN)
            {
                throw new ModbusException(rxData);
            }

            for (int i = 0; i < WRITE_COILS_RX_LEN - 2; i++)
            {
                if (txData[i] != rxData[i])
                {
                    throw new ModbusException("Echo-back verification failed");
                }
            }

            crc = Crc16.Calc(rxData, rxData.Length - 2);
            if (((byte)crc != rxData[rxData.Length - 2])
                || ((byte)(crc >> 8) != rxData[rxData.Length - 1]))
            {
                throw new ModbusException("CRC failure");
            }

            _log.Debug("Coils written OK");
        }

        /// <summary>
        /// Command 16 - Write Holding Registers (multiple)
        /// </summary>
        /// <param name="addr">Slave address</param>
        /// <param name="regStartNo">Starting holding register address</param>
        /// <param name="txRegs">Register values to write</param>
        public async Task WriteHoldingRegisters(byte addr, UInt16 regStartNo, UInt16[] txRegs)
        {
            if (null == txRegs)
            {
                throw new ArgumentNullException(nameof(txRegs));
            }
            // TODO make this dynamic
            byte[] txData = new byte[250];
            int maxRegWrite = (txData.Length - WRITE_HRS_TX_OVERHEAD) / 2;

            if (//((regStartNo + txRegs.Length - 1) > MAX_REG_NO)
                (txRegs.Length == 0)
                || (txRegs.Length > maxRegWrite))
            {
                throw new ArgumentException("Bad length");
            }

            // Prepare the header
            txData[0] = addr;
            txData[1] = (byte)ModbusCommands.WriteHoldingRegisters;
            txData[2] = (byte)((regStartNo & 0xFF00) >> 8);
            txData[3] = (byte)(regStartNo & 0x00FF);
            txData[4] = (byte)((txRegs.Length & 0xFF00) >> 8);
            txData[5] = (byte)(txRegs.Length & 0x00FF);
            txData[6] = (byte)(txRegs.Length * 2);  // number of *bytes*

            // Populate the txCoils on to the transmit buffer
            for (UInt16 i = 0; i < txRegs.Length; i++)
            {
                txData[7 + (i * 2)] = (byte)((txRegs[i] & 0xFF00) >> 8);
                txData[8 + (i * 2)] = (byte)(txRegs[i] & 0x00FF);
            }

            int txLen = WRITE_HRS_TX_OVERHEAD - 2 + (txRegs.Length * 2);
            UInt16 crc = Crc16.Calc(txData, txLen);
            txData[txLen++] = (byte)(crc & 0x00FF);
            txData[txLen++] = (byte)((crc & 0xFF00) >> 8);
            Array.Resize(ref txData, txLen);

            byte[] rxData = null;
            await _semaphore.WaitAsync();
            try
            {
                _log.DebugFormat("Writing {0} holiding registers from {1}@{2}", txRegs.Length, regStartNo, addr);
                CommsSend(txData);
                rxData = await CommsReceive(WRITE_HRS_RX_LEN);
            }
            finally { _semaphore.Release(); }

            if (rxData.Length != WRITE_HRS_RX_LEN)
            {
                throw new ModbusException(rxData);
            }

            // Verify echo-back (excl. CRC)
            for (int i = 0; i < rxData.Length - 2; i++)
            {
                if (txData[i] != rxData[i])
                {
                    _log.ErrorFormat("Read-back failed @ {0} - 0x{1:X2} != 0x{2:X2}", i, txData[i], rxData[i]);
                    throw new ModbusException("Read-back failure");
                }
            }

            // Verify CRC
            crc = Crc16.Calc(rxData, rxData.Length - 2);
            if (((crc & 0x00FF) != rxData[rxData.Length - 2])
                || (((crc & 0xFF00) >> 8) != rxData[rxData.Length - 1]))
            {
                _log.ErrorFormat("Expected CRC 0x{0:X4} received 0x{1:X2}{2:X2}", crc, rxData[rxData.Length - 1], rxData[rxData.Length - 2]);
                throw new ModbusException("CRC failure");
            }

            _log.Debug("Holding registers writen OK");
        }

        /// <summary>
        /// Command 20 - Read File Record(s)
        /// </summary>
        /// <param name="addr">Address of the slave</param>
        /// <param name="fileNo">File number to read from</param>
        /// <param name="recNo">Record number(uint16 offset) in the file</param>
        /// <param name="len">Length(in bytes) to read</param>
        /// <returns>Exactly len bytes; an odd request reads a padded word but omits the padding byte.</returns>
        public async Task<byte[]> ReadFileRecord(byte addr, UInt16 fileNo, UInt16 recNo, UInt16 len)
        {
            byte[] txData = new byte[READ_FILE_RECORD_TX_LEN];

            // Use the same approximately 250-byte transaction budget as the other reads.
            // Rounded data plus the two subresponse header bytes must also fit 0xF5.
            const int MAX_FILE_READ_BYTES = 242;
            if ((0 == len) || (len > MAX_FILE_READ_BYTES))
            {
                throw new ArgumentOutOfRangeException(nameof(len));
            }
            int requestedLength = len;
            int recordBytes = requestedLength + (requestedLength % 2);

            // Form the request
            txData[0] = addr;
            txData[1] = (byte)ModbusCommands.ReadFileRecords;
            txData[2] = (byte)READ_FILE_RECORD_SUBREC_REQ_LEN; // Sub-record length
            txData[3] = (byte)6;  // Reference type is always 6
            txData[4] = (byte)((fileNo & 0xFF00) >> 8);
            txData[5] = (byte)(fileNo & 0x00FF);
            txData[6] = (byte)((recNo & 0xFF00) >> 8);
            txData[7] = (byte)(recNo & 0x00FF);
            txData[8] = (byte)(((recordBytes / 2) & 0xFF00) >> 8);
            txData[9] = (byte)((recordBytes / 2) & 0x00FF);  // record length = byte length / 2

            UInt16 crc = Crc16.Calc(txData, 10);
            txData[10] = (byte)(crc & 0x00FF);
            txData[11] = (byte)((crc & 0xFF00) >> 8);

            int expectedLen = (READ_FILE_RECORD_RX_OVERHEAD + recordBytes);

            byte[] rxData = null;
            await _semaphore.WaitAsync();
            try
            {
                // Send the request
                _log.DebugFormat("Writing {0} records to file number {1} from record number {2} on {3}", len / 2, fileNo, recNo, addr);
                CommsSend(txData);

                // Get the response
                rxData = await CommsReceive(expectedLen);
            }
            finally { _semaphore.Release(); }

            ValidateReadHeader(rxData, expectedLen, addr, ModbusCommands.ReadFileRecords,
                               recordBytes + READ_FILE_RECORD_SUBREC_RES_LEN);
            if ((rxData[3] != (recordBytes + 1)) || (rxData[4] != 6))
            {
                throw new ModbusException(rxData);
            }
            ValidateReadCrc(rxData, expectedLen);

            // Copy the record data back to the passed buffer
            var rxRecs = new byte[requestedLength];
            Array.Copy(rxData, READ_FILE_RECORD_RX_OVERHEAD - 2, rxRecs, 0, requestedLength);

            _log.DebugFormat("Read {0} bytes", requestedLength);
            return rxRecs;
        }

        /// <summary>
        /// Command 21 - Write File Record(s)
        /// </summary>
        /// <param name="addr">Address of the slave</param>
        /// <param name="fileNo">File number to write to</param>
        /// <param name="recNo">Record number (uint16 offset) in the file</param>
        /// <param name="txFileRecs">Records to write to file</param>
        public async Task WriteFileRecord(byte addr, UInt16 fileNo, UInt16 recNo, byte[] txFileRecs)
        {
            if (addr > 247)
            {
                throw new ArgumentOutOfRangeException(nameof(addr));
            }
            if (fileNo == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fileNo));
            }
            if (txFileRecs == null)
            {
                throw new ArgumentNullException(nameof(txFileRecs));
            }
            if ((txFileRecs.Length == 0)
                || (txFileRecs.Length > 244)
                || ((txFileRecs.Length % 2) != 0))
            {
                throw new ArgumentOutOfRangeException(nameof(txFileRecs));
            }
            if ((recNo > 9999) || ((int)recNo + (txFileRecs.Length / 2) > 10000))
            {
                throw new ArgumentOutOfRangeException(nameof(recNo));
            }

            byte[] txData = new byte[WRITE_FILE_RECORD_TX_OVERHEAD + txFileRecs.Length];
            txData[0] = addr;
            txData[1] = (byte)ModbusCommands.WriteFileRecords;
            txData[2] = (byte)(WRITE_FILE_RECORD_SUBREC_LEN + txFileRecs.Length);
            txData[3] = 6;
            txData[4] = (byte)(fileNo >> 8);
            txData[5] = (byte)fileNo;
            txData[6] = (byte)(recNo >> 8);
            txData[7] = (byte)recNo;
            txData[8] = (byte)((txFileRecs.Length / 2) >> 8);
            txData[9] = (byte)(txFileRecs.Length / 2);

            Array.Copy(txFileRecs, 0, txData, WRITE_FILE_RECORD_TX_OVERHEAD - 2, txFileRecs.Length);

            UInt16 crc = Crc16.Calc(txData, txData.Length - 2);
            txData[txData.Length - 2] = (byte)crc;
            txData[txData.Length - 1] = (byte)(crc >> 8);

            byte[] rxData;
            await _semaphore.WaitAsync();
            try
            {
                CommsSend(txData);
                if (addr == 0)
                {
                    return;
                }

                rxData = await CommsReceive(txData.Length);
            }
            finally
            {
                _semaphore.Release();
            }

            if (rxData.Length != txData.Length)
            {
                throw new ModbusException(rxData);
            }

            for (int i = 0; i < txData.Length; i++)
            {
                if (txData[i] != rxData[i])
                {
                    throw new ModbusException("Read-back verification failed");
                }
            }

            _log.DebugFormat("Wrote {0} bytes", txFileRecs.Length);
        }

        /// <summary>Validate the full frame length and byte count before any CRC or payload access.</summary>
        private static void ValidateReadHeader(byte[] rxData, int expectedLength, byte addr,
                                               ModbusCommands command, int expectedDataCount)
        {
            if (null == rxData)
            {
                throw new ModbusException("No response data");
            }
            if ((rxData.Length != expectedLength)
                || (rxData[0] != addr)
                || (rxData[1] != (byte)command)
                || (rxData[2] != expectedDataCount))
            {
                throw new ModbusException(rxData);
            }
        }

        /// <summary>Check CRC bytes at positions derived from the validated expected frame size.</summary>
        private static void ValidateReadCrc(byte[] rxData, int expectedLength)
        {
            int crcOffset = expectedLength - 2;
            UInt16 crc = Crc16.Calc(rxData, crcOffset);
            if (((byte)crc != rxData[crcOffset]) || ((byte)(crc >> 8) != rxData[crcOffset + 1]))
            {
                _log.ErrorFormat("Expected CRC 0x{0:X4} received 0x{1:X2}{2:X2}",
                                 crc, rxData[crcOffset + 1], rxData[crcOffset]);
                throw new ModbusException("CRC failure");
            }
        }
    }
}
