using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;

namespace VVG.Modbus
{
    // TODO refactor to allow for other connection mechanisms other than RTU
    public class ClientRTU
    {
        // N.B. Address 0001 = register 0000 (address range 0001..9999)
        const UInt16 MAX_REG_NO = 9998;

        const byte READ_COILS_TX_LEN = 8;
        const byte READ_COILS_RX_OVERHEAD = 5;

        const byte READ_DI_TX_LEN = 8;
        const byte READ_DI_RX_OVERHEAD = 5;

        const byte READ_HR_TX_LEN = 8;
        const byte READ_HR_RX_OVERHEAD = 5;

        const byte READ_IR_TX_LEN = 8;
        const byte READ_IR_RX_OVERHEAD = 5;

        const byte WRITE_COIL_TX_LEN = 8;
        const byte WRITE_COIL_RX_LEN = 8; // command echoed back

        const byte WRITE_HR_TX_LEN = 8;
        const byte WRITE_HR_RX_LEN = 8;   // command echoed back

        const byte WRITE_COILS_TX_OVERHEAD = 9;
        const byte WRITE_COILS_RX_LEN = 8;

        const byte WRITE_HRS_TX_OVERHEAD = 9;
        const byte WRITE_HRS_RX_LEN = 8;

        const byte READ_FILE_RECORD_TX_LEN = 12;
        const byte READ_FILE_RECORD_RX_OVERHEAD = 7;

        const byte WRITE_FILE_RECORD_TX_OVERHEAD = 12;
        const byte WRITE_FILE_RECORD_RX_OVERHEAD = 12;    // command echoed back

        enum ModbusCommands
        {
            // No command 0
            MBCMD_READ_COILS = 1,
            MBCMD_READ_DI = 2,
            MBCMD_READ_HR = 3,
            MBCMD_READ_IR = 4,
            MBCMD_WRITE_COIL_SINGLE = 5,
            MBCMD_WRITE_HR_SINGLE = 6,
            MBCMD_READ_EXCEPTION_STATUS = 7, /// not implemented
            MBCMD_DIAGNOSTIC = 8, /// not implemented
            // No command 9
            // No command 10
            MBCMD_GET_COM_EVT_CTR = 11, /// not implemented
            MBCMD_GET_COM_EVT_LOG = 12, /// not implemented
            // No command 13
            // No command 14
            MBCMD_WRITE_COIL_MULTIPLE = 15,
            MBCMD_WRITE_HR_MULTIPLE = 16,
            MBCMD_REPORT_SLAVE_ID = 17, /// not implemented
            // No command 18
            // No command 19
            MBCMD_READ_FILE_RECORD = 20,
            MBCMD_WRITE_FILE_RECORD = 21,
            MBCMD_MASK_WRITE_REG = 22,
            MBCMD_READ_WRITE_MULTI_REG = 23,
            MBCMD_READ_FIFO_Q = 24,
            // No commands 25..42
            MBCMD_READ_DEV_ID = 43
        }
	
	    enum ModbusExceptions
        {
            MBEX_ILLEGAL_FN = 1,
            MBEX_ILLEGAL_ADDR,
            MBEX_ILLEGAL_VALUE,
            MBEX_SLAVE_DEV_FAIL,
            MBEX_ACK,
            MBEX_SLAVE_DEV_BUSY,
            MBEX_NAK,
            MBEX_MEM_PARITY_ERR,
            // No exception 9
            MBEX_GATEWAY_UNAVAILABLE = 10,
            MBEX_GATEWAY_TARGET_DEV_FAILED_TO_RESPOND
        }

        readonly SerialPort _comms;

        public ClientRTU(SerialPort port)
        {
            _comms = port;
        }

        public bool ReadCoil(byte addr, UInt16 coilNo)
        {
            return ReadCoils(addr, coilNo, 1)[0];
        }

        public bool[] ReadCoils(byte addr, UInt16 coilStartNo, UInt16 len)
        {
            byte[] rxBuf = new byte[32];
            UInt16 maxCoilRead = (UInt16)((rxBuf.Length - READ_COILS_RX_OVERHEAD) * 8);

            if (((coilStartNo + len) > MAX_REG_NO) || (len > maxCoilRead))
            {
                throw new ArgumentException();
            }

            lock (_comms)
            {
                _comms.ReadExisting();

                byte[] txData = new byte[READ_COILS_TX_LEN];
                txData[0] = addr;
                txData[1] = (byte)ModbusCommands.MBCMD_READ_COILS;
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

                int rcvLen = _comms.Read(rxBuf, 0, expectedLen);

                // Check the length and header
                if ((rcvLen != expectedLen)
                    || (rxBuf[0] != addr)
                    || (rxBuf[1] != (byte)ModbusCommands.MBCMD_READ_COILS)
                    || (rxBuf[2] != expectedDataCount))
                {
                    throw new ModbusException(rxBuf, rcvLen);
                }

                // Check the CRC
                crc = Crc16(rxBuf, expectedDataCount + 3);
                if (((crc & 0x00FF) != rxBuf[rxBuf[2] + 3])
                    || (((crc & 0xFF00) >> 8) != rxBuf[rxBuf[2] + 4]))
                {
                    throw new ModbusException("CRC failure");
                }

                // Populate the return buffer
                byte bufferIdx = 3;
                byte bitPos = 0;
                bool[] rxCoils = new bool[len];
                for (UInt16 i = 0; i < len; i++)
                {
                    byte bitMask = (byte)(1 << bitPos);
                    rxCoils[i] = (rxBuf[bufferIdx] & bitMask) != 0;
                    if (++bitPos >= 8)
                    {
                        bitPos = 0;
                        bufferIdx++;
                    }
                }

                return rxCoils;
            }
        }

        public bool ReadDiscreteInput(byte addr, UInt16 inputNo)
        {
            return ReadDiscreteInputs(addr, inputNo, 1)[0];
        }

        /**
         *	Command 2 - Read Discrete Inputs
         */
        public bool[] ReadDiscreteInputs(byte addr, UInt16 inputStartNo, UInt16 len)
        {
            byte[] rxBuf = new byte[32];
            int maxCoilRead = (rxBuf.Length - READ_DI_RX_OVERHEAD) * 8;

            if (((inputStartNo + len) > MAX_REG_NO)
                || (len > maxCoilRead))
            {
                throw new ArgumentException();
            }

            lock (_comms)
            {
                _comms.ReadExisting();

                byte[] txData = new byte[READ_DI_TX_LEN];
                txData[0] = addr;
                txData[1] = (byte)ModbusCommands.MBCMD_READ_DI;
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

                int rcvLen = _comms.Read(rxBuf, 0, expectedLen);

                // Check the length and header
                if ((rcvLen != expectedLen)
                    || (rxBuf[0] != addr)
                    || (rxBuf[1] != (byte)ModbusCommands.MBCMD_READ_DI)
                    || (rxBuf[2] != expectedDataCount))
                {
                    throw new ModbusException(rxBuf, rcvLen);
                }

                // Check the CRC
                crc = Crc16(rxBuf, expectedDataCount + 3);
                if (((crc & 0x00FF) != rxBuf[rxBuf[2] + 3])
                    || (((crc & 0xFF00) >> 8) != rxBuf[rxBuf[2] + 4]))
                {
                    throw new ModbusException("CRC failure");
                }

                // Populate the return buffer
                byte bufferIdx = 3;
                byte bitPos = 0;
                bool[] rxInputs = new bool[len];
                for (UInt16 i = 0; i < len; i++)
                {
                    byte bitMask = (byte)(1 << bitPos);
                    rxInputs[i] = (rxBuf[bufferIdx] & bitMask) != 0;
                    if (++bitPos >= 8)
                    {
                        bitPos = 0;
                        bufferIdx++;
                    }
                }

                return rxInputs;
            }
        }

        public UInt16 ReadHoldingReg(byte addr, UInt16 regNo)
        {
            return ReadHoldingRegs(addr, regNo, 1)[0];
        }

        /**
         *	Command 3 - Read Holding Registers
         */
        public UInt16[] ReadHoldingRegs(byte addr, UInt16 regStartNo, UInt16 len)
        {
            byte[] rxBuf = new byte[64];
            int maxRegsRead = (rxBuf.Length - READ_HR_RX_OVERHEAD) / 2;

            if (((regStartNo + len) > MAX_REG_NO)
                || (len > maxRegsRead))
            {
                throw new ArgumentException();
            }

            lock (_comms)
            {
                _comms.ReadExisting();

                byte[] txData = new byte[READ_HR_TX_LEN];
                txData[0] = addr;
                txData[1] = (byte)ModbusCommands.MBCMD_READ_HR;
                txData[2] = (byte)((regStartNo & 0xFF00) >> 8);
                txData[3] = (byte)(regStartNo & 0x00FF);
                txData[4] = (byte)((len & 0xFF00) >> 8);
                txData[5] = (byte)(len & 0x00FF);

                UInt16 crc = Crc16(txData, 6);
                txData[6] = (byte)(crc & 0x00FF);
                txData[7] = (byte)((crc & 0xFF00) >> 8);

                _comms.Write(txData, 0, READ_HR_TX_LEN);

                int expectedLen = READ_HR_RX_OVERHEAD + (len * 2);
                int rcvLen = _comms.Read(rxBuf, 0, expectedLen);

                if ((rcvLen != expectedLen)
                    || (rxBuf[0] != addr)
                    || (rxBuf[1] != (byte)ModbusCommands.MBCMD_READ_HR))
                {
                    throw new ModbusException(rxBuf, rcvLen);
                }

                crc = Crc16(rxBuf, rxBuf[2] + 3);
                if (((crc & 0x00FF) != rxBuf[rxBuf[2] + 3])
                    || (((crc & 0xFF00) >> 8) != rxBuf[rxBuf[2] + 4]))
                {
                    throw new ModbusException("CRC failure");
                }

                UInt16[] rxRegs = new UInt16[len];
                for (UInt16 i = 0; i < len; i++)
                {
                    UInt16 reg = (UInt16)(((UInt16)rxBuf[3 + i * 2] << 8) + rxBuf[4 + i * 2]);
                    rxRegs[i] = reg;
                }

                return rxRegs;
            }
        }

        public UInt16 ReadInputReg(byte addr, UInt16 regNo)
        {
            return ReadInputRegs(addr, regNo, 1)[0];
        }

        /**
         *	Command 4 - Read Input Registers
         */
        public UInt16[] ReadInputRegs(byte addr, UInt16 regStartNo, UInt16 len)
        {
            byte[] rxBuf = new byte[64];
            int maxRegsRead = (rxBuf.Length - READ_IR_RX_OVERHEAD) / 2;

            if ((regStartNo > MAX_REG_NO)
                || (len > maxRegsRead))
            {
                throw new ArgumentException();
            }

            lock (_comms)
            {
                _comms.ReadExisting();

                byte[] txData = new byte[READ_IR_TX_LEN];
                txData[0] = addr;
                txData[1] = (byte)ModbusCommands.MBCMD_READ_IR;
                txData[2] = (byte)((regStartNo & 0xFF00) >> 8);
                txData[3] = (byte)(regStartNo & 0x00FF);
                txData[4] = (byte)((len & 0xFF00) >> 8);
                txData[5] = (byte)(len & 0x00FF);

                UInt16 crc = Crc16(txData, 6);
                txData[6] = (byte)(crc & 0x00FF);
                txData[7] = (byte)((crc & 0xFF00) >> 8);

                _comms.Write(txData, 0, READ_IR_TX_LEN);

                int expectedLen = READ_IR_RX_OVERHEAD + (len * 2);
                int rcvLen = _comms.Read(rxBuf, 0, expectedLen);

                if ((rcvLen != expectedLen)
                    || (rxBuf[0] != addr)
                    || (rxBuf[1] != (byte)ModbusCommands.MBCMD_READ_IR))
                {
                    throw new ModbusException(rxBuf, rcvLen);
                }

                crc = Crc16(rxBuf, rxBuf[2] + 3);
                if (((crc & 0x00FF) != rxBuf[rxBuf[2] + 3])
                    || (((crc & 0xFF00) >> 8) != rxBuf[rxBuf[2] + 4]))
                {
                    throw new ModbusException("CRC failure");
                }

                UInt16[] rxRegs = new UInt16[len];
                for (UInt16 i = 0; i < len; i++)
                {
                    UInt16 reg = (UInt16)(((UInt16)rxBuf[3 + i * 2] << 8) + rxBuf[4 + i * 2]);
                    rxRegs[i] = reg;
                }

                return rxRegs;
            }
        }

        /**
         *	Command 5 - Write Single Coil
         */
        public void WriteCoil(byte addr, UInt16 coilNo, bool txCoil)
        {
            if (coilNo > MAX_REG_NO)
            {
                throw new ArgumentException();
            }

            lock (_comms)
            {
                _comms.ReadExisting();

                byte[] txData = new byte[WRITE_COIL_TX_LEN];
                txData[0] = addr;
                txData[1] = (byte)ModbusCommands.MBCMD_WRITE_COIL_SINGLE;
                txData[2] = (byte)((coilNo & 0xFF00) >> 8);
                txData[3] = (byte)(coilNo & 0x00FF);
                txData[4] = (byte)(txCoil ? 0xff : 0x00);
                txData[5] = (byte)0x00;

                UInt16 crc = Crc16(txData, 6);
                txData[6] = (byte)(crc & 0x00FF);
                txData[7] = (byte)((crc & 0xFF00) >> 8);
                _comms.Write(txData, 0, WRITE_COIL_TX_LEN);

                byte[] rxBuf = new byte[WRITE_COIL_RX_LEN];
                int rcvLen = _comms.Read(rxBuf, 0, WRITE_COIL_RX_LEN);

                if (rcvLen != WRITE_COIL_RX_LEN)
                {
                    throw new ModbusException(rxBuf, rcvLen);
                }

                // check the command was echoed back as sent
                for (UInt16 i = 0; i < WRITE_COIL_RX_LEN; i++)
                {
                    if (txData[i] != rxBuf[i])
                    {
                        // Mismatch - fail :(
                        throw new ModbusException("Failed to validate response");
                    }
                }

                // All data matched - success!
            }
        }

        /**
         *	Command 6 - Write Single Holding Register
         */
        public void WriteHoldingReg(byte addr, UInt16 regNo, UInt16 txReg)
        {
            if (regNo > MAX_REG_NO)
            {
                throw new ArgumentException();
            }

            lock (_comms)
            {
                _comms.ReadExisting();

                byte[] txData = new byte[WRITE_HR_TX_LEN];
                txData[0] = addr;
                txData[1] = (byte)ModbusCommands.MBCMD_WRITE_HR_SINGLE;
                txData[2] = (byte)((regNo & 0xFF00) >> 8);
                txData[3] = (byte)(regNo & 0x00FF);
                txData[4] = (byte)((txReg & 0xFF00) >> 8);
                txData[5] = (byte)(txReg & 0x00FF);

                UInt16 crc = Crc16(txData, 6);
                txData[6] = (byte)(crc & 0x00FF);
                txData[7] = (byte)((crc & 0xFF00) >> 8);
                _comms.Write(txData, 0, WRITE_HR_TX_LEN);

                byte[] rxBuf = new byte[WRITE_HR_RX_LEN];
                int rcvLen = _comms.Read(rxBuf, 0, WRITE_HR_RX_LEN);

                if (rcvLen != rxBuf.Length)
                {
                    throw new ModbusException(rxBuf, rcvLen);
                }

                // check the command was echoed back as sent
                for (UInt16 i = 0; i < rxBuf.Length; i++)
                {
                    if (txData[i] != rxBuf[i])
                    {
                        // Mismatch - fail :(
                        throw new ModbusException("Failed to validate response");
                    }
                }

                // All data matched - success!
            }
        }

        /**
         *	Command 15 - Write Coils (multiple)
         */
        public void WriteCoils(byte addr, UInt16 coilStartNo, bool[] txCoils)
        {
            byte[] txData = new byte[32];
            int maxCoilWrite = (txData.Length - WRITE_COILS_TX_OVERHEAD) * 8;
	
	        if (	((coilStartNo + txCoils.Length) > MAX_REG_NO)
		        ||	(txCoils.Length > maxCoilWrite) )
	        {
                throw new ArgumentException();
	        }

            lock (_comms)
            {
                _comms.ReadExisting();

                // Prepare the header
                txData[0] = addr;
                txData[1] = (byte)ModbusCommands.MBCMD_WRITE_COIL_MULTIPLE;
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

                // If the final byte is partially filled, add it to the length
                if (bitPos > 0)
                {
                    txLen++;
                }
                txData[6] = (byte)(txLen - 6);

                UInt16 crc = Crc16(txData, txLen);
                txData[txLen++] = (byte)(crc & 0x00FF);
                txData[txLen++] = (byte)((crc & 0xFF00) >> 8);
                _comms.Write(txData, 0, txLen);

                byte[] rxBuf = new byte[WRITE_COILS_RX_LEN];
                int rcvLen = _comms.Read(rxBuf, 0, WRITE_COILS_RX_LEN);

                if (rcvLen != rxBuf.Length)
                {
                    throw new ModbusException(rxBuf, rcvLen);
                }

                // Verify echo-back (excl. CRC)
                for (int i = 0; i < rxBuf.Length - 2; i++)
                {
                    if (txData[i] != rxBuf[i])
                    {
                        throw new ModbusException("Echo-back verification failed");
                    }
                }

                // Verify CRC
                crc = Crc16(rxBuf, rxBuf.Length - 2);
                if (((crc & 0x00FF) != rxBuf[rxBuf.Length - 2])
                    || (((crc & 0xFF00) >> 8) != rxBuf[rxBuf.Length - 1]))
                {
                    throw new ModbusException("CRC fail");
                }
            }
        }

        /**
         *	Command 16 - Write Holding Registers (multiple)
         */
        public void WriteHoldingRegs(byte addr, UInt16 regStartNo, UInt16[] txRegs)
        {
            // TODO make this dynamic
            byte[] txData = new byte[64];
            int maxRegWrite = (txData.Length - WRITE_HRS_TX_OVERHEAD) / 2;

            if (((regStartNo + txRegs.Length) > MAX_REG_NO)
                || (txRegs.Length == 0)
                || (txRegs.Length > maxRegWrite))
            {
                throw new ArgumentException();
            }

            lock (_comms)
            {
                _comms.ReadExisting();

                // Prepare the header
                txData[0] = addr;
                txData[1] = (byte)ModbusCommands.MBCMD_WRITE_HR_MULTIPLE;
                txData[2] = (byte)((regStartNo & 0xFF00) >> 8);
                txData[3] = (byte)(regStartNo & 0x00FF);
                txData[4] = (byte)((txRegs.Length & 0xFF00) >> 8);
                txData[5] = (byte)(txRegs.Length & 0x00FF);
                txData[6] = (byte)(txRegs.Length * 2);  // number of *bytes*

                // Populate the txCoils on to the transmit buffer
                for (UInt16 i = 0; i < txRegs.Length; i++)
                {
                    txData[6 + (i * 2)] = (byte)((txRegs[i] & 0xFF00) >> 8);
                    txData[7 + (i * 2)] = (byte)(txRegs[i] & 0x00FF);
                }

                int txLen = WRITE_HRS_TX_OVERHEAD - 2 + (txRegs.Length * 2);
                UInt16 crc = Crc16(txData, txLen);
                txData[txLen++] = (byte)(crc & 0x00FF);
                txData[txLen++] = (byte)((crc & 0xFF00) >> 8);
                _comms.Write(txData, 0, txLen);

                byte[] rxBuf = new byte[WRITE_HRS_RX_LEN];
                int rcvLen = _comms.Read(rxBuf, 0, WRITE_HRS_RX_LEN);

                if (rcvLen != rxBuf.Length)
                {
                    throw new ModbusException(rxBuf, rcvLen);
                }

                // Verify echo-back (excl. CRC)
                for (int i = 0; i < WRITE_HRS_RX_LEN - 2; i++)
                {
                    if (txData[i] != rxBuf[i])
                    {
                        throw new ModbusException("Read-back failure");
                    }
                }

                // Verify CRC
                crc = Crc16(rxBuf, rxBuf.Length - 2);
                if (((crc & 0x00FF) != rxBuf[rxBuf.Length - 2])
                    || (((crc & 0xFF00) >> 8) != rxBuf[rxBuf.Length - 1]))
                {
                    throw new ModbusException("CRC failure");
                }
            }
        }

        /**
         *	Command 20 - Read File Record(s)
         *
         *	@param[in]	addr	Address of the slave
         *	@param[in]	fileNo	File number to read from
         *	@param[in]	recNo	Record number (uint16 offset) in the file
         *	@param[in]	len		Length (in bytes) to read
         *	@param[out]	txFileRecs	Records read from file
         */
        public byte[] ReadFileRecord(byte addr, UInt16 fileNo, UInt16 recNo, UInt16 len)
        {
            byte[] txData = new byte[READ_FILE_RECORD_TX_LEN];
            byte[] rxData = new byte[READ_FILE_RECORD_RX_OVERHEAD + len];

            lock (_comms)
            {
                _comms.ReadExisting();

                // Form the request
                txData[0] = addr;
                txData[1] = (byte)ModbusCommands.MBCMD_READ_FILE_RECORD;
                txData[2] = (byte)(READ_FILE_RECORD_TX_LEN - 3 + len);
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
                _comms.Write(txData, 0, READ_FILE_RECORD_TX_LEN);

                // Get the response
                int expectedLen = (READ_FILE_RECORD_RX_OVERHEAD + len);
                int rcvLen = _comms.Read(rxData, 0, expectedLen);

                // Validate the response
                if ((rcvLen != expectedLen)
                    || (rxData[0] != addr)
                    || (rxData[1] != (byte)ModbusCommands.MBCMD_READ_FILE_RECORD))
                {
                    throw new ModbusException(rxData, rcvLen);
                }

                crc = Crc16(rxData, rxData[2] + 3);
                if (((crc & 0x00FF) != rxData[rxData[2] + 3])
                    || (((crc & 0xFF00) >> 8) != rxData[rxData[2] + 4]))
                {
                    throw new ModbusException("CRC failure");
                }

                // Copy the record data back to the passed buffer
                var rxRecs = new byte[len];
                Array.Copy(rxData, READ_FILE_RECORD_RX_OVERHEAD - 2, rxRecs, 0, len);

                return rxRecs;
            }
        }

        /**
         *	Command 21 - Write File Record(s)
         *
         *	@param[in]	addr	Address of the slave
         *	@param[in]	fileNo	File number to write to
         *	@param[in]	recNo	Record number (uint16 offset) in the file
         *	@param[in]	len		Length (in bytes) to write
         *	@param[in]	txFileRecs	Records to write to file
         */
        public void WriteFileRecord(byte addr, UInt16 fileNo, UInt16 recNo, byte[] txFileRecs)
        {
            if ((txFileRecs.Length == 0) || (txFileRecs.Length > (255 - WRITE_FILE_RECORD_TX_OVERHEAD)))
            {
                throw new ArgumentException();
            }

            byte[] txData = new byte[WRITE_FILE_RECORD_TX_OVERHEAD + txFileRecs.Length];
            byte[] rxData = new byte[WRITE_FILE_RECORD_RX_OVERHEAD + txFileRecs.Length];
            
            lock (_comms)
            {
                _comms.ReadExisting();

                // Form the request
                txData[0] = addr;
                txData[1] = (byte)ModbusCommands.MBCMD_WRITE_FILE_RECORD;
                txData[2] = (byte)(WRITE_FILE_RECORD_TX_OVERHEAD - 3 + txFileRecs.Length);
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
                _comms.Write(txData, 0, WRITE_FILE_RECORD_TX_OVERHEAD + txFileRecs.Length);

                // Get the response
                int rcvLen = _comms.Read(rxData, 0, (WRITE_FILE_RECORD_TX_OVERHEAD + txFileRecs.Length));

                // Validate the response
                if (rcvLen != (WRITE_FILE_RECORD_TX_OVERHEAD + txFileRecs.Length))
                {
                    throw new ModbusException(rxData, rcvLen);
                }

                // Verify echo-back (inc. CRC)
                for (int i = 0; i < (WRITE_FILE_RECORD_TX_OVERHEAD + txFileRecs.Length); i++)
                {
                    if (txData[i] != rxData[i])
                    {
                        throw new ModbusException("Read-back verification failed");
                    }
                }
            }
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

        static UInt16 Crc16(byte[] data, int len)
        {
            byte temp;
            UInt16 crc = 0xFFFF;
            int i = 0;
            while (len-- > 0)
            {
                temp = (byte)(data[i] ^ crc);
                crc >>= 8;
                crc ^= CRC16_TABLE[temp];
            }
            return crc;
        }

    }
}
