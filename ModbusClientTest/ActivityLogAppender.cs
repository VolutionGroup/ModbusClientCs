using log4net.Appender;
using log4net.Core;
using System;
using System.Collections.Generic;

namespace VVG.Modbus.ClientTest
{
    /// <summary>A thread-safe bounded buffer, independent of WPF and transport threads.</summary>
    internal sealed class ActivityLogBuffer
    {
        private readonly object _sync = new object();
        private readonly Queue<string> _entries = new Queue<string>();
        private readonly int _capacity;
        private int _version;

        public ActivityLogBuffer(int capacity = 1000)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        public int Version { get { lock (_sync) { return _version; } } }

        public void Append(string entry)
        {
            entry = entry ?? string.Empty;
            if (entry.Length > 4096) entry = entry.Substring(0, 4096) + " [truncated]";
            lock (_sync)
            {
                while (_entries.Count >= _capacity) _entries.Dequeue();
                _entries.Enqueue(entry);
                unchecked { _version++; }
            }
        }

        public string GetText()
        {
            lock (_sync) { return string.Join(Environment.NewLine, _entries); }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _entries.Clear();
                unchecked { _version++; }
            }
        }
    }

    /// <summary>Consumes existing log4net events without posting one UI callback per event.</summary>
    internal sealed class ActivityLogAppender : AppenderSkeleton
    {
        private readonly ActivityLogBuffer _buffer;

        public ActivityLogAppender(ActivityLogBuffer buffer)
        {
            _buffer = buffer;
            Name = "ClientTestActivityLog";
            Threshold = Level.Debug;
        }

        protected override void Append(LoggingEvent loggingEvent)
        {
            string text = string.Format("{0:HH:mm:ss.fff} {1,-5} {2}", loggingEvent.TimeStamp,
                loggingEvent.Level.DisplayName, loggingEvent.RenderedMessage);
            string exception = loggingEvent.GetExceptionString();
            if (!string.IsNullOrEmpty(exception)) text += Environment.NewLine + exception;
            _buffer.Append(text);
        }
    }
}
