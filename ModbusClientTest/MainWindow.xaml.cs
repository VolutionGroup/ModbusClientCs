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

namespace VVG.Modbus.ClientTest
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly ILog _log = LogManager.GetLogger(typeof(MainWindow));
        private ClientSlave _slave = new ClientSlave();
        private SerialPort _port = null;

        public MainWindow()
        {
            InitializeComponent();

            RefreshComPorts();
            UpdateProgress(0.0f);
        }
        
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

        private void UpdateConnected(bool connected)
        {
            lblStatus.Content = connected ? "Connected" : "Disconnected";
            cmdDisconnect.IsEnabled = connected;
            cmdConnect.IsEnabled = !connected;
            tabs.IsEnabled = connected;
            txtBaudRate.IsEnabled = !connected;
            cboParity.IsEnabled = !connected;
            txtSlaveID.IsEnabled = !connected;
        }

        private void cmdConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _port = new SerialPort((string)cboSerialPort.SelectedItem)
                {
                    BaudRate = int.Parse(txtBaudRate.Text),
                    Parity = (Parity)Enum.Parse(typeof(Parity), (string)cboParity.SelectedItem)
                };
                _port.Open();
                _slave.Client = new ClientRTU(_port);

                _log.InfoFormat("Connected to {0} @ {1}/{2}", cboSerialPort.SelectedItem, txtBaudRate.Text, cboParity.SelectedItem);
                UpdateConnected(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to open serial port: " + ex, "Connection failure");
            }
        }

        private void cmdDisconnect_Click(object sender, RoutedEventArgs e)
        {
            _port.Close();
            _port = null;
            _slave.Client = null;

            _log.Info("COM port disconnected");
            UpdateConnected(false);
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
            UInt16 fileNum, recNum, len;
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
            if (false == UInt16.TryParse(txtLen.Text, out len))
            {
                MessageBox.Show("Failed to parse Length (bytes)", "Fail");
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
                UpdateProgress(100 * i / len);
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
                MessageBox.Show("Modbus transfer successful but failed to write file locally: " + ex, "Fail");
            }
        }

        private async void cmdWriteFile_Click(object sender, RoutedEventArgs e)
        {
            UInt16 fileNum, recNum, len;
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
            if (false == UInt16.TryParse(txtLen.Text, out len))
            {
                MessageBox.Show("Failed to parse Length", "Fail");
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
                if (MessageBox.Show("Insufficient bytes in file for requested send size - reduce to match file size?", "Size mis-match", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }
                len = (UInt16)fileBytes.Length;
            }

            _log.InfoFormat("Beginning File {0} write from record {1}, of {2} bytes", fileNum, recNum, len);

            int retries = 0;

            for (int i = 0; i < len;)
            {
                UpdateProgress(100 * i / len);

                int remaining = (len - i);

                // Limit to 128 bytes per request
                var writeRecs = new byte[(remaining > 128) ? 128 : remaining];
                Array.Copy(fileBytes, i, writeRecs, 0, writeRecs.Length);
                try
                {
                    await _slave.WriteFileRecord(fileNum, recNum, writeRecs);
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

                i += (UInt16)writeRecs.Length;
                recNum += (UInt16)(writeRecs.Length / 2);
                retries = 0;
            }

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

        private void txtLen_TextChanged(object sender, TextChangedEventArgs e)
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
    }
}
