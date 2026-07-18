using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace VoskVoiceTester;

internal sealed class WaveInRecorder : IDisposable
{
    private const int SampleRate = 16000;
    private const short Channels = 1;
    private const short BitsPerSample = 16;
    private const int BufferMilliseconds = 100;
    private const int BufferCount = 4;
    private const int CallbackFunction = 0x00030000;
    private const int WaveMapper = -1;
    private const int MmWimData = 0x3C0;

    private readonly ConcurrentQueue<byte[]> chunks = new();
    private readonly List<BufferState> buffers = new();
    private WaveInProc? callback;
    private IntPtr handle;
    private bool running;

    public bool IsRunning => running;

    public event Action<byte[]>? DataAvailable;

    public void Start()
    {
        if (running)
            return;

        callback = OnWaveIn;
        var format = new WaveFormat
        {
            wFormatTag = 1,
            nChannels = Channels,
            nSamplesPerSec = SampleRate,
            wBitsPerSample = BitsPerSample,
            nBlockAlign = (short)(Channels * BitsPerSample / 8),
            nAvgBytesPerSec = SampleRate * Channels * BitsPerSample / 8,
            cbSize = 0,
        };

        Check(waveInOpen(out handle, WaveMapper, ref format, callback, IntPtr.Zero, CallbackFunction), "waveInOpen");

        int bufferBytes = SampleRate * Channels * BitsPerSample / 8 * BufferMilliseconds / 1000;
        for (int i = 0; i < BufferCount; i++)
        {
            var state = new BufferState(bufferBytes);
            buffers.Add(state);
            PrepareAndQueue(state);
        }

        running = true;
        Check(waveInStart(handle), "waveInStart");
    }

    public void Stop()
    {
        if (handle == IntPtr.Zero)
            return;

        running = false;
        waveInReset(handle);
        foreach (BufferState state in buffers)
        {
            if (state.HeaderPtr != IntPtr.Zero)
                waveInUnprepareHeader(handle, state.HeaderPtr, Marshal.SizeOf<WaveHeader>());
            state.Dispose();
        }

        buffers.Clear();
        waveInClose(handle);
        handle = IntPtr.Zero;
        while (chunks.TryDequeue(out _))
        {
        }
    }

    public void Dispose()
    {
        Stop();
        callback = null;
    }

    private void PrepareAndQueue(BufferState state)
    {
        Check(waveInPrepareHeader(handle, state.HeaderPtr, Marshal.SizeOf<WaveHeader>()), "waveInPrepareHeader");
        Check(waveInAddBuffer(handle, state.HeaderPtr, Marshal.SizeOf<WaveHeader>()), "waveInAddBuffer");
    }

    private void OnWaveIn(IntPtr hwi, int message, IntPtr instance, IntPtr param1, IntPtr param2)
    {
        if (message != MmWimData || !running)
            return;

        var header = Marshal.PtrToStructure<WaveHeader>(param1);
        if (header.dwBytesRecorded > 0)
        {
            var bytes = new byte[header.dwBytesRecorded];
            Marshal.Copy(header.lpData, bytes, 0, bytes.Length);
            DataAvailable?.Invoke(bytes);
        }

        waveInUnprepareHeader(handle, param1, Marshal.SizeOf<WaveHeader>());
        BufferState? state = buffers.FirstOrDefault(candidate => candidate.HeaderPtr == param1);
        if (state == null || !running)
            return;

        state.ResetHeader();
        PrepareAndQueue(state);
    }

    private static void Check(int result, string operation)
    {
        if (result != 0)
            throw new InvalidOperationException($"{operation} failed with MMRESULT {result}.");
    }

    private sealed class BufferState : IDisposable
    {
        private readonly GCHandle dataHandle;
        private readonly GCHandle headerHandle;
        private WaveHeader header;

        public IntPtr HeaderPtr => headerHandle.AddrOfPinnedObject();

        public BufferState(int bytes)
        {
            var data = new byte[bytes];
            dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
            header = new WaveHeader
            {
                lpData = dataHandle.AddrOfPinnedObject(),
                dwBufferLength = bytes,
            };
            headerHandle = GCHandle.Alloc(header, GCHandleType.Pinned);
        }

        public void ResetHeader()
        {
            header.dwBytesRecorded = 0;
            header.dwFlags = 0;
            Marshal.StructureToPtr(header, HeaderPtr, false);
        }

        public void Dispose()
        {
            if (headerHandle.IsAllocated)
                headerHandle.Free();
            if (dataHandle.IsAllocated)
                dataHandle.Free();
        }
    }

    private delegate void WaveInProc(IntPtr hwi, int uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormat
    {
        public short wFormatTag;
        public short nChannels;
        public int nSamplesPerSec;
        public int nAvgBytesPerSec;
        public short nBlockAlign;
        public short wBitsPerSample;
        public short cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr lpData;
        public int dwBufferLength;
        public int dwBytesRecorded;
        public IntPtr dwUser;
        public int dwFlags;
        public int dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [DllImport("winmm.dll")]
    private static extern int waveInOpen(out IntPtr hWaveIn, int uDeviceID, ref WaveFormat lpFormat, WaveInProc dwCallback, IntPtr dwInstance, int dwFlags);

    [DllImport("winmm.dll")]
    private static extern int waveInPrepareHeader(IntPtr hWaveIn, IntPtr lpWaveInHdr, int uSize);

    [DllImport("winmm.dll")]
    private static extern int waveInUnprepareHeader(IntPtr hWaveIn, IntPtr lpWaveInHdr, int uSize);

    [DllImport("winmm.dll")]
    private static extern int waveInAddBuffer(IntPtr hWaveIn, IntPtr lpWaveInHdr, int uSize);

    [DllImport("winmm.dll")]
    private static extern int waveInStart(IntPtr hWaveIn);

    [DllImport("winmm.dll")]
    private static extern int waveInReset(IntPtr hWaveIn);

    [DllImport("winmm.dll")]
    private static extern int waveInClose(IntPtr hWaveIn);
}
