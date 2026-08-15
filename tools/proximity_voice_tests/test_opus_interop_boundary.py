#!/usr/bin/env python3

from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
CAPTURE_SOURCE = REPOSITORY_ROOT / "mods" / "proximity_voice_chat" / "OpusVoiceCapture.cs"
ENCODER_SOURCE = REPOSITORY_ROOT / "mods" / "proximity_voice_chat" / "OpusVoiceEncoder.cs"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


capture = CAPTURE_SOURCE.read_text(encoding="utf-8")
encoder = ENCODER_SOURCE.read_text(encoding="utf-8")
module_source = "\n".join(
    path.read_text(encoding="utf-8")
    for path in sorted(CAPTURE_SOURCE.parent.glob("*.cs"))
)

for forbidden in (
    "Il2CppSystem.ArraySegment",
    "POpusCodec.OpusEncoder",
    "OnEncodedFrame",
):
    require(
        forbidden not in module_source,
        f"non-blittable Opus callback boundary returned: {forbidden}",
    )

require(
    "Il2CppSystem.Action<Il2CppSystem.ArraySegment" not in module_source,
    "non-blittable IL2CPP ArraySegment delegate returned",
)
require(
    ".Output =" not in capture and ".Output =" not in encoder,
    "Opus output callback assignment returned",
)

for required in (
    "Wrapper.opus_encoder_create",
    "Wrapper.opus_encode",
    "Wrapper.opus_encoder_destroy",
    "OpusCtlSetRequest.Bitrate",
    "OpusCtlSetRequest.InbandFec",
    "OpusCtlSetRequest.PacketLossPercentage",
    "OpusCtlSetRequest.Dtx",
):
    require(required in encoder, f"direct Opus lifecycle operation is missing: {required}")

require(
    "encodedBytes > _packetBuffer.Length" in encoder,
    "Opus packet length is not validated before copying",
)
require(
    encoder.count("Wrapper.opus_encoder_destroy") == 2,
    "Opus handle is not destroyed on both constructor failure and disposal",
)
require(
    "if (_handle == IntPtr.Zero)" in encoder,
    "Opus encoder disposal is not idempotent",
)

print("Proximity voice Opus interop boundary test passed.")
