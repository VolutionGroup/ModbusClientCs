using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO.Ports;
using Microsoft.Win32;
using System.IO;
using log4net;
using System.Net.Sockets;

namespace VVG.Modbus.ClientTest
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly ILog _log = LogManager.GetLogger(typeof(MainWindow));
        private ClientSlave _slave = new ClientSlave();
        private SerialPort _serialPort = null;
        private TcpClient _tcpClient = null;

        private List<Coil> _coils = new List<Coil>();
        private List<DiscreteInput> _discreteInputs = new List<DiscreteInput>();
        private List<HoldingRegister> _holdingRegisters = new List<HoldingRegister>();
        private List<InputRegister> _inputRegisters = new List<InputRegister>();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshComPorts();
            UpdateProgress(0.0f);

            UpdateCoils(null, 0);
            UpdateDiscreteInputs(null, 0);
            UpdateHoldingRegisters(null, 0);
            UpdateInputRegisters(null, 0);

            Title += String.Format(" - V{0}", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
        }

        #region Update form helpers
        private void RefreshComPorts()
        {
            cboSerialPort.Items.Clear();
            var ports = SerialPort.GetPortNames().ToList();
            ports.Sort();
            foreach (var port in ports)
            {
                cboSerialPort.Items.Add(port);
            }
            cboSerialPort.SelectedIndex = 0;

            cboParity.Items.Clear();
            foreach (var parity in Enum.GetNames(typeof(Parity)))
            {
                cboParity.Items.Add(parity);
            }
            cboParity.SelectedIndex = 0;
        }

        private void UpdateCoils(IEnumerable<bool> coils, UInt16 start)
        {
            _coils.Clear();

            if ((coils != null) && (coils.Count() > 0))
            {
                int i = 0;
                foreach (var coil in coils)
                {
                    _coils.Add(new Coil((UInt16)(start + i), coil));
                    i++;
                }
            }

            dgCoils.ItemsSource = _coils;
            dgCoils.Items.Refresh();
        }

        private void UpdateDiscreteInputs(IEnumerable<bool> discreteInputs, UInt16 start)
        {
            _discreteInputs.Clear();

            if ((discreteInputs != null) && (discreteInputs.Count() > 0))
            {
                int i = 0;
                foreach (var di in discreteInputs)
                {
                    _discreteInputs.Add(new DiscreteInput((UInt16)(start + i), di));
                    i++;
                }
            }

            dgDiscreteInputs.ItemsSource = _discreteInputs;
            dgDiscreteInputs.Items.Refresh();
        }

        private void UpdateHoldingRegisters(IEnumerable<UInt16> holdingRegisters, UInt16 start)
        {
            _holdingRegisters.Clear();

            if ((holdingRegisters != null) && (holdingRegisters.Count() > 0))
            {
                int i = 0;
                foreach (var hr in holdingRegisters)
                {
                    _holdingRegisters.Add(new HoldingRegister((UInt16)(start + i), hr));
                    i++;
                }
            }

            dgHoldingRegisters.ItemsSource = _holdingRegisters;
            dgHoldingRegisters.Items.Refresh();
        }

        private void UpdateInputRegisters(IEnumerable<UInt16> inputRegisters, UInt16 start)
        {
            _inputRegisters.Clear();

            if ((inputRegisters != null) && (inputRegisters.Count() > 0))
            {
                int i = 0;
                foreach (var ir in inputRegisters)
                {
                    _inputRegisters.Add(new InputRegister((UInt16)(start + i), ir));
                    i++;
                }
            }

            dgInputRegisters.ItemsSource = _inputRegisters;
            dgInputRegisters.Items.Refresh();
        }

        public void UpdateProgress(float percent)
        {
            if (percent < 0.0f)
            {
                percent = 0.0f;
            }
            else if (percent > 100.0f)
            {
                percent = 100.0f;
            }

            Dispatcher.BeginInvoke(new Action(() => 
            {
                lblProgress.Content = percent.ToString("0.0") + '%';
                progBar.Value = percent;
            }));
        }

        private void txtSlaveID_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = (TextBox)sender;
            byte slaveId;
            if (byte.TryParse(tb.Text, out slaveId))
            {
                tb.Background = Brushes.LightGreen;
                _slave.Address = slaveId;
            }
            else
            {
                tb.Background = Brushes.LightSalmon;
            }
        }

        private void UpdateConnected()
        {
            bool serialOpen = ((_serialPort != null) && (_serialPort.IsOpen));
            bool tcpOpen = ((_tcpClient != null) && (_tcpClient.Connected));
            
            lblStatus.Content = (serialOpen || tcpOpen) ? "Connected" : "Disconnected";
            cmdDisconnect.IsEnabled = serialOpen;
            cmdConnect.IsEnabled = !(serialOpen || tcpOpen);
            tabs.IsEnabled = (serialOpen || tcpOpen);
            txtBaudRate.IsEnabled = !serialOpen;
            cboParity.IsEnabled = !serialOpen;
            txtSlaveID.IsEnabled = !(serialOpen || tcpOpen);
            cmdDisconnectTCP.IsEnabled = tcpOpen;
            cmdConnectTCP.IsEnabled = !(serialOpen || tcpOpen);
        }
        #endregion

        private void cmdConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _serialPort = new SerialPort((string)cboSerialPort.SelectedItem)
                {
                    BaudRate = int.Parse(txtBaudRate.Text),
                    Parity = (Parity)Enum.Parse(typeof(Parity), (string)cboParity.SelectedItem)
                };
                _serialPort.Open();
                _slave.Client = new ClientRTU(_serialPort);

                _log.InfoFormat("Connected to {0} @ {1}/{2}", cboSerialPort.SelectedItem, txtBaudRate.Text, cboParity.SelectedItem);
                UpdateConnected();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to open serial port: " + ex.Message, "Connection failure");
            }
        }

        private void cmdDisconnect_Click(object sender, RoutedEventArgs e)
        {
            _serialPort.Close();
            _serialPort = null;
            _slave.Client = null;

            _log.Info("COM port disconnected");
            UpdateConnected();
        }

        private void cmdBrowseFile_Click(object sender, RoutedEventArgs e)
        {
            var dlgOpenFile = new OpenFileDialog();
            if (true == dlgOpenFile.ShowDialog())
            {
                if (File.Exists(dlgOpenFile.FileName))
                {
                    txtFilename.Text = dlgOpenFile.FileName;
                    var fileLength = new FileInfo(dlgOpenFile.FileName).Length;
                    txtLen.Text = fileLength.ToString();
                }
            }
        }

        private async void cmdReadFile_Click(object sender, RoutedEventArgs e)
        {
            UInt16 fileNum, recNum;
            int len;
            if (false == UInt16.TryParse(txtFileNum.Text, out fileNum))
            {
                MessageBox.Show("Failed to parse File Number", "Fail");
                return;
            }
            if (false == UInt16.TryParse(txtRecNum.Text, out recNum))
            {
                MessageBox.Show("Failed to parse Record Number", "Fail");
                return;
            }
            if (false == int.TryParse(txtLen.Text, out len))
            {
                MessageBox.Show("Failed to parse Length (bytes)", "Fail");
                return;
            }
            if (len > UInt16.MaxValue * 2)
            {
                MessageBox.Show("Length exceeds maximum of " + (UInt16.MaxValue * 2) + " bytes", "Fail");
                return;
            }

            if (File.Exists(txtFilename.Text))
            {
                if (MessageBox.Show("File already exists - do you want to overwrite?", "File exists", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            _log.InfoFormat("Beginning File {0} read from record {1}, of {2} bytes", fileNum, recNum, len);

            var readFile = new byte[len];
            int retries = 0;
            
            for (int i = 0; i < len;)
            {
                int remaining = len - i;

                // Limit to 128 bytes per request
                UInt16 thisLen = (UInt16)((remaining > 128) ? 128 : remaining);

                byte[] readRecs;
                try
                {
                    readRecs = await _slave.ReadFileRecord(fileNum, recNum, thisLen);
                }
                catch (Exception ex)
                {
                    _log.ErrorFormat("Read request failed at {0}/{1} (attempt {2}) - exception {3}", i, len, retries, ex);
                    if (++retries > 5)
                    {
                        MessageBox.Show(String.Format("Failed to read file (retries exhausted).\n\nProgress {0}/{1} bytes.\n\nLast exception: {2}", i, len, ex), "Fail");
                        return;
                    }
                    continue;
                }

                if (readRecs.Length == thisLen)
                {
                    Array.Copy(readRecs, 0, readFile, i, thisLen);
                    i += thisLen;
                    recNum += (UInt16)(thisLen / 2);    // Records are multiples of UInt16
                    retries = 0;
                }

                UpdateProgress(100 * i / len);
            }

            _log.Info("File read complete");
            
            try
            {
                File.WriteAllBytes(txtFilename.Text, readFile);
                MessageBox.Show("File received and saved OK", "Success");
            }
            catch (Exception ex)
            {
                _log.Error("Failed to write to file", ex);
                MessageBox.Show("Modbus transfer successful but failed to write file locally: " + ex.Message, "Fail");
            }
        }

        private async void cmdWriteFile_Click(object sender, RoutedEventArgs e)
        {
            UInt16 fileNum, ui16;
            int len, recNum; // need to be > UInt16 now we're allowing for roll-over
            if (false == UInt16.TryParse(txtFileNum.Text, out fileNum))
            {
                MessageBox.Show("Failed to parse File Number", "Fail");
                return;
            }
            if (UInt16.TryParse(txtRecNum.Text, out ui16))
            {
                recNum = ui16;
            }
            else
            {
                MessageBox.Show("Failed to parse Record Number", "Fail");
                return;
            }
            if (false == int.TryParse(txtLen.Text, out len))
            {
                MessageBox.Show("Failed to parse Length (bytes)", "Fail");
                return;
            }
            
            if (false == File.Exists(txtFilename.Text))
            {
                MessageBox.Show("File does not exist - cannot send file", "Fail");
                return;
            }

            byte[] fileBytes = File.ReadAllBytes(txtFilename.Text);
            
            if (fileBytes.Length < len)
            {
                if (MessageBox.Show("Insufficient bytes in file for requested send size - update to match file size?", "Size mis-match", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }
                len = (UInt16)fileBytes.Length;
                await Dispatcher.BeginInvoke(new Action(() => txtLen.Text = len.ToString()));
            }
            if (len > UInt16.MaxValue * 2)
            {
                // TODO - also take into account starting offset
                int split = len / (UInt16.MaxValue * 2);
                if (len % (UInt16.MaxValue * 2) > 0)
                {
                    split++;
                }
                if (MessageBox.Show(String.Format("Length exceeds maximum of {0} bytes\n\nSplit over {1} file IDs?", UInt16.MaxValue * 2, split),
                    "That's larger than expected", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            _log.InfoFormat("Beginning File {0} write from record {1}, of {2} bytes", fileNum, recNum, len);

            int retries = 0;

            for (int i = 0; i < len;)
            {
                UpdateProgress(100.0f * i / len);

                int remaining = (len - i);

                // Limit to 128 bytes per request
                int thisWriteLen = (remaining > 128) ? 128 : remaining;
                if (recNum + (thisWriteLen / 2) > UInt16.MaxValue)
                {
                    thisWriteLen = (UInt16.MaxValue + 1 - recNum) * 2; // limit to max record size
                    _log.InfoFormat("Restricting write to {0} bytes to avoid overlap to next FileID", thisWriteLen);
                }
                var writeRecs = new byte[thisWriteLen];
                Array.Copy(fileBytes, i, writeRecs, 0, writeRecs.Length);
                try
                {
                    await _slave.WriteFileRecord(fileNum, (UInt16)recNum, writeRecs);
                }
                catch (Exception ex)
                {
                    _log.ErrorFormat("Write request failed at {0}/{1} (attempt {2}) - exception {3}", i, len, retries, ex);
                    if (++retries > 5)
                    {
                        MessageBox.Show(String.Format("Failed to write file.\n\nProgress {0}/{1} records.\n\nLast exception: {2}", i, len, ex), "Fail");
                        return;
                    }
                    continue;
                }

                i += writeRecs.Length;
                recNum += writeRecs.Length / 2;
                retries = 0;

                if (recNum > UInt16.MaxValue)
                {
                    _log.InfoFormat("FileID {0} length exceeded, wrapping to next", fileNum);
                    fileNum++;
                    recNum = 0; // wrap around
                }
            }

            UpdateProgress(100);
            _log.Info("File sent OK");
            MessageBox.Show("File sent OK", "Success");
        }

        private void txtRecNum_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = (TextBox)sender;
            UInt16 recNum;
            if (UInt16.TryParse(tb.Text, out recNum))
            {
                tb.Background = Brushes.LightGreen;
                if (lblFileByteOffset != null) // trap initialisation issue
                {
                    lblFileByteOffset.Content = (recNum * 2).ToString();
                }
            }
            else
            {
                tb.Background = Brushes.LightSalmon;
            }
        }
        
        private void txtBaudRate_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = (TextBox)sender;
            int baud;
            if (int.TryParse(tb.Text, out baud))
            {
                tb.Background = Brushes.LightGreen;
            }
            else
            {
                tb.Background = Brushes.LightSalmon;
            }
        }

        private void UInt16ValidateTextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = (TextBox)sender;
            UInt16 len;
            if (UInt16.TryParse(tb.Text, out len))
            {
                tb.Background = Brushes.LightGreen;
            }
            else
            {
                tb.Background = Brushes.LightSalmon;
            }
        }

        private void UInt16x2ValidateTextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = (TextBox)sender;
            int len;
            if (int.TryParse(tb.Text, out len) && (len > 0) && (len < UInt16.MaxValue * 2))
            {
                tb.Background = Brushes.LightGreen;
            }
            else
            {
                tb.Background = Brushes.LightSalmon;
            }
        }

        private void cmdUpdateCoilsDG_Click(object sender, RoutedEventArgs e)
        {
            UInt16 coilStartNo, len;
            if (false == UInt16.TryParse(txtStartingCoil.Text, out coilStartNo))
            {
                MessageBox.Show("Could not parse starting coil", "Fail");
                return;
            }
            if (false == UInt16.TryParse(txtNumCoils.Text, out len))
            {
                MessageBox.Show("Could not parse number of coils", "Fail");
                return;
            }

            var coils = new bool[len];
            UpdateCoils(coils, coilStartNo);
        }

        private async void cmdReadCoils_Click(object sender, RoutedEventArgs e)
        {
            UInt16 coilStartNo, len;
            if (false == UInt16.TryParse(txtStartingCoil.Text, out coilStartNo))
            {
                MessageBox.Show("Could not parse starting coil", "Fail");
                return;
            }
            if (false == UInt16.TryParse(txtNumCoils.Text, out len))
            {
                MessageBox.Show("Could not parse number of coils", "Fail");
                return;
            }

            IEnumerable<bool> coils;
            try
            {
                coils = await _slave.ReadCoils(coilStartNo, len);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to read coils: " + ex.Message, "Fail");
                return;
            }

            UpdateCoils(coils, coilStartNo);
        }

        private async void cmdWriteCoils_Click(object sender, RoutedEventArgs e)
        {
            if (_coils.Count() < 1)
            {
                MessageBox.Show("No coil(s) available to write", "Error");
            }
            else if (_coils.Count() == 1)
            {
                try
                {
                    var coil = _coils.First();
                    await _slave.WriteCoil(coil.Register, coil.Value);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to write coil: " + ex.Message, "Fail");
                    return;
                }
                
                MessageBox.Show("Coil written OK", "Result");
            }
            else
            {
                var coils = new bool[_coils.Count()];
                for (int i = 0; i < coils.Length; i++)
                {
                    coils[i] = _coils[i].Value;
                }

                try
                {
                    await _slave.WriteCoils(_coils.FirstOrDefault().Register, coils);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to write coils: " + ex.Message, "Fail");
                    return;
                }

                MessageBox.Show("Coils written OK", "Result");
            }
        }
                
        private async void cmdReadDIs_Click(object sender, RoutedEventArgs e)
        {
            UInt16 startNo, len;
            if (false == UInt16.TryParse(txtStartingDI.Text, out startNo))
            {
                MessageBox.Show("Could not parse starting DI", "Fail");
                return;
            }
            if (false == UInt16.TryParse(txtNumDIs.Text, out len))
            {
                MessageBox.Show("Could not parse number of DIs", "Fail");
                return;
            }

            IEnumerable<bool> values;
            try
            {
                values = await _slave.ReadDiscreteInputs(startNo, len);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to read discrete inputs: " + ex.Message, "Fail");
                return;
            }

            UpdateDiscreteInputs(values, startNo);
        }
        
        private void cmdUpdateHRs_Click(object sender, RoutedEventArgs e)
        {
            UInt16 startNo, len;
            if (false == UInt16.TryParse(txtStartingHR.Text, out startNo))
            {
                MessageBox.Show("Could not parse starting HR", "Fail");
                return;
            }
            if (false == UInt16.TryParse(txtNumHRs.Text, out len))
            {
                MessageBox.Show("Could not parse number of HRs", "Fail");
                return;
            }

            var hrs = new UInt16[len];
            UpdateHoldingRegisters(hrs, startNo);
        }
        
        private async void cmdReadHRs_Click(object sender, RoutedEventArgs e)
        {
            UInt16 startNo, len;
            if (false == UInt16.TryParse(txtStartingHR.Text, out startNo))
            {
                MessageBox.Show("Could not parse starting HR", "Fail");
                return;
            }
            if (false == UInt16.TryParse(txtNumHRs.Text, out len))
            {
                MessageBox.Show("Could not parse number of HRs", "Fail");
                return;
            }

            IEnumerable<UInt16> values;
            try
            {
                values = await _slave.ReadHoldingRegisters(startNo, len);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to read holding registers: " + ex.Message, "Fail");
                return;
            }

            UpdateHoldingRegisters(values, startNo);
        }

        private async void cmdWriteHRs_Click(object sender, RoutedEventArgs e)
        {
            if (_holdingRegisters.Count() < 1)
            {
                MessageBox.Show("No holding registers to write", "Error");
            }
            else if (_holdingRegisters.Count() == 1)
            {
                try
                {
                    var hr = _holdingRegisters.First();
                    await _slave.WriteHoldingRegister(hr.Register, hr.Value);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to write holding register: " + ex.Message, "Fail");
                    return;
                }

                MessageBox.Show("Write holding register OK", "Result");
            }
            else
            {
                var hrs = new UInt16[_holdingRegisters.Count()];
                for (int i = 0; i < hrs.Length; i++)
                {
                    hrs[i] = _holdingRegisters[i].Value;
                }

                try
                {
                    await _slave.WriteHoldingRegisters(_holdingRegisters.FirstOrDefault().Register, hrs);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to write holding registers: " + ex.Message, "Fail");
                    return;
                }

                MessageBox.Show("Write holding registers OK", "Result");
            }
        }
        
        private async void cmdReadIRs_Click(object sender, RoutedEventArgs e)
        {
            UInt16 startNo, len;
            if (false == UInt16.TryParse(txtStartingIR.Text, out startNo))
            {
                MessageBox.Show("Could not parse starting IR", "Fail");
                return;
            }
            if (false == UInt16.TryParse(txtNumIRs.Text, out len))
            {
                MessageBox.Show("Could not parse number of IRs", "Fail");
                return;
            }

            IEnumerable<UInt16> values;
            try
            {
                values = await _slave.ReadInputRegisters(startNo, len);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to read input registers: " + ex.Message, "Fail");
                return;
            }

            UpdateInputRegisters(values, startNo);
        }

        private void cmdConnectTCP_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _tcpClient = new TcpClient();
                _tcpClient.Connect(txtIpHost.Text, int.Parse(txtPort.Text));
                _slave.Client = new ClientTCP_RTU(_tcpClient);

                _log.InfoFormat("Connected to {0} @ {1}/{2}", cboSerialPort.SelectedItem, txtBaudRate.Text, cboParity.SelectedItem);
                UpdateConnected();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to open serial port: " + ex.Message, "Connection failure");
            }
        }

        private void cmdDisconnectTCP_Click(object sender, RoutedEventArgs e)
        {
            _tcpClient.Close();
            _tcpClient = null;
            _slave.Client = null;

            _log.Info("COM port disconnected");
            UpdateConnected();
        }
    }

    #region Holding classes for presentation in DataGrid
    class Coil
    {
        // Read-write
        public Coil(UInt16 reg, bool val) { Register = reg; Value = val; }
        public UInt16 Register { get; private set; }
        public bool Value { get; set; }
    }

    class DiscreteInput
    {
        // Read-only
        public DiscreteInput(UInt16 reg, bool val) { Register = reg; Value = val; }
        public UInt16 Register { get; private set; }
        public bool Value { get; private set; }
    }

    class HoldingRegister
    {
        // Read-write
        public HoldingRegister(UInt16 reg, UInt16 val) { Register = reg; Value = val; }
        public UInt16 Register { get; private set; }
        public UInt16 Value { get; set; }

        public Int16 S_Value
        {
            get
            {
                return (Int16)Value;
            }
            set
            {
                Value = (UInt16)value;
            }
        }
        public string H_Value
        {
            get
            {
                return Value.ToString("X4");
            }
            set
            {
                UInt16 val;
                UInt16.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out val);
                Value = val;
            }
        }
    }

    class InputRegister
    {
        // Read-only
        public InputRegister(UInt16 reg, UInt16 val) { Register = reg; Value = val; }
        public UInt16 Register { get; private set; }
        public UInt16 Value { get; private set; }
        public Int16 S_Value { get { return (Int16)Value; } }
        public string H_Value { get { return Value.ToString("X4"); } }
    }
    #endregion
}
