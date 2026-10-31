"""Loopback-only Whisper service. Audio stays on this PC; no DeepSeek key is used."""
import argparse
import io
import json
import logging
from pathlib import Path
import threading
import wave
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ROOT = Path(__file__).resolve().parent
MODEL_PATH = ROOT / "Cache~" / "base"
SERVICE = "wjy-dog2-whisper-v1"
model = None
model_error = ""
model_lock = threading.Lock()

def load_model(download=False):
    from faster_whisper import WhisperModel
    from faster_whisper.utils import download_model
    if download:
        download_model("base", output_dir=str(MODEL_PATH))
    if not (MODEL_PATH / "model.bin").is_file():
        raise RuntimeError("Whisper model is missing. Run SpeechService/Setup.ps1 first.")
    return WhisperModel(str(MODEL_PATH), device="cpu", compute_type="int8", cpu_threads=4)

def warm_model():
    global model, model_error
    try:
        model = load_model()
        logging.info("Whisper base ready on CPU/int8")
    except Exception as exc:
        model_error = str(exc)
        logging.exception("Could not load speech model")

class Handler(BaseHTTPRequestHandler):
    def reply(self, status, body):
        encoded = json.dumps(body, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(encoded)))
        self.end_headers()
        self.wfile.write(encoded)

    def do_GET(self):
        if self.path == "/health":
            self.reply(200, {"service": SERVICE, "ready": model is not None, "error": model_error})
        else:
            self.reply(404, {"error": "Unknown path"})

    def do_POST(self):
        if self.path != "/transcribe":
            self.reply(404, {"error": "Unknown path"}); return
        if model is None:
            self.reply(503, {"error": model_error or "Whisper is loading"}); return
        try:
            length = int(self.headers.get("Content-Length", "0"))
            if not 44 < length <= 8_000_000:
                self.reply(400, {"error": "Expected PCM WAV under 8 MB"}); return
            payload = self.rfile.read(length)
            import numpy as np
            with wave.open(io.BytesIO(payload), "rb") as audio:
                if audio.getsampwidth() != 2 or audio.getnchannels() not in (1, 2):
                    self.reply(400, {"error": "Expected 16-bit mono or stereo WAV"}); return
                samples = np.frombuffer(audio.readframes(audio.getnframes()), dtype="<i2").astype(np.float32) / 32768
                rms = float(np.sqrt(np.mean(samples * samples))) if samples.size else 0
            if rms < 0.0025:
                self.reply(200, {"text": "", "rms": rms}); return
            with model_lock:
                segments, _ = model.transcribe(
                    io.BytesIO(payload), language="zh", beam_size=5, vad_filter=True,
                    condition_on_previous_text=False,
                )
                text = "".join(segment.text for segment in segments).strip()
            self.reply(200, {"text": text, "rms": rms})
            logging.info("Transcribed audio: %s bytes, %s text characters", length, len(text))
        except (ValueError, wave.Error) as exc:
            self.reply(400, {"error": str(exc)})
        except Exception:
            logging.exception("Transcription failed")
            self.reply(500, {"error": "Local transcription failed; see SpeechService/Logs~/service.log"})

    def log_message(self, fmt, *args):
        logging.info(fmt, *args)

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--download-only", action="store_true")
    parser.add_argument("--port", type=int, default=8765)
    args = parser.parse_args()
    if args.download_only:
        load_model(download=True)
        print("Whisper base downloaded and loaded successfully.")
    else:
        (ROOT / "Logs~").mkdir(exist_ok=True)
        logging.basicConfig(filename=ROOT / "Logs~" / "service.log", level=logging.INFO, encoding="utf-8")
        server = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
        threading.Thread(target=warm_model, daemon=True).start()
        logging.info("Speech service listening on 127.0.0.1:%s", args.port)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass
        finally:
            server.server_close()