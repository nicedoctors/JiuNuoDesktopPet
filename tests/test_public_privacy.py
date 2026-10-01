"""Offline metadata-cleanup invariants; fixtures contain no real personal data."""
import hashlib
import io
import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
from public_privacy import wav_clean
from strip_image_metadata import clean_png
from PIL import Image, PngImagePlugin


class MediaPrivacyTests(unittest.TestCase):
    def test_png_pixels_and_alpha_survive_description_removal(self):
        source = Image.new("RGBA", (8, 8), (120, 20, 40, 100))
        info = PngImagePlugin.PngInfo()
        info.add_text("Author", "private-fixture")
        info.add(b"gAMA", struct.pack(">I", 45455))
        buffer = io.BytesIO()
        source.save(buffer, format="PNG", pnginfo=info)
        cleaned = clean_png(buffer.getvalue())
        self.assertNotIn(b"private-fixture", cleaned.data)
        self.assertIn(b"gAMA", cleaned.data)
        with Image.open(io.BytesIO(cleaned.data)) as result:
            self.assertEqual(source.tobytes(), result.convert("RGBA").tobytes())
        self.assertEqual((), clean_png(cleaned.data).metadata)

    def test_wav_pcm_and_format_survive_info_removal(self):
        def chunk(kind, payload):
            return kind + struct.pack("<I", len(payload)) + payload + b"\0" * (len(payload) % 2)
        format_bytes = struct.pack("<HHIIHH", 1, 1, 48000, 96000, 2, 16)
        pcm = bytes(range(32))
        wave = b"WAVE" + chunk(b"fmt ", format_bytes) + chunk(b"LIST", b"INFO" + chunk(b"IART", b"private-fixture\0")) + chunk(b"data", pcm)
        original = b"RIFF" + struct.pack("<I", len(wave)) + wave
        cleaned, metadata, digest = wav_clean(original)
        self.assertNotIn(b"private-fixture", cleaned)
        self.assertIn(chunk(b"fmt ", format_bytes), cleaned)
        self.assertIn(chunk(b"data", pcm), cleaned)
        self.assertEqual(len(cleaned), struct.unpack_from("<I", cleaned, 4)[0] + 8)
        self.assertEqual(digest, wav_clean(cleaned)[2])
        self.assertEqual([], wav_clean(cleaned)[1])
        self.assertEqual(["WAV:LIST"], metadata)


if __name__ == "__main__":
    unittest.main()
