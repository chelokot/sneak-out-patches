using Il2CppInterop.Runtime.InteropTypes.Arrays;
using POpusCodec;
using POpusCodec.Enums;

namespace SneakOut.ProximityVoiceChat;

internal sealed class OpusVoiceEncoder : IDisposable
{
    private const int PacketBufferBytes = 4000;

    private readonly Il2CppStructArray<byte> _packetBuffer = new(PacketBufferBytes);
    private IntPtr _handle;

    public OpusVoiceEncoder(int bitrate)
    {
        var handle = Wrapper.opus_encoder_create(
            SamplingRate.Sampling24000,
            Channels.Mono,
            OpusApplicationType.Voip);
        try
        {
            Wrapper.set_opus_encoder_ctl(handle, OpusCtlSetRequest.Bitrate, bitrate);
            Wrapper.set_opus_encoder_ctl(handle, OpusCtlSetRequest.InbandFec, 1);
            Wrapper.set_opus_encoder_ctl(handle, OpusCtlSetRequest.PacketLossPercentage, 30);
            Wrapper.set_opus_encoder_ctl(handle, OpusCtlSetRequest.Dtx, 0);
            _handle = handle;
        }
        catch
        {
            Wrapper.opus_encoder_destroy(handle);
            throw;
        }
    }

    public bool TryEncode(Il2CppStructArray<float> samples, int frameSamples, out byte[] packet)
    {
        if (_handle == IntPtr.Zero)
        {
            throw new ObjectDisposedException(nameof(OpusVoiceEncoder));
        }

        var encodedBytes = Wrapper.opus_encode(
            _handle,
            samples,
            frameSamples,
            _packetBuffer);
        if (encodedBytes <= 1)
        {
            packet = Array.Empty<byte>();
            return false;
        }
        if (encodedBytes > _packetBuffer.Length)
        {
            throw new InvalidOperationException(
                $"Opus returned {encodedBytes} bytes for a {_packetBuffer.Length}-byte packet buffer");
        }

        packet = new byte[encodedBytes];
        for (var index = 0; index < packet.Length; index++)
        {
            packet[index] = _packetBuffer[index];
        }
        return true;
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        Wrapper.opus_encoder_destroy(_handle);
        _handle = IntPtr.Zero;
    }
}
