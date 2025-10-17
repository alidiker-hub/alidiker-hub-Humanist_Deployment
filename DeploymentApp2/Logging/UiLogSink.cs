using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DeploymentApp2.Logging
{
    /// <summary>
    /// In-memory, bounded, debounced UI log sink.
    /// Üreticiler Channel.Writer ile log yazar; tek tüketici buffer'a alır.
    /// </summary>
    public sealed class UiLogSink : IAsyncDisposable
    {
        private readonly Channel<UiLogItem> _channel;
        private readonly ConcurrentQueue<string> _buffer = new();
        private readonly int _maxLines;
        private readonly Timer _debounceTimer;
        private int _pendingFlag = 0;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _readerTask;

        public event EventHandler? Changed;

        public UiLogSink(int capacity = 10_000, int maxLines = 5_000, int debounceMs = 120)
        {
            _maxLines = Math.Max(200, maxLines);

            // Tek tüketici performansı için Bounded Channel
            var opts = new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            };
            _channel = Channel.CreateBounded<UiLogItem>(opts);

            _debounceTimer = new Timer(_ =>
            {
                // biriktirilmiş bildirim varsa 1 -> 0'a çek ve event’i ateşle
                if (Interlocked.Exchange(ref _pendingFlag, 0) == 1)
                    Changed?.Invoke(this, EventArgs.Empty);
            }, null, Timeout.Infinite, Timeout.Infinite);

            _readerTask = Task.Run(ReaderLoop);
        }

        public ChannelWriter<UiLogItem> Writer => _channel.Writer;

        private async Task ReaderLoop()
        {
            try
            {
                var reader = _channel.Reader;
                while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
                {
                    while (reader.TryRead(out var item))
                    {
                        var line = $"{item.Timestamp:HH:mm:ss} [{item.Level}] {item.Category}: {item.Message}";
                        if (item.Exception is not null)
                            line += Environment.NewLine + item.Exception;

                        _buffer.Enqueue(line);
                        while (_buffer.Count > _maxLines && _buffer.TryDequeue(out _)) { }
                    }

                    // Debounce bildirimi
                    Interlocked.Exchange(ref _pendingFlag, 1);
                    _debounceTimer.Change(100, Timeout.Infinite);
                }
            }
            catch (OperationCanceledException) { }
        }

        public string[] Snapshot() => _buffer.ToArray();

        public void Clear()
        {
            while (_buffer.TryDequeue(out _)) { }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _debounceTimer.Dispose();
            _channel.Writer.TryComplete();
            try { await _readerTask.ConfigureAwait(false); } catch { }
            _cts.Dispose();
        }
    }

    public readonly record struct UiLogItem(
        DateTime Timestamp,
        LogLevel Level,
        string Category,
        string Message,
        Exception? Exception);
}
