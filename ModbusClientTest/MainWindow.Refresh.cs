using log4net.Repository.Hierarchy;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace VVG.Modbus.ClientTest
{
    public partial class MainWindow
    {
        private readonly RefreshCoordinator _refresh = new RefreshCoordinator();
        private readonly ActivityLogBuffer _activityLog = new ActivityLogBuffer();
        private DispatcherTimer _refreshTimer;
        private DispatcherTimer _logTimer;
        private ActivityLogAppender _activityAppender;
        private Hierarchy _loggingHierarchy;
        private bool _monitoringReady;
        private bool _closing;
        private bool _editingGrid;
        private bool _timeoutValid = true;
        private bool _lastPollFailed;
        private int _timeoutMilliseconds = 500;
        private int _connectionGeneration;
        private int _displayedLogVersion = -1;
        private double _logHeightAdded;

        private void InitialiseMonitoring()
        {
            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background);
            _refreshTimer.Tick += RefreshTimerTick;
            _logTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            _logTimer.Tick += (sender, args) => UpdateLogView();
            _activityAppender = new ActivityLogAppender(_activityLog);
            _activityAppender.ActivateOptions();
            _loggingHierarchy = (Hierarchy)log4net.LogManager.GetRepository();
            _loggingHierarchy.Root.AddAppender(_activityAppender);
            _loggingHierarchy.Configured = true;
            _monitoringReady = true;
            _logTimer.Start();
            _log.Info("Activity log attached; automatic refresh is off");
        }

        private bool ClientConnected
        {
            get { return _slave.Client != null && _slave.Client.IsConnected; }
        }

        private bool PollingSelected
        {
            get { return _refresh.Enabled && ClientConnected && tabs.SelectedIndex >= 0 && tabs.SelectedIndex < 4; }
        }

        private void UpdateOperationControls()
        {
            if (!_monitoringReady || _closing) return;
            bool manual = ClientConnected && !_refresh.IsBusy && !PollingSelected && _timeoutValid;
            foreach (var button in new[] { cmdReadCoils, cmdWriteCoils, cmdUpdateCoilsDG,
                cmdReadDIs, cmdReadHRs, cmdWriteHRs, cmdUpdateHRs, cmdReadIRs, cmdReadFile, cmdWriteFile, cmdBrowseFile })
            {
                button.IsEnabled = manual;
            }
            dgCoils.IsReadOnly = _refresh.IsBusy || PollingSelected;
            dgHoldingRegisters.IsReadOnly = _refresh.IsBusy || PollingSelected;
            txtCommandTimeout.IsEnabled = !_refresh.IsBusy;
        }

        private void ScheduleNextRefresh()
        {
            if (!_monitoringReady || _closing) return;
            _refreshTimer.Stop();
            UpdateOperationControls();
            if (!_refresh.Enabled) txtRefreshStatus.Text = "Off";
            else if (!_timeoutValid) txtRefreshStatus.Text = "Invalid command timeout";
            else if (!_refresh.IntervalValid) txtRefreshStatus.Text = "Interval: 100–3600000 ms";
            else if (!ClientConnected) txtRefreshStatus.Text = "Paused: disconnected";
            else if (tabs.SelectedIndex >= 4) txtRefreshStatus.Text = "Paused: file transfer tab";
            else if (_refresh.IsBusy) txtRefreshStatus.Text = "Command in progress";
            else if (_editingGrid) txtRefreshStatus.Text = "Paused: editing";
            else if (_refresh.CanRefresh(ClientConnected, tabs.SelectedIndex))
            {
                txtRefreshStatus.Text = _lastPollFailed ? "Last poll failed; retrying" : "Waiting for next poll";
                _refreshTimer.Interval = TimeSpan.FromMilliseconds(_refresh.IntervalMilliseconds);
                _refreshTimer.Start();
            }
        }

        private async void RefreshTimerTick(object sender, EventArgs args)
        {
            _refreshTimer.Stop();
            await RefreshTabAsync(tabs.SelectedIndex, true);
            ScheduleNextRefresh();
        }

        private void RefreshSettingsChanged(object sender, RoutedEventArgs args)
        {
            if (!_monitoringReady) return;
            _refresh.Enabled = chkAutoRefresh.IsChecked == true;
            if (_refresh.Enabled)
            {
                dgCoils.CancelEdit();
                dgCoils.CancelEdit(DataGridEditingUnit.Row);
                dgHoldingRegisters.CancelEdit();
                dgHoldingRegisters.CancelEdit(DataGridEditingUnit.Row);
                _editingGrid = false;
                _lastPollFailed = false;
            }
            _log.Info(_refresh.Enabled ? "Automatic refresh enabled" : "Automatic refresh disabled");
            ScheduleNextRefresh();
        }

        private void RefreshIntervalChanged(object sender, TextChangedEventArgs args)
        {
            if (!_monitoringReady) return;
            bool valid = _refresh.SetInterval(txtRefreshInterval.Text);
            txtRefreshInterval.Background = valid ? Brushes.White : Brushes.LightSalmon;
            ScheduleNextRefresh();
        }

        private void CommandTimeoutChanged(object sender, TextChangedEventArgs args)
        {
            if (!_monitoringReady) return;
            int milliseconds;
            _timeoutValid = int.TryParse(txtCommandTimeout.Text, NumberStyles.None, CultureInfo.InvariantCulture,
                out milliseconds) && milliseconds >= 1 && milliseconds <= 600000;
            txtCommandTimeout.Background = _timeoutValid ? Brushes.White : Brushes.LightSalmon;
            if (_timeoutValid)
            {
                _timeoutMilliseconds = milliseconds;
                if (ClientConnected && !_refresh.IsBusy)
                {
                    _slave.Client.Timeout = TimeSpan.FromMilliseconds(milliseconds);
                }
            }
            ScheduleNextRefresh();
        }

        private TimeSpan GetCommandTimeout()
        {
            if (!_timeoutValid) throw new ArgumentException("Command timeout must be between 1 and 600000 ms.");
            return TimeSpan.FromMilliseconds(_timeoutMilliseconds);
        }

        private void TabsSelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            if (ReferenceEquals(args.Source, tabs)) ScheduleNextRefresh();
        }

        private void GridBeginningEdit(object sender, DataGridBeginningEditEventArgs args)
        {
            _editingGrid = true;
            ScheduleNextRefresh();
        }

        private void GridRowEditEnding(object sender, DataGridRowEditEndingEventArgs args)
        {
            Dispatcher.BeginInvoke(new Action(() => { _editingGrid = false; ScheduleNextRefresh(); }));
        }

        private async Task RunUiOperationAsync(Func<Task> operation, bool automatic, string description)
        {
            if (_closing) return;
            _refreshTimer.Stop();
            try
            {
                await _refresh.TryRunAsync(async () =>
                {
                    UpdateOperationControls();
                    _slave.Client.Timeout = GetCommandTimeout();
                    await operation();
                });
            }
            catch (Exception exception)
            {
                _log.Error(description, exception);
                if (automatic) _lastPollFailed = true;
                if (!automatic && !_closing)
                {
                    MessageBox.Show(description + ": " + ErrorText(exception), "Command failed");
                }
            }
            finally
            {
                if (!_closing)
                {
                    UpdateOperationControls();
                    ScheduleNextRefresh();
                }
            }
        }

        private static string ErrorText(Exception exception)
        {
            return string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;
        }

        private async Task RefreshTabAsync(int selectedTab, bool automatic)
        {
            if (!ClientConnected || selectedTab < 0 || selectedTab >= 4 || _closing) return;
            if (automatic && (!_refresh.CanRefresh(ClientConnected, selectedTab) || _editingGrid || !_timeoutValid)) return;
            var client = _slave.Client;
            byte address = _slave.Address;
            int generation = _connectionGeneration;
            var starts = new[] { txtStartingCoil, txtStartingDI, txtStartingHR, txtStartingIR };
            var counts = new[] { txtNumCoils, txtNumDIs, txtNumHRs, txtNumIRs };
            string startText = starts[selectedTab].Text;
            string countText = counts[selectedTab].Text;
            await RunUiOperationAsync(async () =>
            {
                ushort start;
                ushort count;
                if (!ushort.TryParse(startText, out start) || !ushort.TryParse(countText, out count) || count == 0)
                {
                    throw new ArgumentException("Enter a valid starting address and a non-zero quantity.");
                }
                Func<bool> current = () => !_closing && generation == _connectionGeneration
                    && ReferenceEquals(client, _slave.Client) && address == _slave.Address
                    && tabs.SelectedIndex == selectedTab && starts[selectedTab].Text == startText
                    && counts[selectedTab].Text == countText;
                switch (selectedTab)
                {
                    case 0:
                        var coils = await client.ReadCoils(address, start, count);
                        if (current()) UpdateCoils(coils, start);
                        break;
                    case 1:
                        var inputs = await client.ReadDiscreteInputs(address, start, count);
                        if (current()) UpdateDiscreteInputs(inputs, start);
                        break;
                    case 2:
                        var holding = await client.ReadHoldingRegisters(address, start, count);
                        if (current()) UpdateHoldingRegisters(holding, start);
                        break;
                    case 3:
                        var registers = await client.ReadInputRegisters(address, start, count);
                        if (current()) UpdateInputRegisters(registers, start);
                        break;
                }
                _log.InfoFormat("{0}: read {1} values from {2}@{3}", ((TabItem)tabs.Items[selectedTab]).Header,
                    count, start, address);
                if (automatic) _lastPollFailed = false;
            }, automatic, "Failed to read " + ((TabItem)tabs.Items[selectedTab]).Header);
        }

        private void UpdateLogView()
        {
            if (_closing || logExpander.IsExpanded == false) return;
            int version = _activityLog.Version;
            if (version == _displayedLogVersion) return;
            bool follow = txtActivityLog.VerticalOffset >= txtActivityLog.ExtentHeight - txtActivityLog.ViewportHeight - 2;
            txtActivityLog.Text = _activityLog.GetText();
            _displayedLogVersion = version;
            if (follow) txtActivityLog.ScrollToEnd();
        }

        private void LogExpanded(object sender, RoutedEventArgs args)
        {
            if (!_monitoringReady) return;
            if (WindowState == WindowState.Normal)
            {
                double original = Height;
                Height = Math.Min(Height + 200, SystemParameters.WorkArea.Height);
                _logHeightAdded = Height - original;
                Top = Math.Max(SystemParameters.WorkArea.Top, Math.Min(Top, SystemParameters.WorkArea.Bottom - Height));
            }
            UpdateLogView();
        }

        private void LogCollapsed(object sender, RoutedEventArgs args)
        {
            if (!_monitoringReady) return;
            if (WindowState == WindowState.Normal) Height = Math.Max(MinHeight, Height - _logHeightAdded);
            _logHeightAdded = 0;
        }

        private void ClearLogClick(object sender, RoutedEventArgs args)
        {
            _activityLog.Clear();
            UpdateLogView();
        }

        private void SaveLogClick(object sender, RoutedEventArgs args)
        {
            var dialog = new SaveFileDialog { Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt", FileName = "ModbusClient.log" };
            if (dialog.ShowDialog() != true) return;
            try { File.WriteAllText(dialog.FileName, _activityLog.GetText(), Encoding.UTF8); }
            catch (Exception exception)
            {
                _log.Error("Failed to save activity log", exception);
                MessageBox.Show(ErrorText(exception), "Failed to save log");
            }
        }

        protected override void OnClosed(EventArgs args)
        {
            _closing = true;
            _refreshTimer?.Stop();
            _logTimer?.Stop();
            try { _serialPort?.Dispose(); _tcpClient?.Close(); }
            catch (Exception exception) { _log.Warn("Closing connection", exception); }
            _loggingHierarchy?.Root.RemoveAppender(_activityAppender);
            _activityAppender?.Close();
            base.OnClosed(args);
        }
    }
}
