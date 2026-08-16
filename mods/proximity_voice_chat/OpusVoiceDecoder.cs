using Il2CppInterop.Runtime.InteropTypes.Arrays;
using POpusCodec;
using POpusCodec.Enums;
using Photon.Voice;

namespace SneakOut.ProximityVoiceChat;

internal sealed class OpusVoiceDecoder : IDisposable
{
    private const int MaximumOpusPacketLength = 1275;

    private readonly Il2CppStructArray<byte> _packetBuffer = new(MaximumOpusPacketLength);
    private readonly Il2CppStructArray<float> _pcmBuffer = new(OpusVoiceCapture.FrameSamples);
    private readonly Action<Il2CppArrayBase<float>> _onDecodedFrame;
    private IntPtr _handle;
    private byte _frameNumber;

    public OpusVoiceDecoder(Action<Il2CppArrayBase<float>> onDecodedFrame)
    {
        _onDecodedFrame = onDecodedFrame;
        _handle = Wrapper.opus_decoder_create(
            SamplingRate.Sampling24000,
            Channels.Mono);
    }

    public bool TryDecode(byte[] encodedAudio, int missingFramesBefore)
    {
        if (encodedAudio.Length == 0 || encodedAudio.Length > MaximumOpusPacketLength)
        {
            return false;
        }

        for (var index = 0; index < missingFramesBefore; index++)
        {
            DecodeMissingFrame();
        }

        for (var index = 0; index < encodedAudio.Length; index++)
        {
            _packetBuffer[index] = encodedAudio[index];
        }

        var packet = new FrameBuffer(
            _packetBuffer,
            0,
            encodedAudio.Length,
            0,
            _frameNumber++,
            null!);
        try
        {
            return DecodePacket(packet);
        }
        finally
        {
            packet.Release();
        }
    }

    private void DecodeMissingFrame()
    {
        var missingPacket = new FrameBuffer(
            null!,
            (FrameFlags)0,
            _frameNumber++);
        try
        {
            DecodePacket(missingPacket);
        }
        finally
        {
            missingPacket.Release();
        }
    }

    private bool DecodePacket(FrameBuffer packet)
    {
        if (_handle == IntPtr.Zero)
        {
            throw new ObjectDisposedException(nameof(OpusVoiceDecoder));
        }

        var decodedSamples = Wrapper.opus_decode(
            _handle,
            packet,
            _pcmBuffer,
            OpusVoiceCapture.FrameSamples,
            0);
        if (decodedSamples <= 0)
        {
            return false;
        }
        if (decodedSamples != OpusVoiceCapture.FrameSamples)
        {
            throw new InvalidOperationException(
                $"Opus decoded {decodedSamples} samples for a {OpusVoiceCapture.FrameSamples}-sample frame");
        }

        _onDecodedFrame(_pcmBuffer);
        return true;
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        Wrapper.opus_decoder_destroy(_handle);
        _handle = IntPtr.Zero;
    }
}
