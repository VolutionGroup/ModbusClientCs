using log4net;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
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

namespace VVG.Modbus.ClientTest
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly ILog _log = LogManager.GetLogger(typeof(MainWindow));
        private ClientSlave _slave = new ClientSlave((IClient)null, 1);
        private SerialPort _serialPort = null;
        private TcpClient _tcpClient = null;

        private List<Coil> _coils = new List<Coil>();
        private List<DiscreteInput> _discreteInputs = new List<DiscreteInput>();
        private List<HoldingRegister> _holdingRegisters = new List<HoldingRegister>();
        private List<InputRegister> _inputRegisters = new List<InputRegister>();

        public MainWindow()
        {
            InitializeComponent();
            InitialiseMonitoring();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshComPorts();
            UpdateProgress(0.0f);

            UpdateCoils(null, 0);
            UpdateDiscreteInputs(null, 0);
            UpdateHoldingRegisters(null, 0);
            UpdateInputRegisters(null, 0);

            UpdateConnected();

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
            _connectionGeneration++;
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
            UpdateOperationControls();
            ScheduleNextRefresh();
        }
        #endregion

        private void cmdConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TimeSpan timeout = GetCommandTimeout();
                _serialPort = new SerialPort((string)cboSerialPort.SelectedItem)
                {
                    BaudRate = int.Parse(txtBaudRate.Text),
                    Parity = (Parity)Enum.Parse(typeof(Parity), (string)cboParity.SelectedItem)
                };
                _serialPort.Open();
                _slave.Client = new ClientRTU(_serialPort) { Timeout = timeout, AllowExtendedFileRecordAddresses = true };

                _log.InfoFormat("Connected to {0} @ {1}/{2}", cboSerialPort.SelectedItem, txtBaudRate.Text, cboParity.SelectedItem);
                UpdateConnected();
            }
            catch (Exception ex)
            {
                _log.Error("Operation failed", ex);
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
            await RunUiOperationAsync(() => TransferFileAsync(false), false, "File read failed");
        }

        private async void cmdWriteFile_Click(object sender, RoutedEventArgs e)
        {
            await RunUiOperationAsync(() => TransferFileAsync(true), false, "File write failed");
        }

        private async Task TransferFileAsync(bool writing)
        {
            ushort fileNumber;
            ushort recordNumber;
            int length;
            if (!ushort.TryParse(txtFileNum.Text, out fileNumber) || fileNumber == 0)
                throw new ArgumentException("Enter a file number from 1 to 65535.");
            if (!ushort.TryParse(txtRecNum.Text, out recordNumber))
                throw new ArgumentException("Enter a record number from 0 to 65535.");
            if (!int.TryParse(txtLen.Text, out length) || length <= 0)
                throw new ArgumentException("Enter a positive transfer length in bytes.");
            string filename = txtFilename.Text;
            if (string.IsNullOrWhiteSpace(filename)) throw new ArgumentException("Choose a local filename.");
            byte[] data;
            if (writing)
            {
                data = File.ReadAllBytes(filename);
                if (length > data.Length)
                {
                    if (MessageBox.Show("The file has fewer bytes than requested. Use its actual size?", "File size",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    length = data.Length;
                    txtLen.Text = length.ToString();
                }
                if (length <= 0 || length % 2 != 0)
                    throw new ArgumentException("File writes require a positive, even number of bytes.");
                if (data.Length != length) Array.Resize(ref data, length);
            }
            else
            {
                if (File.Exists(filename) && MessageBox.Show("File already exists. Overwrite it?", "File exists",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                data = null;
            }
            var position = new FileTransferPosition(fileNumber, recordNumber, length);
            if (position.AdditionalFiles > 0 && MessageBox.Show(string.Format(
                "This transfer crosses a 128 KiB file boundary. Continue across {0} adjacent file ID(s), ending at file {1}?",
                position.AdditionalFiles, fileNumber + position.AdditionalFiles), "Adjacent files",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            if (data == null) data = new byte[length];
            var slave = new ClientSlave(_slave.Client, _slave.Address);
            _log.InfoFormat("Starting file {0}: {1} bytes from file {2}, record {3}",
                writing ? "write" : "read", length, fileNumber, recordNumber);
            bool completed = await FileTransferRunner.RunAsync(slave, data, position, writing,
                (exception, failed) => Task.FromResult(!_closing && MessageBox.Show(string.Format(
                    "Retries exhausted during file {0}.\n\nProgress: {1}/{2} bytes. File {3}, record {4}.\n\n{5}\n\nAttempt to continue from this position?",
                    writing ? "write" : "read", failed.Offset, failed.Length, failed.FileNumber,
                    failed.RecordNumber, ErrorText(exception)), "File transfer issue",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes), UpdateProgress);
            if (!completed || _closing) return;
            if (!writing) File.WriteAllBytes(filename, data);
            MessageBox.Show(writing ? "File sent OK" : "File received and saved OK", "Success");
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
            if (int.TryParse(tb.Text, out len) && (len > 0))
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
            await RefreshTabAsync(0, false);
        }

        private async void cmdWriteCoils_Click(object sender, RoutedEventArgs e)
        {
            await RunUiOperationAsync(WriteCoilsAsync, false, "Write command failed");
        }

        private async Task WriteCoilsAsync()
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
                    _log.Error("Write failed", ex);
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
                    _log.Error("Write failed", ex);
                    MessageBox.Show("Failed to write coils: " + ex.Message, "Fail");
                    return;
                }

                MessageBox.Show("Coils written OK", "Result");
            }
        }
                
        private async void cmdReadDIs_Click(object sender, RoutedEventArgs e)
        {
            await RefreshTabAsync(1, false);
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
            await RefreshTabAsync(2, false);
        }

        private async void cmdWriteHRs_Click(object sender, RoutedEventArgs e)
        {
            await RunUiOperationAsync(WriteHoldingRegistersAsync, false, "Write command failed");
        }

        private async Task WriteHoldingRegistersAsync()
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
                    _log.Error("Write failed", ex);
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
                    _log.Error("Write failed", ex);
                    MessageBox.Show("Failed to write holding registers: " + ex.Message, "Fail");
                    return;
                }

                MessageBox.Show("Write holding registers OK", "Result");
            }
        }
        
        private async void cmdReadIRs_Click(object sender, RoutedEventArgs e)
        {
            await RefreshTabAsync(3, false);
        }

        private void cmdConnectTCP_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TimeSpan timeout = GetCommandTimeout();
                _tcpClient = new TcpClient();
                _tcpClient.Connect(txtIpHost.Text, int.Parse(txtPort.Text));
                _slave.Client = new ClientTCP_RTU(_tcpClient) { Timeout = timeout, AllowExtendedFileRecordAddresses = true };

                _log.InfoFormat("Connected to {0}:{1} (RTU over TCP)", txtIpHost.Text, txtPort.Text);
                UpdateConnected();
            }
            catch (Exception ex)
            {
                _log.Error("Operation failed", ex);
                MessageBox.Show("Failed to open TCP connection: " + ex.Message, "Connection failure");
            }
        }

        private void cmdDisconnectTCP_Click(object sender, RoutedEventArgs e)
        {
            _tcpClient.Close();
            _tcpClient = null;
            _slave.Client = null;

            _log.Info("TCP connection disconnected");
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
