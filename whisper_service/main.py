import io
import logging
from contextlib import asynccontextmanager
from typing import Annotated

from fastapi import FastAPI, File, UploadFile, HTTPException
from fastapi.responses import JSONResponse
from faster_whisper import WhisperModel

# ── Konfigürasyon ─────────────────────────────────────────────────────────────
MODEL_SIZE = "large-v3"  # Daha hızlı denemek için "medium" yapılabilir
DEVICE     = "cpu"       # Apple Silicon MPS henüz stabil değil
COMPUTE    = "int8"      # CPU için en iyi performans/bellek dengesi

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("whisper_service")

# ── Model startup'ta bir kez yüklenir ─────────────────────────────────────────
model: WhisperModel | None = None

@asynccontextmanager
async def lifespan(app: FastAPI):
    global model
    logger.info(f"[Startup] Whisper modeli yükleniyor: {MODEL_SIZE} / {DEVICE} / {COMPUTE}")
    model = WhisperModel(MODEL_SIZE, device=DEVICE, compute_type=COMPUTE)
    logger.info("[Startup] Model hazır.")
    yield
    model = None

app = FastAPI(title="Whisper Transcription Service", version="1.0.0", lifespan=lifespan)

# ── Health check ──────────────────────────────────────────────────────────────
@app.get("/health")
def health():
    return {"status": "ok", "model": MODEL_SIZE, "device": DEVICE}

# ── Transkripsiyon endpoint'i ─────────────────────────────────────────────────
@app.post("/transcribe")
async def transcribe(
    audio: Annotated[UploadFile, File(description="Ses dosyası (wav, mp3, m4a, ogg, webm)")],
):
    if model is None:
        raise HTTPException(status_code=503, detail="Model henüz yüklenmedi.")

    audio_bytes = await audio.read()
    if not audio_bytes:
        raise HTTPException(status_code=400, detail="Ses verisi boş.")

    logger.info(f"[Transcribe] Dosya alındı: {audio.filename}, boyut: {len(audio_bytes)} byte")

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

        logger.info(f"[Transcribe] Sonuç: \"{full_text}\"")
        logger.info(f"[Transcribe] Dil: {info.language} (olasılık: {info.language_probability:.2f})")

        return JSONResponse({
            "text": full_text,
            "language": info.language,
            "language_probability": round(info.language_probability, 4),
            "duration_seconds": round(info.duration, 2),
        })

    except Exception as ex:
        logger.error(f"[Transcribe] HATA: {ex}")
        raise HTTPException(status_code=500, detail=f"Transkripsiyon hatası: {str(ex)}")
