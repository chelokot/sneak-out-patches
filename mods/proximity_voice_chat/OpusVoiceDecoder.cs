using Il2CppInterop.Runtime.InteropTypes.Arrays;
using POpusCodec;
using POpusCodec.Enums;
using Photon.Voice;

namespace SneakOut.ProximityVoiceChat;

internal sealed class OpusVoiceDecoder : IDisposable
{
    private const int MaximumOpusPacketLength = 1275;

    private readonly OpusDecoder<float> _decoder;
    private readonly Il2CppStructArray<byte> _packetBuffer = new(MaximumOpusPacketLength);
    private readonly Action<Il2CppArrayBase<float>> _onDecodedFrame;
    private byte _frameNumber;
    private long _outputFrames;

    public OpusVoiceDecoder(Action<Il2CppArrayBase<float>> onDecodedFrame)
    {
        _onDecodedFrame = onDecodedFrame;
        _decoder = new OpusDecoder<float>(
            (Il2CppSystem.Action<FrameOut<float>>)OnDecoded,
            SamplingRate.Sampling24000,
            Channels.Mono,
            OpusVoiceCapture.FrameSamples);
    }

    public bool TryDecode(byte[] encodedAudio, int missingFramesBefore)
    {
        if (encodedAudio.Length == 0 || encodedAudio.Length > MaximumOpusPacketLength)
        {
            return false;
        }

        var outputFramesBefore = _outputFrames;
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
            _decoder.DecodePacket(ref packet, false);
            return _outputFrames > outputFramesBefore;
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
            _decoder.DecodePacket(ref missingPacket, false);
        }
        finally
        {
            missingPacket.Release();
        }
    }

    private void OnDecoded(FrameOut<float> frame)
    {
        if (!frame.EndOfStream && frame.Buf.Length > 0)
        {
            _outputFrames++;
            _onDecodedFrame(frame.Buf);
        }
    }

    public void Dispose()
    {
        _decoder.Dispose();
    }
}
