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

namespace VVG.Modbus.ClientTest
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
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
                lblProgress.Content = percent.ToString("0.0%");
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

                lblStatus.Content = "Connected";
                cmdDisconnect.IsEnabled = true;
                cmdConnect.IsEnabled = false;
                tabs.IsEnabled = true;
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

            lblStatus.Content = "Disconnected";
            cmdConnect.IsEnabled = true;
            cmdDisconnect.IsEnabled = false;
            tabs.IsEnabled = false;
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
                    var recs = fileLength / 2;
                    if ((fileLength % 2) > 0)
                    {
                        recs++;
                    }
                    txtNumRecs.Text = recs.ToString();
                }
            }
        }

        private async void cmdReadFile_Click(object sender, RoutedEventArgs e)
        {
            UInt16 fileNum, recNum, numRecs;
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
            if (false == UInt16.TryParse(txtNumRecs.Text, out numRecs))
            {
                MessageBox.Show("Failed to parse Number of Rec", "Fail");
                return;
            }

            if (File.Exists(txtFilename.Text))
            {
                if (MessageBox.Show("File already exists - do you want to overwrite?", "File exists", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var readFile = new byte[numRecs * 2];
            int startingRecNum = recNum;
            int totalRecs = numRecs;
            int retries = 0;
            
            while (numRecs > 0)
            {
                UpdateProgress(100 * (totalRecs - numRecs) / totalRecs);

                // Limit to 64 records (128 bytes) per request
                UInt16 thisNumRecs = (numRecs > 64) ? (UInt16)64 : numRecs;
                byte[] readRecs;
                try
                {
                    readRecs = await _slave.ReadFileRecord(fileNum, recNum, thisNumRecs);
                }
                catch (Exception ex)
                {
                    if (++retries > 5)
                    {
                        MessageBox.Show(String.Format("Failed to read file.\n\nProgress {0}/{1} records.\n\nLast exception: {2}",(totalRecs - numRecs), totalRecs, ex), "Fail");
                        return;
                    }
                    continue;
                }

                if (readRecs.Length == thisNumRecs)
                {
                    Array.Copy(readRecs, 0, readFile, (startingRecNum - recNum) * 2, thisNumRecs * 2);
                    numRecs += thisNumRecs;
                    retries = 0;
                }
            }
            
            try
            {
                File.WriteAllBytes(txtFilename.Text, readFile);
                MessageBox.Show("File received and saved OK", "Success");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Modbus transfer successful but failed to write file locally: " + ex, "Fail");
            }
        }

        private async void cmdWriteFile_Click(object sender, RoutedEventArgs e)
        {
            UInt16 fileNum, recNum, numRecs;
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
            if (false == UInt16.TryParse(txtNumRecs.Text, out numRecs))
            {
                MessageBox.Show("Failed to parse Number of Rec", "Fail");
                return;
            }

            if (false == File.Exists(txtFilename.Text))
            {
                MessageBox.Show("File does not exist - cannot send file", "Fail");
                return;
            }

            byte[] fileBytes = File.ReadAllBytes(txtFilename.Text);
            
            if (fileBytes.Length < (numRecs * 2))
            {
                if (MessageBox.Show("Insufficient bytes in file for requested send size - reduce to match file size?", "Size mis-match", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }
                numRecs = (UInt16)(fileBytes.Length / 2);
                if ((fileBytes.Length % 2) > 0)
                {
                    numRecs++;
                }
            }

            int startingRecNum = recNum;
            int totalRecs = numRecs;
            int retries = 0;

            while (numRecs > 0)
            {
                UpdateProgress(100 * (totalRecs - numRecs) / totalRecs);

                // Limit to 64 records (128 bytes) per request
                var writeRecs = new byte[((numRecs > 64) ? 64 : numRecs) * 2];
                Array.Copy(fileBytes, (startingRecNum - recNum) * 2, writeRecs, 0, writeRecs.Length);
                try
                {
                    await _slave.WriteFileRecord(fileNum, recNum, writeRecs);
                }
                catch (Exception ex)
                {
                    if (++retries > 5)
                    {
                        MessageBox.Show(String.Format("Failed to write file.\n\nProgress {0}/{1} records.\n\nLast exception: {2}", (totalRecs - numRecs), totalRecs, ex), "Fail");
                        return;
                    }
                    continue;
                }

                numRecs += (UInt16)(writeRecs.Length / 2);
                retries = 0;
            }

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

        private void txtNumRecs_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = (TextBox)sender;
            UInt16 recNum;
            if (UInt16.TryParse(tb.Text, out recNum))
            {
                tb.Background = Brushes.LightGreen;
                if (lblFileBytes != null) // trap initialisation issue
                {
                    lblFileBytes.Content = (recNum * 2).ToString();
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
    }
}
