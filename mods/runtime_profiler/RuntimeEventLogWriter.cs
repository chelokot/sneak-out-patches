using System.Collections.Concurrent;
using System.Text;

namespace SneakOut.RuntimeProfiler;

internal sealed class RuntimeEventLogWriter : IDisposable
{
    private readonly ConcurrentQueue<string> _pendingLines = new();
    private readonly AutoResetEvent _linesAvailable = new(false);
    private readonly Thread _writerThread;
    private readonly FileStream _stream;
    private readonly StreamWriter _writer;
    private int _acceptingLines = 1;
    private int _disposed;

    public RuntimeEventLogWriter(string path)
    {
        _stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(_stream, new UTF8Encoding(false));
        _writerThread = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "SneakOut Runtime Event Log Writer"
        };
        _writerThread.Start();
    }

    public void Enqueue(string line)
    {
        if (Volatile.Read(ref _acceptingLines) == 0)
        {
            return;
        }

        _pendingLines.Enqueue(line);
        _linesAvailable.Set();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _acceptingLines, 0);
        _linesAvailable.Set();
        if (Thread.CurrentThread != _writerThread)
        {
            _writerThread.Join(TimeSpan.FromSeconds(2));
        }

        _linesAvailable.Dispose();
    }

    private void WriteLoop()
    {
        try
        {
            using var stream = _stream;
            using var writer = _writer;
            while (Volatile.Read(ref _acceptingLines) != 0 || !_pendingLines.IsEmpty)
            {
                _linesAvailable.WaitOne(TimeSpan.FromMilliseconds(250));
                Drain(writer);
            }

            Drain(writer);
        }
        catch
        {
            // Avoid recursive logging from this thread when the log filesystem itself fails.
        }
    }

    private void Drain(StreamWriter writer)
    {
        var wroteLine = false;
        while (_pendingLines.TryDequeue(out var line))
        {
            writer.WriteLine(line);
            wroteLine = true;
        }

        if (wroteLine)
        {
            writer.Flush();
        }
    }
}
