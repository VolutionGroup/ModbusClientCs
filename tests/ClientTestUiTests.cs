using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VVG.Modbus;
using VVG.Modbus.ClientTest;

internal sealed class UiFakeClient : ClientRTU_Base
{
    internal readonly List<byte[]> Frames = new List<byte[]>();
    internal int FailuresRemaining;
    internal int FailRecord = -1;
    internal TaskCompletionSource<byte[]> Pending;
    internal TimeSpan ObservedTimeout;
    public override bool IsConnected { get { return true; } }
    public override TimeSpan Timeout { get; set; } = TimeSpan.FromMilliseconds(500);

    protected override void CommsSend(byte[] data) { Frames.Add((byte[])data.Clone()); }
    protected override Task<byte[]> CommsReceive(int count)
    {
        ObservedTimeout = Timeout;
        byte[] tx = Frames.Last();
        int record = tx[1] >= 20 ? ((tx[6] << 8) | tx[7]) : -1;
        if (FailuresRemaining > 0 && (FailRecord < 0 || FailRecord == record))
        {
            FailuresRemaining--;
            return Task.FromException<byte[]>(new IOException("test poll failed"));
        }
        if (Pending != null) return Pending.Task;
        return Task.FromResult(Reply(tx));
    }

    internal static byte[] Frame(params byte[] data)
    {
        ushort crc = 0xffff;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (ushort)((crc >> 1) ^ ((crc & 1) != 0 ? 0xa001 : 0));
        }
        return data.Concat(new byte[] {(byte)crc, (byte)(crc >> 8)}).ToArray();
    }

    internal static byte[] Reply(byte[] tx)
    {
        int function = tx[1];
        if (function == 21 || function == 5 || function == 6) return (byte[])tx.Clone();
        if (function == 15 || function == 16) return Frame(tx.Take(6).ToArray());
        if (function == 20)
        {
            int bytes = ((tx[8] << 8) | tx[9]) * 2;
            int file = (tx[4] << 8) | tx[5];
            int record = (tx[6] << 8) | tx[7];
            return Frame(new byte[] {tx[0],20,(byte)(bytes + 2),(byte)(bytes + 1),6}
                .Concat(Enumerable.Range(0, bytes).Select(i => (byte)(file ^ (record * 2 + i)))).ToArray());
        }
        int quantity = (tx[4] << 8) | tx[5];
        int length = function <= 2 ? (quantity + 7) / 8 : quantity * 2;
        return Frame(new byte[] {tx[0],(byte)function,(byte)length}
            .Concat(Enumerable.Repeat((byte)0xa5, length)).ToArray());
    }
}

internal static class ClientTestUiTests
{
    private static readonly List<KeyValuePair<string, Action>> Tests = new List<KeyValuePair<string, Action>>();
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Add(string name, Action body) { Tests.Add(new KeyValuePair<string, Action>(name, body)); }
    private static void Throws<T>(Action body) where T : Exception
    {
        try { body(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Wait(Task task)
    {
        while (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
    private static object Field(object target, string name)
    {
        return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }
    private static object Invoke(object target, string name, params object[] args)
    {
        try { return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args); }
        catch (TargetInvocationException exception) { throw exception.InnerException; }
    }
    private static MainWindow Window(UiFakeClient client)
    {
        var window = new MainWindow();
        window.Top = 0;
        ((ClientSlave)Field(window, "_slave")).Client = client;
        ((TabControl)Field(window, "tabs")).IsEnabled = true;
        Invoke(window, "ScheduleNextRefresh");
        return window;
    }

    private static void DefineTests(string output)
    {
        Add("refresh.eligibility", () =>
        {
            var coordinator = new RefreshCoordinator();
            Check(!coordinator.CanRefresh(true, 0), "refresh defaults on");
            coordinator.Enabled = true;
            for (int tab = 0; tab < 4; tab++) Check(coordinator.CanRefresh(true, tab), "read tab excluded");
            Check(!coordinator.CanRefresh(true, 4) && !coordinator.CanRefresh(false, 0), "file/disconnected polling allowed");
            foreach (string value in new[] {"", "0", "99", "3600001", "-1", "1.5", "word"})
                Check(!coordinator.SetInterval(value) && !coordinator.CanRefresh(true, 0), "invalid interval accepted");
            foreach (string value in new[] {"100", "1000", "3600000"}) Check(coordinator.SetInterval(value), "valid interval rejected");
        });
        Add("refresh.no_overlap_and_recovery", () =>
        {
            var coordinator = new RefreshCoordinator {Enabled = true};
            var completion = new TaskCompletionSource<bool>();
            Task<bool> first = coordinator.TryRunAsync(() => completion.Task);
            Check(coordinator.IsBusy && !coordinator.CanRefresh(true, 0), "operation not busy");
            bool secondRan = false;
            Task<bool> second = coordinator.TryRunAsync(() => { secondRan = true; return Task.CompletedTask; });
            Wait(second);
            Check(!second.Result && !secondRan, "overlapping operation ran");
            completion.SetResult(true);
            Wait(first);
            Check(!coordinator.IsBusy, "busy flag retained");
            Throws<IOException>(() => Wait(coordinator.TryRunAsync(() => Task.FromException(new IOException()))));
            Check(!coordinator.IsBusy, "failed operation retained gate");
        });
        Add("log.bounded_and_thread_safe", () =>
        {
            var log = new ActivityLogBuffer(3);
            for (int i = 0; i < 5; i++) log.Append(i.ToString());
            Check(log.GetText().Split(new[] {Environment.NewLine}, StringSplitOptions.None).SequenceEqual(new[] {"2","3","4"}), "log not bounded");
            log.Clear();
            Check(log.GetText() == string.Empty, "clear failed");
            log.Append(new string('a', 100000));
            Check(log.GetText().Length < 4200, "entry not bounded");
            Parallel.For(0, 1000, i => log.Append(i.ToString()));
            Check(log.GetText().Split(new[] {Environment.NewLine}, StringSplitOptions.None).Length == 3, "parallel append lost bound");
        });
        Add("files.plan_rollover", () =>
        {
            var position = new FileTransferPosition(4, 65535, 132);
            Check(position.AdditionalFiles == 1 && position.ChunkLength == 2, "starting offset boundary wrong");
            position.Advance();
            Check(position.FileNumber == 5 && position.RecordNumber == 0 && position.Offset == 2, "adjacent file not selected");
            position.Advance(); position.Advance();
            Check(position.Complete && position.Offset == 132, "plan lost bytes");
            Check(new FileTransferPosition(4, 0, 131072).AdditionalFiles == 0, "exact 128 KiB overflows");
            Check(new FileTransferPosition(4, 0, 131073).AdditionalFiles == 1, "128 KiB plus one does not overflow");
            Throws<ArgumentOutOfRangeException>(() => new FileTransferPosition(65535, 0, 131073));
            Throws<ArgumentOutOfRangeException>(() => new FileTransferPosition(1, 0, 0));
        });
        foreach (bool writing in new[] {false,true})
        {
            string direction = writing ? "write" : "read";
            Add("files." + direction + "_large_rollover", () =>
            {
                var client = new UiFakeClient {AllowExtendedFileRecordAddresses = true};
                var slave = new ClientSlave(client, 0x11);
                int length = 131074;
                var data = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
                var position = new FileTransferPosition(4, 0, length);
                float progress = -1;
                Task<bool> task = FileTransferRunner.RunAsync(slave, data, position, writing,
                    (e,p) => Task.FromResult(false), value => progress = value, () => Task.CompletedTask);
                Wait(task);
                Check(task.Result && progress == 100 && position.Complete, "large transfer incomplete");
                Check(client.Frames.Count == 1025, "incorrect chunk count");
                byte[] last = client.Frames.Last();
                Check(last[5] == 5 && last[6] == 0 && last[7] == 0, "did not continue at next file record zero");
                if (!writing) Check(data[131072] == 5 && data[131073] == 4, "adjacent-file data misplaced");
            });
            Add("files." + direction + "_continue_after_retries", () =>
            {
                var client = new UiFakeClient {AllowExtendedFileRecordAddresses = true,
                    FailRecord = 64, FailuresRemaining = writing ? 10 : 5};
                var position = new FileTransferPosition(4, 0, 256);
                int prompts = 0;
                Task<bool> task = FileTransferRunner.RunAsync(new ClientSlave(client, 0x11), new byte[256], position, writing,
                    (e,p) => { prompts++; Check(p.Offset == 128 && p.RecordNumber == 64, "progress reset on failure"); return Task.FromResult(true); },
                    value => {}, () => Task.CompletedTask);
                Wait(task);
                Check(task.Result && prompts == 1 && position.Complete, "continue did not resume failed chunk");
                Check(client.Frames.Skip(1).All(frame => frame[6] == 0 && frame[7] == 64), "retry changed record");
            });
            Add("files." + direction + "_abort_after_retries", () =>
            {
                var client = new UiFakeClient {AllowExtendedFileRecordAddresses = true, FailuresRemaining = 20};
                var position = new FileTransferPosition(4, 0, 128);
                Task<bool> task = FileTransferRunner.RunAsync(new ClientSlave(client, 0x11), new byte[128], position, writing,
                    (e,p) => Task.FromResult(false), value => {}, () => Task.CompletedTask);
                Wait(task);
                Check(!task.Result && position.Offset == 0, "abort advanced transfer");
            });
        }
        Add("files.extended_addresses_opt_in", () =>
        {
            var client = new UiFakeClient();
            Throws<ArgumentOutOfRangeException>(() => Wait(client.WriteFileRecord(0x11, 4, 65535, new byte[2])));
            client.AllowExtendedFileRecordAddresses = true;
            Wait(client.WriteFileRecord(0x11, 4, 65535, new byte[2]));
            Throws<ArgumentOutOfRangeException>(() => Wait(client.WriteFileRecord(0x11, 4, 65535, new byte[4])));
        });
        Add("files.odd_read_at_boundary", () =>
        {
            var client = new UiFakeClient();
            var data = new byte[3];
            var position = new FileTransferPosition(4, 65535, 3);
            Task<bool> task = FileTransferRunner.RunAsync(new ClientSlave(client, 0x11), data, position, false,
                (e,p) => Task.FromResult(false), value => {}, () => Task.CompletedTask);
            Wait(task);
            Check(task.Result && data.SequenceEqual(new byte[] {250,251,5}), "odd adjacent-file bytes incorrect");
        });
        Add("files.odd_write_rejected_before_send", () =>
        {
            var client = new UiFakeClient {AllowExtendedFileRecordAddresses = true};
            Throws<ArgumentException>(() => Wait(FileTransferRunner.RunAsync(new ClientSlave(client, 0x11),
                new byte[3], new FileTransferPosition(4, 0, 3), true, (e,p) => Task.FromResult(false),
                value => {}, () => Task.CompletedTask)));
            Check(client.Frames.Count == 0, "odd file write sent");
        });
        Add("window.manual_lock_and_file_exclusion", () =>
        {
            var client = new UiFakeClient();
            var window = Window(client);
            try
            {
                ((CheckBox)Field(window, "chkAutoRefresh")).IsChecked = true;
                foreach (string name in new[] {"cmdReadCoils","cmdWriteCoils","cmdReadDIs","cmdReadHRs","cmdWriteHRs","cmdReadIRs","cmdUpdateHRs"})
                    Check(!((Button)Field(window,name)).IsEnabled, "manual button enabled during auto-refresh: " + name);
                ((TabControl)Field(window,"tabs")).SelectedIndex = 4;
                Check(((Button)Field(window,"cmdReadFile")).IsEnabled, "file read disabled while polling paused");
                Check(!((DispatcherTimer)Field(window,"_refreshTimer")).IsEnabled, "timer active on file tab");
                Check(client.Frames.Count == 0, "file tab was polled");
                ((TabControl)Field(window,"tabs")).SelectedIndex = 0;
                ((CheckBox)Field(window, "chkAutoRefresh")).IsChecked = false;
                Check(((Button)Field(window,"cmdReadCoils")).IsEnabled, "manual read not restored");
            }
            finally { window.Close(); }
        });
        Add("window.silent_failed_poll_and_timeout", () =>
        {
            var client = new UiFakeClient {FailuresRemaining = 1};
            var window = Window(client);
            try
            {
                ((TextBox)Field(window,"txtCommandTimeout")).Text = "419";
                ((CheckBox)Field(window,"chkAutoRefresh")).IsChecked = true;
                Wait((Task)Invoke(window,"RefreshTabAsync",0,true));
                Check(client.ObservedTimeout.TotalMilliseconds == 419, "configured timeout ignored");
                string text = (string)Field(window,"_activityLog").GetType().GetMethod("GetText").Invoke(Field(window,"_activityLog"),null);
                Check(text.Contains("Failed to read Coils") && text.Contains("test poll failed"), "failed poll not logged");
                Check(((TextBox)Field(window,"txtCommandTimeout")).IsEnabled, "timeout remained locked after failure");
                Wait((Task)Invoke(window,"RefreshTabAsync",0,true));
                Check(((DataGrid)Field(window,"dgCoils")).Items.Count == 1, "poll did not recover after failure");
            }
            finally { window.Close(); }
        });
        Add("window.overlap_stale_result_and_edit_lock", () =>
        {
            var client = new UiFakeClient {Pending = new TaskCompletionSource<byte[]>()};
            var window = Window(client);
            try
            {
                ((CheckBox)Field(window,"chkAutoRefresh")).IsChecked = true;
                Task read = (Task)Invoke(window,"RefreshTabAsync",0,true);
                Check(!read.IsCompleted && !((TextBox)Field(window,"txtCommandTimeout")).IsEnabled, "timeout not locked during request");
                Wait((Task)Invoke(window,"RefreshTabAsync",0,true));
                Check(client.Frames.Count == 1, "overlapping poll queued");
                ((TabControl)Field(window,"tabs")).SelectedIndex = 1;
                ((TabControl)Field(window,"tabs")).SelectedIndex = 4;
                Check(!((Button)Field(window,"cmdReadFile")).IsEnabled, "file read enabled during in-flight poll");
                client.Pending.SetResult(UiFakeClient.Reply(client.Frames.Last()));
                Wait(read);
                var coils = (System.Collections.ICollection)Field(window,"_coils");
                Check(coils.Count == 0, "stale tab result applied");
            }
            finally { window.Close(); }
        });
        Add("window.current_tab_routing_and_interval", () =>
        {
            var client = new UiFakeClient();
            var window = Window(client);
            try
            {
                window.GetType().GetField("_editingGrid", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, true);
                ((CheckBox)Field(window,"chkAutoRefresh")).IsChecked = true;
                ((TextBox)Field(window,"txtRefreshInterval")).Text = "1750";
                var timer = (DispatcherTimer)Field(window,"_refreshTimer");
                Check(timer.IsEnabled && timer.Interval.TotalMilliseconds == 1750, "interval not applied");
                for (int tab = 0; tab < 4; tab++)
                {
                    ((TabControl)Field(window,"tabs")).SelectedIndex = tab;
                    Wait((Task)Invoke(window,"RefreshTabAsync",tab,true));
                    Check(client.Frames.Last()[1] == tab + 1, "wrong current tab polled");
                }
                int calls = client.Frames.Count;
                ((TextBox)Field(window,"txtRefreshInterval")).Text = "0";
                Wait((Task)Invoke(window,"RefreshTabAsync",0,true));
                Check(!timer.IsEnabled && client.Frames.Count == calls, "invalid interval polled");
                ((TextBox)Field(window,"txtRefreshInterval")).Text = "1000";
                ((TextBox)Field(window,"txtCommandTimeout")).Text = "0";
                Wait((Task)Invoke(window,"RefreshTabAsync",0,true));
                Check(!timer.IsEnabled && client.Frames.Count == calls, "invalid timeout polled");
            }
            finally { window.Close(); }
        });
        Add("window.log4net_consumer_and_layout", () =>
        {
            var existing = new log4net.Appender.MemoryAppender();
            existing.ActivateOptions();
            var root = ((log4net.Repository.Hierarchy.Hierarchy)log4net.LogManager.GetRepository()).Root;
            root.AddAppender(existing);
            var window = Window(new UiFakeClient());
            try
            {
                log4net.LogManager.GetLogger("ExistingConsumerTest").Warn("existing-log4net-marker");
                Check(existing.GetEvents().Any(item => item.RenderedMessage.Contains("existing-log4net-marker")),
                    "existing appender stopped receiving events");
                ((Expander)Field(window,"logExpander")).IsExpanded = true;
                Check(((TextBox)Field(window,"txtActivityLog")).Text.Contains("existing-log4net-marker"), "existing log4net event not consumed");
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(640,590)); content.Arrange(new Rect(0,0,640,590)); content.UpdateLayout();
                var bitmap = new RenderTargetBitmap(640,590,96,96,PixelFormats.Pbgra32);
                bitmap.Render(content);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(output,"window-preview.png"))) png.Save(stream);
                ((Expander)Field(window,"logExpander")).IsExpanded = false;
                Check(window.Height >= window.MinHeight, "collapsed below minimum height");
            }
            finally { window.Close(); root.RemoveAppender(existing); existing.Close(); }
        });
    }

    [STAThread]
    private static int Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : Environment.CurrentDirectory;
        Directory.CreateDirectory(output);
        var app = new Application {ShutdownMode = ShutdownMode.OnExplicitShutdown};
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        DefineTests(output);
        int failed = 0;
        foreach (var test in Tests)
        {
            try { test.Value(); Console.WriteLine("PASS " + test.Key); }
            catch (Exception exception) { failed++; Console.WriteLine("FAIL " + test.Key + ": " + exception); }
        }
        Console.WriteLine("RESULT " + (Tests.Count-failed) + "/" + Tests.Count + " passed; " + failed + " failed");
        app.Shutdown();
        return failed == 0 ? 0 : 1;
    }
}
