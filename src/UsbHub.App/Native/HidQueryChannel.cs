using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace UsbHub.App.Native;

// Used only on whitelisted vendor control collections. No input-event collections are opened.
internal sealed class HidQueryChannel : IDisposable
{
    private readonly FileStream _stream;
    private readonly int _inputLength;
    private readonly int _outputLength;

    public HidQueryChannel(string path, NativeHid.InterfaceCaps caps)
    {
        _inputLength = caps.InputLength;
        _outputLength = caps.OutputLength;
        var handle = NativeHid.CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw new IOException("无法打开厂商电量控制接口，可能被其他程序占用。"); }
        _stream = new FileStream(handle, FileAccess.ReadWrite, 1, true);
    }

    public async Task<byte[]?> QueryAsync(byte[] request, Func<byte[], bool> matches, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(1200));
        var output = new byte[_outputLength];
        request.CopyTo(output, 0);
        await _stream.WriteAsync(output, timeout.Token);
        while (true)
        {
            var input = new byte[_inputLength];
            var length = await _stream.ReadAsync(input, timeout.Token);
            if (length == 0) return null;
            var response = input[..length];
            if (matches(response)) return response;
            // Discard unrelated vendor notifications, never interpret them as battery responses.
        }
    }

    public void Dispose() => _stream.Dispose();
}
