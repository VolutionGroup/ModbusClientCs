using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;
using System.Threading;
using System.Diagnostics;
using log4net;

namespace VVG.Modbus
{
    public class ClientRTU : IClient
    {
        private static readonly ILog _log = LogManager.GetLogger(typeof(ClientRTU));

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
        const byte READ_FILE_RECORD_RX_OVERHEAD = PDU_OVERHEAD + READ_FILE_RECORD_SUBREC_RES_LEN;

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
	
        public int TimeoutMs { get; set; }

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

        public bool IsConnected
        {
            get
            {
                if (_comms == null) return false;
                return _comms.IsOpen;
            }
        }

        public ClientRTU(SerialPort port)
        {
            Port = port;
            TimeoutMs = 500;
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

        private async Task<byte[]> CommsReceive(int count)
        {
            var sw = new Stopwatch();
            sw.Restart();
            _dataRx.Reset();
            while ((sw.ElapsedMilliseconds < TimeoutMs) && (_rxData.Count < count))
            {
                var msRemaining = (int)(TimeoutMs - sw.ElapsedMilliseconds);
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

        private void CommsPurge()
        {
            _comms.ReadExisting();
            _rxData.Clear();
        }
        #endregion

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
            if (len > maxCoilRead)
            {
                throw new ArgumentException("Too many coils requested", "len");
            }

            CommsPurge();

            byte[] txData = new byte[READ_COILS_TX_LEN];
            txData[0] = addr;
            txData[1] = (byte)ModbusCommands.ReadCoils;
            txData[2] = (byte)((coilStartNo & 0xFF00) >> 8);
            txData[3] = (byte)(coilStartNo & 0x00FF);
            txData[4] = (byte)((len & 0xFF00) >> 8);
            txData[5] = (byte)(len & 0x00FF);

            UInt16 crc = Crc16(txData, 6);
            txData[6] = (byte)(crc & 0x00FF);
            txData[7] = (byte)((crc & 0xFF00) >> 8);
            _comms.Write(txData, 0, txData.Length);

            int expectedDataCount = (len / 8);
            if ((len % 8) > 0)
            {
                expectedDataCount++;
            }
            int expectedLen = READ_COILS_RX_OVERHEAD + expectedDataCount;

            _log.DebugFormat("Reading {0} coils from {1}@{2}", len, coilStartNo, addr);
            var rxData = await CommsReceive(expectedLen);

            // Check the length and header
            if ((rxData.Length != expectedLen)
                || (rxData[0] != addr)
                || (rxData[1] != (byte)ModbusCommands.ReadCoils)
                || (rxData[2] != expectedDataCount))
            {
                throw new ModbusException(rxData);
            }

            // Check the CRC
            crc = Crc16(rxData, expectedDataCount + 3);
            if (((crc & 0x00FF) != rxData[rxData[2] + 3])
                || (((crc & 0xFF00) >> 8) != rxData[rxData[2] + 4]))
            {
                _log.ErrorFormat("Expected CRC 0x{0:X4} received 0x{1:X2}{2:X2}", crc, rxData[rxData[2] + 4], rxData[rxData[2] + 3]);
                throw new ModbusException("CRC failure");
            }

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
            if (len > maxCoilRead)
            {
                throw new ArgumentException("Too many discrete inputs requested", "len");
            }
            
            CommsPurge();

            byte[] txData = new byte[READ_DI_TX_LEN];
            txData[0] = addr;
            txData[1] = (byte)ModbusCommands.ReadDiscreteInput;
            txData[2] = (byte)((inputStartNo & 0xFF00) >> 8);
            txData[3] = (byte)(inputStartNo & 0x00FF);
            txData[4] = (byte)((len & 0xFF00) >> 8);
            txData[5] = (byte)(len & 0x00FF);

            UInt16 crc = Crc16(txData, 6);
            txData[6] = (byte)(crc & 0x00FF);
            txData[7] = (byte)((crc & 0xFF00) >> 8);
            _comms.Write(txData, 0, READ_DI_TX_LEN);

            int expectedDataCount = (len / 8);
            if ((len % 8) > 0)
            {
                expectedDataCount++;
            }
            int expectedLen = READ_DI_RX_OVERHEAD + expectedDataCount;

            _log.DebugFormat("Reading {0} discrete inputs from {1}@{2}", len, inputStartNo, addr);
            var rxData = await CommsReceive(expectedLen);

            // Check the length and header
            if ((rxData.Length != expectedLen)
                || (rxData[0] != addr)
                || (rxData[1] != (byte)ModbusCommands.ReadDiscreteInput)
                || (rxData[2] != expectedDataCount))
            {
                throw new ModbusException(rxData);
            }

            // Check the CRC
            crc = Crc16(rxData, expectedDataCount + 3);
            if (((crc & 0x00FF) != rxData[rxData[2] + 3])
                || (((crc & 0xFF00) >> 8) != rxData[rxData[2] + 4]))
            {
                _log.ErrorFormat("Expected CRC 0x{0:X4} received 0x{1:X2}{2:X2}", crc, rxData[rxData[2] + 4], rxData[rxData[2] + 3]);
                throw new ModbusException("CRC failure");
            }

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
            if (len > maxRegsRead)
            {
                throw new ArgumentException("Too many holding registers requested", "len");
            }

            CommsPurge();

            byte[] txData = new byte[READ_HR_TX_LEN];
            txData[0] = addr;
            txData[1] = (byte)ModbusCommands.ReadHoldingRegisters;
            txData[2] = (byte)((regStartNo & 0xFF00) >> 8);
            txData[3] = (byte)(regStartNo & 0x00FF);
            txData[4] = (byte)((len & 0xFF00) >> 8);
            txData[5] = (byte)(len & 0x00FF);

            UInt16 crc = Crc16(txData, 6);
            txData[6] = (byte)(crc & 0x00FF);
            txData[7] = (byte)((crc & 0xFF00) >> 8);

            _log.DebugFormat("Reading {0} holding registers from {1}@{2}", len, regStartNo, addr);
            _comms.Write(txData, 0, READ_HR_TX_LEN);

            int expectedLen = READ_HR_RX_OVERHEAD + (len * 2);
            var rxData = await CommsReceive(expectedLen);

            if ((rxData.Length != expectedLen)
                || (rxData[0] != addr)
                || (rxData[1] != (byte)ModbusCommands.ReadHoldingRegisters))
            {
                throw new ModbusException(rxData);
            }

            crc = Crc16(rxData, rxData[2] + 3);
            if (((crc & 0x00FF) != rxData[rxData[2] + 3])
                || (((crc & 0xFF00) >> 8) != rxData[rxData[2] + 4]))
            {
                _log.ErrorFormat("Expected CRC 0x{0:X4} received 0x{1:X2}{2:X2}", crc, rxData[rxData[2] + 4], rxData[rxData[2] + 3]);
                throw new ModbusException("CRC failure");
            }

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

            CommsPurge();

            byte[] txData = new byte[READ_IR_TX_LEN];
            txData[0] = addr;
            txData[1] = (byte)ModbusCommands.ReadInputRegisters;
            txData[2] = (byte)((regStartNo & 0xFF00) >> 8);
            txData[3] = (byte)(regStartNo & 0x00FF);
            txData[4] = (byte)((len & 0xFF00) >> 8);
            txData[5] = (byte)(len & 0x00FF);

            UInt16 crc = Crc16(txData, 6);
            txData[6] = (byte)(crc & 0x00FF);
            txData[7] = (byte)((crc & 0xFF00) >> 8);

            _comms.Write(txData, 0, READ_IR_TX_LEN);

            int expectedLen = READ_IR_RX_OVERHEAD + (len * 2);

            _log.DebugFormat("Reading {0} input registers from {1}@{2}", len, regStartNo, addr);
            var rxData = await CommsReceive(expectedLen);

            if ((rxData.Length != expectedLen)
                || (rxData[0] != addr)
                || (rxData[1] != (byte)ModbusCommands.ReadInputRegisters))
            {
                throw new ModbusException(rxData);
            }

            crc = Crc16(rxData, rxData[2] + 3);
            if (((crc & 0x00FF) != rxData[rxData[2] + 3])
                || (((crc & 0xFF00) >> 8) != rxData[rxData[2] + 4]))
            {
                _log.ErrorFormat("Expected CRC 0x{0:X4} received 0x{1:X2}{2:X2}", crc, rxData[rxData[2] + 4], rxData[rxData[2] + 3]);
                throw new ModbusException("CRC failure");
            }

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

            CommsPurge();

            byte[] txData = new byte[WRITE_COIL_TX_LEN];
            txData[0] = addr;
            txData[1] = (byte)ModbusCommands.WriteCoil;
            txData[2] = (byte)((coilNo & 0xFF00) >> 8);
            txData[3] = (byte)(coilNo & 0x00FF);
            txData[4] = (byte)(txCoil ? 0xff : 0x00);
            txData[5] = (byte)0x00;

            UInt16 crc = Crc16(txData, 6);
            txData[6] = (byte)(crc & 0x00FF);
            txData[7] = (byte)((crc & 0xFF00) >> 8);

            _log.DebugFormat("Writing coil to {0}@{1}", coilNo, addr);
            _comms.Write(txData, 0, WRITE_COIL_TX_LEN);

            var rxData = await CommsReceive(WRITE_COIL_RX_LEN);

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

            CommsPurge();

            byte[] txData = new byte[WRITE_HR_TX_LEN];
            txData[0] = addr;
            txData[1] = (byte)ModbusCommands.WriteHoldingRegister;
            txData[2] = (byte)((regNo & 0xFF00) >> 8);
            txData[3] = (byte)(regNo & 0x00FF);
            txData[4] = (byte)((txReg & 0xFF00) >> 8);
            txData[5] = (byte)(txReg & 0x00FF);

            UInt16 crc = Crc16(txData, 6);
            txData[6] = (byte)(crc & 0x00FF);
            txData[7] = (byte)((crc & 0xFF00) >> 8);
            _comms.Write(txData, 0, WRITE_HR_TX_LEN);

            _log.DebugFormat("Writing holiding register to {0}@{1}", regNo, addr);
            var rxData = await CommsReceive(WRITE_HR_RX_LEN);

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
            // TODO - make dynamic
            byte[] txData = new byte[250];
            int maxCoilWrite = (txData.Length - WRITE_COILS_TX_OVERHEAD) * 8;
	
	        //if ( ((coilStartNo + txCoils.Length - 1) > MAX_REG_NO) || (txCoils.Length > maxCoilWrite) )
            if (txCoils.Length > maxCoilWrite)
            {
                throw new ArgumentException("Length too long");
	        }

            CommsPurge();

            // Prepare the header
            txData[0] = addr;
            txData[1] = (byte)ModbusCommands.WriteCoils;
            txData[2] = (byte)((coilStartNo & 0xFF00) >> 8);
            txData[3] = (byte)(coilStartNo & 0x00FF);
            txData[4] = (byte)((txCoils.Length & 0xFF00) >> 8);
            txData[5] = (byte)(txCoils.Length & 0x00FF);
            // txData[6] to be filled with number of bytes after filling the buffer

            // Populate the txCoils on to the transmit buffer
            byte txLen = 7;
            txData[txLen] = 0;
            byte bitPos = 0;
            for (UInt16 i = 0; i < txCoils.Length; i++)
            {
                txData[txLen] <<= 1;
                if (txCoils[i])
                {
                    txData[txLen] |= 1;
                }

                if (++bitPos >= 8)
                {
                    bitPos = 0;
                    txLen++;
                    txData[txLen] = 0;
                }
            }

            txData[6] = (byte)(txLen - 6);

            UInt16 crc = Crc16(txData, txLen);
            txData[txLen++] = (byte)(crc & 0x00FF);
            txData[txLen++] = (byte)((crc & 0xFF00) >> 8);

            _log.DebugFormat("Writing {0} coils from {1}@{2}", txCoils.Length, coilStartNo, addr);
            _comms.Write(txData, 0, txLen);

            var rxData = await CommsReceive(WRITE_COILS_RX_LEN);

            if (rxData.Length != WRITE_COILS_RX_LEN)
            {
                throw new ModbusException(rxData);
            }

            // Verify echo-back (excl. CRC)
            for (int i = 0; i < rxData.Length - 2; i++)
            {
                if (txData[i] != rxData[i])
                {
                    _log.ErrorFormat("Read-back failed @ {0} - 0x{1:X2} != 0x{2:X2}", i, txData[i], rxData[i]);
                    throw new ModbusException("Echo-back verification failed");
                }
            }

            // Verify CRC
            crc = Crc16(rxData, rxData.Length - 2);
            if (((crc & 0x00FF) != rxData[rxData.Length - 2])
                || (((crc & 0xFF00) >> 8) != rxData[rxData.Length - 1]))
            {
                _log.ErrorFormat("Expected CRC 0x{0:X4} received 0x{1:X2}{2:X2}", crc, rxData[rxData.Length - 1], rxData[rxData.Length - 2]);
                throw new ModbusException("CRC fail");
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
            // TODO make this dynamic
            byte[] txData = new byte[250];
            int maxRegWrite = (txData.Length - WRITE_HRS_TX_OVERHEAD) / 2;

            if (//((regStartNo + txRegs.Length - 1) > MAX_REG_NO)
                (txRegs.Length == 0)
                || (txRegs.Length > maxRegWrite))
            {
                throw new ArgumentException("Bad length");
            }

            CommsPurge();

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
            UInt16 crc = Crc16(txData, txLen);
            txData[txLen++] = (byte)(crc & 0x00FF);
            txData[txLen++] = (byte)((crc & 0xFF00) >> 8);

            _log.DebugFormat("Writing {0} holiding registers from {1}@{2}", txRegs.Length, regStartNo, addr);
            _comms.Write(txData, 0, txLen);

            var rxData = await CommsReceive(WRITE_HRS_RX_LEN);

            if (rxData.Length != WRITE_HRS_RX_LEN)
            {
                throw new ModbusException(rxData);
            }

            // Verify echo-back (excl. CRC)
            for (int i = 0; i < WRITE_HRS_RX_LEN - 2; i++)
            {
                if (txData[i] != rxData[i])
                {
                    _log.ErrorFormat("Read-back failed @ {0} - 0x{1:X2} != 0x{2:X2}", i, txData[i], rxData[i]);
                    throw new ModbusException("Read-back failure");
                }
            }

            // Verify CRC
            crc = Crc16(rxData, rxData.Length - 2);
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
        /// <returns>Records read from file</returns>
        public async Task<byte[]> ReadFileRecord(byte addr, UInt16 fileNo, UInt16 recNo, UInt16 len)
        {
            byte[] txData = new byte[READ_FILE_RECORD_TX_LEN];

            if ((len % 2) > 0)
            {
                // Needs to be a whole number of UInt16
                len++;
            }

            CommsPurge();

            // Form the request
            txData[0] = addr;
            txData[1] = (byte)ModbusCommands.ReadFileRecords;
            txData[2] = (byte)READ_FILE_RECORD_SUBREC_REQ_LEN; // Sub-record length
            txData[3] = (byte)6;  // Reference type is always 6
            txData[4] = (byte)((fileNo & 0xFF00) >> 8);
            txData[5] = (byte)(fileNo & 0x00FF);
            txData[6] = (byte)((recNo & 0xFF00) >> 8);
            txData[7] = (byte)(recNo & 0x00FF);
            txData[8] = (byte)(((len / 2) & 0xFF00) >> 8);
            txData[9] = (byte)((len / 2) & 0x00FF);  // record length = byte length / 2

            UInt16 crc = Crc16(txData, 10);
            txData[10] = (byte)(crc & 0x00FF);
            txData[11] = (byte)((crc & 0xFF00) >> 8);

            // Send the request
            _log.DebugFormat("Writing {0} records to file number {1} from record number {2} on {3}", len / 2, fileNo, recNo, addr);
            _comms.Write(txData, 0, READ_FILE_RECORD_TX_LEN);

            // Get the response
            int expectedLen = (READ_FILE_RECORD_RX_OVERHEAD + len);
            var rxData = await CommsReceive(expectedLen);

            // Validate the response
            if ((rxData.Length < expectedLen) // may be 1 greater if odd number
                || (rxData[0] != addr)
                || (rxData[1] != (byte)ModbusCommands.ReadFileRecords))
            {
                throw new ModbusException(rxData);
            }

            crc = Crc16(rxData, rxData[2] + 3);
            if (((crc & 0x00FF) != rxData[rxData[2] + 3])
                || (((crc & 0xFF00) >> 8) != rxData[rxData[2] + 4]))
            {
                _log.ErrorFormat("Expected CRC 0x{0:X4} received 0x{1:X2}{2:X2}", crc, rxData[rxData[2] + 4], rxData[rxData[2] + 3]);
                throw new ModbusException("CRC failure");
            }

            // Copy the record data back to the passed buffer
            // TODO - verify record response length and reference type
            var rxRecs = new byte[len];
            Array.Copy(rxData, READ_FILE_RECORD_RX_OVERHEAD - 2, rxRecs, 0, len);

            _log.DebugFormat("Read {0} bytes", len);
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
            if ((txFileRecs.Length == 0) || (txFileRecs.Length > (255 - WRITE_FILE_RECORD_TX_OVERHEAD)))
            {
                throw new ArgumentException();
            }

            byte[] txData = new byte[WRITE_FILE_RECORD_TX_OVERHEAD + txFileRecs.Length];
            
            CommsPurge();

            // Form the request
            txData[0] = addr;
            txData[1] = (byte)ModbusCommands.WriteFileRecords;
            txData[2] = (byte)(WRITE_FILE_RECORD_SUBREC_LEN + txFileRecs.Length);
            txData[3] = (byte)6;  // Reference type is always 6
            txData[4] = (byte)((fileNo & 0xFF00) >> 8);
            txData[5] = (byte)(fileNo & 0x00FF);
            txData[6] = (byte)((recNo & 0xFF00) >> 8);
            txData[7] = (byte)(recNo & 0x00FF);
            txData[8] = (byte)(((txFileRecs.Length / 2) & 0xFF00) >> 8);
            txData[9] = (byte)((txFileRecs.Length / 2) & 0x00FF);  // record length = byte length / 2

            Array.Copy(txFileRecs, 0, txData, WRITE_FILE_RECORD_TX_OVERHEAD - 2, txFileRecs.Length);

            UInt16 crc = Crc16(txData, WRITE_FILE_RECORD_TX_OVERHEAD - 2 + txFileRecs.Length);
            txData[WRITE_FILE_RECORD_TX_OVERHEAD + txFileRecs.Length - 2] = (byte)(crc & 0x00FF);
            txData[WRITE_FILE_RECORD_TX_OVERHEAD + txFileRecs.Length - 1] = (byte)((crc & 0xFF00) >> 8);

            // Send the request
            _log.DebugFormat("Writing {0} records to file number {1} from record number {2} on {3}", txFileRecs.Length / 2, fileNo, recNo, addr);
            _comms.Write(txData, 0, WRITE_FILE_RECORD_TX_OVERHEAD + txFileRecs.Length);

            // Get the response
            var rxData = await CommsReceive(WRITE_FILE_RECORD_TX_OVERHEAD + txFileRecs.Length);

            // Validate the response
            if (rxData.Length != (WRITE_FILE_RECORD_TX_OVERHEAD + txFileRecs.Length))
            {
                throw new ModbusException(rxData);
            }

            // Verify echo-back (inc. CRC)
            for (int i = 0; i < (WRITE_FILE_RECORD_TX_OVERHEAD + txFileRecs.Length); i++)
            {
                if (txData[i] != rxData[i])
                {
                    _log.ErrorFormat("Read-back failed @ {0} - 0x{1:X2} != 0x{2:X2}", i, txData[i], rxData[i]);
                    throw new ModbusException("Read-back verification failed");
                }
            }

            _log.DebugFormat("Wrote {0} bytes", txFileRecs.Length);
        }

        static readonly UInt16[] CRC16_TABLE = new UInt16[]
        {
            (UInt16)0x0000, (UInt16)0xC0C1, (UInt16)0xC181, (UInt16)0x0140, (UInt16)0xC301, (UInt16)0x03C0, (UInt16)0x0280, (UInt16)0xC241,
            (UInt16)0xC601, (UInt16)0x06C0, (UInt16)0x0780, (UInt16)0xC741, (UInt16)0x0500, (UInt16)0xC5C1, (UInt16)0xC481, (UInt16)0x0440,
            (UInt16)0xCC01, (UInt16)0x0CC0, (UInt16)0x0D80, (UInt16)0xCD41, (UInt16)0x0F00, (UInt16)0xCFC1, (UInt16)0xCE81, (UInt16)0x0E40,
            (UInt16)0x0A00, (UInt16)0xCAC1, (UInt16)0xCB81, (UInt16)0x0B40, (UInt16)0xC901, (UInt16)0x09C0, (UInt16)0x0880, (UInt16)0xC841,
            (UInt16)0xD801, (UInt16)0x18C0, (UInt16)0x1980, (UInt16)0xD941, (UInt16)0x1B00, (UInt16)0xDBC1, (UInt16)0xDA81, (UInt16)0x1A40,
            (UInt16)0x1E00, (UInt16)0xDEC1, (UInt16)0xDF81, (UInt16)0x1F40, (UInt16)0xDD01, (UInt16)0x1DC0, (UInt16)0x1C80, (UInt16)0xDC41,
            (UInt16)0x1400, (UInt16)0xD4C1, (UInt16)0xD581, (UInt16)0x1540, (UInt16)0xD701, (UInt16)0x17C0, (UInt16)0x1680, (UInt16)0xD641,
            (UInt16)0xD201, (UInt16)0x12C0, (UInt16)0x1380, (UInt16)0xD341, (UInt16)0x1100, (UInt16)0xD1C1, (UInt16)0xD081, (UInt16)0x1040,
            (UInt16)0xF001, (UInt16)0x30C0, (UInt16)0x3180, (UInt16)0xF141, (UInt16)0x3300, (UInt16)0xF3C1, (UInt16)0xF281, (UInt16)0x3240,
            (UInt16)0x3600, (UInt16)0xF6C1, (UInt16)0xF781, (UInt16)0x3740, (UInt16)0xF501, (UInt16)0x35C0, (UInt16)0x3480, (UInt16)0xF441,
            (UInt16)0x3C00, (UInt16)0xFCC1, (UInt16)0xFD81, (UInt16)0x3D40, (UInt16)0xFF01, (UInt16)0x3FC0, (UInt16)0x3E80, (UInt16)0xFE41,
            (UInt16)0xFA01, (UInt16)0x3AC0, (UInt16)0x3B80, (UInt16)0xFB41, (UInt16)0x3900, (UInt16)0xF9C1, (UInt16)0xF881, (UInt16)0x3840,
            (UInt16)0x2800, (UInt16)0xE8C1, (UInt16)0xE981, (UInt16)0x2940, (UInt16)0xEB01, (UInt16)0x2BC0, (UInt16)0x2A80, (UInt16)0xEA41,
            (UInt16)0xEE01, (UInt16)0x2EC0, (UInt16)0x2F80, (UInt16)0xEF41, (UInt16)0x2D00, (UInt16)0xEDC1, (UInt16)0xEC81, (UInt16)0x2C40,
            (UInt16)0xE401, (UInt16)0x24C0, (UInt16)0x2580, (UInt16)0xE541, (UInt16)0x2700, (UInt16)0xE7C1, (UInt16)0xE681, (UInt16)0x2640,
            (UInt16)0x2200, (UInt16)0xE2C1, (UInt16)0xE381, (UInt16)0x2340, (UInt16)0xE101, (UInt16)0x21C0, (UInt16)0x2080, (UInt16)0xE041,
            (UInt16)0xA001, (UInt16)0x60C0, (UInt16)0x6180, (UInt16)0xA141, (UInt16)0x6300, (UInt16)0xA3C1, (UInt16)0xA281, (UInt16)0x6240,
            (UInt16)0x6600, (UInt16)0xA6C1, (UInt16)0xA781, (UInt16)0x6740, (UInt16)0xA501, (UInt16)0x65C0, (UInt16)0x6480, (UInt16)0xA441,
            (UInt16)0x6C00, (UInt16)0xACC1, (UInt16)0xAD81, (UInt16)0x6D40, (UInt16)0xAF01, (UInt16)0x6FC0, (UInt16)0x6E80, (UInt16)0xAE41,
            (UInt16)0xAA01, (UInt16)0x6AC0, (UInt16)0x6B80, (UInt16)0xAB41, (UInt16)0x6900, (UInt16)0xA9C1, (UInt16)0xA881, (UInt16)0x6840,
            (UInt16)0x7800, (UInt16)0xB8C1, (UInt16)0xB981, (UInt16)0x7940, (UInt16)0xBB01, (UInt16)0x7BC0, (UInt16)0x7A80, (UInt16)0xBA41,
            (UInt16)0xBE01, (UInt16)0x7EC0, (UInt16)0x7F80, (UInt16)0xBF41, (UInt16)0x7D00, (UInt16)0xBDC1, (UInt16)0xBC81, (UInt16)0x7C40,
            (UInt16)0xB401, (UInt16)0x74C0, (UInt16)0x7580, (UInt16)0xB541, (UInt16)0x7700, (UInt16)0xB7C1, (UInt16)0xB681, (UInt16)0x7640,
            (UInt16)0x7200, (UInt16)0xB2C1, (UInt16)0xB381, (UInt16)0x7340, (UInt16)0xB101, (UInt16)0x71C0, (UInt16)0x7080, (UInt16)0xB041,
            (UInt16)0x5000, (UInt16)0x90C1, (UInt16)0x9181, (UInt16)0x5140, (UInt16)0x9301, (UInt16)0x53C0, (UInt16)0x5280, (UInt16)0x9241,
            (UInt16)0x9601, (UInt16)0x56C0, (UInt16)0x5780, (UInt16)0x9741, (UInt16)0x5500, (UInt16)0x95C1, (UInt16)0x9481, (UInt16)0x5440,
            (UInt16)0x9C01, (UInt16)0x5CC0, (UInt16)0x5D80, (UInt16)0x9D41, (UInt16)0x5F00, (UInt16)0x9FC1, (UInt16)0x9E81, (UInt16)0x5E40,
            (UInt16)0x5A00, (UInt16)0x9AC1, (UInt16)0x9B81, (UInt16)0x5B40, (UInt16)0x9901, (UInt16)0x59C0, (UInt16)0x5880, (UInt16)0x9841,
            (UInt16)0x8801, (UInt16)0x48C0, (UInt16)0x4980, (UInt16)0x8941, (UInt16)0x4B00, (UInt16)0x8BC1, (UInt16)0x8A81, (UInt16)0x4A40,
            (UInt16)0x4E00, (UInt16)0x8EC1, (UInt16)0x8F81, (UInt16)0x4F40, (UInt16)0x8D01, (UInt16)0x4DC0, (UInt16)0x4C80, (UInt16)0x8C41,
            (UInt16)0x4400, (UInt16)0x84C1, (UInt16)0x8581, (UInt16)0x4540, (UInt16)0x8701, (UInt16)0x47C0, (UInt16)0x4680, (UInt16)0x8641,
            (UInt16)0x8201, (UInt16)0x42C0, (UInt16)0x4380, (UInt16)0x8341, (UInt16)0x4100, (UInt16)0x81C1, (UInt16)0x8081, (UInt16)0x4040
        };

        static public UInt16 Crc16(byte[] data, int len)
        {
            byte temp;
            UInt16 crc = 0xFFFF;
            int i = 0;
            while (len-- > 0)
            {
                temp = (byte)(data[i++] ^ crc);
                crc >>= 8;
                crc ^= CRC16_TABLE[temp];
            }
            return crc;
        }

    }
}
