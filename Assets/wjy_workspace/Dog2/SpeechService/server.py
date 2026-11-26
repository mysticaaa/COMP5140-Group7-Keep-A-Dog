"""Loopback-only Whisper service. Audio stays on this PC; no DeepSeek key is used."""
import argparse
import io
import json
import logging
import os
from pathlib import Path
import secrets
import threading
import wave
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ROOT = Path(__file__).resolve().parent
MODEL_NAME = os.environ.get("CITYDOG_WHISPER_MODEL", "auto")
if MODEL_NAME == "auto":
    MODEL_NAME = "small" if (ROOT / "Cache~" / "small" / "model.bin").is_file() else "base"
if MODEL_NAME not in ("base", "small"):
    raise ValueError("CITYDOG_WHISPER_MODEL must be auto, base or small")
MODEL_PATH = ROOT / "Cache~" / MODEL_NAME
SERVICE = "wjy-dog2-whisper-v1"
TRAINING_SERVICE = "wjy-dog1-training-draw-v1"
model = None
model_error = ""
model_lock = threading.Lock()

def load_model(download=False):
    from faster_whisper import WhisperModel
    from faster_whisper.utils import download_model
    from faster_whisper.vad import get_vad_model
    if download:
        download_model(MODEL_NAME, output_dir=str(MODEL_PATH))
    if not (MODEL_PATH / "model.bin").is_file():
        raise RuntimeError("Whisper model is missing. Run SpeechService/Setup.ps1 first.")
    get_vad_model()  # Validate/prewarm this dependency before advertising readiness.
    return WhisperModel(str(MODEL_PATH), device="cpu", compute_type="int8",
                        cpu_threads=min(8, max(2, (os.cpu_count() or 4) // 2)))

def warm_model():
    global model, model_error
    try:
        model = load_model()
        logging.info("Whisper %s ready on CPU/int8", MODEL_NAME)
    except Exception as exc:
        model_error = str(exc)
        logging.exception("Could not load speech model")

def training_draw(success_rate):
    if type(success_rate) is not int or not 0 <= success_rate <= 100:
        raise ValueError("Expected an integer successRate from 0 to 100")
    roll = secrets.randbelow(100) + 1
    return {"service": TRAINING_SERVICE, "successRate": success_rate, "roll": roll,
            "status": "success" if roll <= success_rate else "failure"}

class Handler(BaseHTTPRequestHandler):
    def reply(self, status, body):
        encoded = json.dumps(body, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(encoded)))
        self.end_headers()
        self.wfile.write(encoded)

    def do_GET(self):
        if self.path == "/health":
            self.reply(200, {"service": SERVICE, "ready": model is not None, "error": model_error, "model": MODEL_NAME})
        else:
            self.reply(404, {"error": "Unknown path"})

    def do_POST(self):
        if self.path == "/training-draw":
            try:
                length = int(self.headers.get("Content-Length", "0"))
                if not 0 < length <= 128:
                    raise ValueError("Expected a small JSON body with a successRate")
                payload = json.loads(self.rfile.read(length))
                if not isinstance(payload, dict):
                    raise ValueError("Expected a JSON object")
                result = training_draw(payload.get("successRate"))
            except ValueError as exc:
                self.reply(400, {"error": str(exc)})
            else:
                self.reply(200, result)
            return
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
                rate = audio.getframerate()
                channels = audio.getnchannels()
                if not 8000 <= rate <= 96000:
                    self.reply(400, {"error": "Unsupported sample rate"}); return
                samples = np.frombuffer(audio.readframes(audio.getnframes()), dtype="<i2").astype(np.float32) / 32768
                samples = samples.reshape(-1, channels).mean(axis=1)
            if samples.size < rate // 5 or samples.size > rate * 61:
                self.reply(400, {"error": "Expected 0.2 to 61 seconds of audio"}); return
            samples -= samples.mean()  # Reject DC offset as well as digital silence.
            rms = float(np.sqrt(np.mean(samples * samples)))
            peak = float(np.max(np.abs(samples)))
            if rms < 0.00015 or peak < 0.001:
                self.reply(200, {"text": "", "rms": rms}); return
            # Moderate gain helps quiet headset microphones; VAD still rejects noise.
            gain = min(8.0, .08 / max(rms, 1e-8), .95 / max(peak, 1e-8))
            samples *= max(1.0, gain)
            normalized = io.BytesIO()
            with wave.open(normalized, "wb") as audio:
                audio.setnchannels(1); audio.setsampwidth(2); audio.setframerate(rate)
                audio.writeframes((np.clip(samples, -1, 1) * 32767).astype("<i2").tobytes())
            normalized.seek(0)
            with model_lock:
                segments, _ = model.transcribe(
                    normalized, language="zh", beam_size=3, temperature=0,
                    vad_filter=True,
                    vad_parameters={"min_speech_duration_ms": 120, "min_silence_duration_ms": 300, "speech_pad_ms": 200},
                    initial_prompt="简体中文。和小狗麦粒交流：坐下，趴下，过来，转圈，抬右爪，把玩具拿来，真棒。",
                    condition_on_previous_text=False, no_speech_threshold=0.6,
                )
                text = "".join(segment.text for segment in segments
                               if segment.avg_logprob >= -1.0 and segment.no_speech_prob < .75).strip()
            self.reply(200, {"text": text, "rms": rms, "model": MODEL_NAME})
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
    parser.add_argument("--port", type=int, default=18765)
    args = parser.parse_args()
    if args.download_only:
        load_model(download=True)
        print(f"Whisper {MODEL_NAME} downloaded and loaded successfully.")
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
