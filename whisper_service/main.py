import io
import time
import logging
from contextlib import asynccontextmanager
from typing import Annotated, Optional

from fastapi import FastAPI, File, UploadFile, HTTPException, Request
from fastapi.responses import JSONResponse
from faster_whisper import WhisperModel

from logging_config import setup_logging, log_timing

# ── Loglama yapılandırması ───────────────────────────────────────────────────
setup_logging()
logger = logging.getLogger("whisper_service")

# ── Konfigürasyon ─────────────────────────────────────────────────────────────
MODEL_SIZE = "large-v3"  # Daha hızlı denemek için "medium" yapılabilir
DEVICE     = "cpu"       # Apple Silicon MPS henüz stabil değil
COMPUTE    = "int8"      # CPU için en iyi performans/bellek dengesi

# ── Model startup'ta bir kez yüklenir ─────────────────────────────────────────
model: Optional[WhisperModel] = None

@asynccontextmanager
async def lifespan(app: FastAPI):
    global model
    logger.info(f"Whisper modeli yükleniyor: {MODEL_SIZE} / {DEVICE} / {COMPUTE}")
    start = time.perf_counter()
    model = WhisperModel(MODEL_SIZE, device=DEVICE, compute_type=COMPUTE)
    elapsed = time.perf_counter() - start
    logger.info(f"Model hazır ({elapsed:.2f}s)")
    yield
    model = None

app = FastAPI(title="Whisper Transcription Service", version="1.0.0", lifespan=lifespan)

# ── Request timing middleware ─────────────────────────────────────────────────
@app.middleware("http")
async def request_timing_middleware(request: Request, call_next):
    start = time.perf_counter()
    response = await call_next(request)
    duration = time.perf_counter() - start
    logger.info(f"REQUEST {request.method} {request.url.path} -> {response.status_code} in {duration:.3f}s")
    return response

# ── Health check ──────────────────────────────────────────────────────────────
@app.get("/health")
@log_timing(logger)
def health():
    return {"status": "ok", "model": MODEL_SIZE, "device": DEVICE}

# ── Transkripsiyon endpoint'i ─────────────────────────────────────────────────
@app.post("/transcribe")
@log_timing(logger)
async def transcribe(
    audio: Annotated[UploadFile, File(description="Ses dosyası (wav, mp3, m4a, ogg, webm)")],
):
    if model is None:
        raise HTTPException(status_code=503, detail="Model henüz yüklenmedi.")

    audio_bytes = await audio.read()
    if not audio_bytes:
        raise HTTPException(status_code=400, detail="Ses verisi boş.")

    logger.info(f"Dosya alındı: {audio.filename}, boyut: {len(audio_bytes)} byte")

    audio_buffer = io.BytesIO(audio_bytes)
    audio_buffer.name = audio.filename or "audio.wav"

    try:
        segments, info = model.transcribe(
            audio_buffer,
            language="tr",
            beam_size=5,
            vad_filter=True,
            vad_parameters=dict(min_silence_duration_ms=500),
        )

        full_text = " ".join(segment.text.strip() for segment in segments).strip()

        logger.info(f"Sonuç: \"{full_text}\"")
        logger.info(f"Dil: {info.language} (olasılık: {info.language_probability:.2f})")

        return JSONResponse({
            "text": full_text,
            "language": info.language,
            "language_probability": round(info.language_probability, 4),
            "duration_seconds": round(info.duration, 2),
        })

    except Exception as ex:
        logger.error(f"Transkripsiyon HATA: {ex}")
        raise HTTPException(status_code=500, detail=f"Transkripsiyon hatası: {str(ex)}")
