"""
Whisper RabbitMQ Worker
-----------------------
Listens on `whisper.requests` queue. Each message body is raw audio bytes.
Headers carry x-filename. Reply is JSON: {text, language, language_probability, duration_seconds}.
Replies via RPC pattern: publishes to message.reply_to with same correlation_id.
"""

import io
import json
import os
import time
import logging
import signal
import sys
from typing import Optional

import pika
from pika.adapters.blocking_connection import BlockingChannel
from faster_whisper import WhisperModel

from logging_config import setup_logging, log_timing

# ── Logging ──────────────────────────────────────────────────────────────────
setup_logging()
logger = logging.getLogger("whisper_worker")

# ── Config ───────────────────────────────────────────────────────────────────
RABBITMQ_HOST = os.getenv("RABBITMQ_HOST", "localhost")
RABBITMQ_PORT = int(os.getenv("RABBITMQ_PORT", "5672"))
RABBITMQ_USER = os.getenv("RABBITMQ_USER", "guest")
RABBITMQ_PASS = os.getenv("RABBITMQ_PASS", "guest")
WHISPER_QUEUE = os.getenv("WHISPER_QUEUE", "whisper.requests")

MODEL_SIZE = os.getenv("WHISPER_MODEL", "large-v3")
DEVICE     = os.getenv("WHISPER_DEVICE", "cpu")
COMPUTE    = os.getenv("WHISPER_COMPUTE", "int8")

# ── Model (load once at startup) ─────────────────────────────────────────────
logger.info(f"Whisper modeli yükleniyor: {MODEL_SIZE} / {DEVICE} / {COMPUTE}")
_t0 = time.perf_counter()
model: WhisperModel = WhisperModel(MODEL_SIZE, device=DEVICE, compute_type=COMPUTE)
logger.info(f"Whisper hazır ({time.perf_counter() - _t0:.2f}s)")


@log_timing(logger)
def transcribe_audio(audio_bytes: bytes, filename: str) -> dict:
    """Transcribe a single audio blob and return the result dict."""
    if not audio_bytes:
        raise ValueError("Boş ses verisi.")

    buf = io.BytesIO(audio_bytes)
    buf.name = filename or "audio.wav"

    segments, info = model.transcribe(
        buf,
        language="tr",
        beam_size=5,
        vad_filter=True,
        vad_parameters=dict(min_silence_duration_ms=500),
    )
    full_text = " ".join(segment.text.strip() for segment in segments).strip()

    logger.info(f"Sonuç: \"{full_text}\" (lang={info.language}, prob={info.language_probability:.2f})")

    return {
        "text": full_text,
        "language": info.language,
        "language_probability": round(info.language_probability, 4),
        "duration_seconds": round(info.duration, 2),
    }


def on_message(ch: BlockingChannel, method, properties, body: bytes):
    correlation_id = properties.correlation_id
    reply_to = properties.reply_to
    headers = properties.headers or {}
    raw_filename = headers.get("x-filename", "audio.webm")
    # AMQP string header'ları pika'ya bytes olarak gelir, decode et
    filename = raw_filename.decode("utf-8") if isinstance(raw_filename, bytes) else raw_filename

    logger.info(
        f"Mesaj alındı | size={len(body)} byte | filename={filename} | "
        f"corr_id={correlation_id} | reply_to={reply_to}"
    )

    if not reply_to:
        logger.error("reply_to header eksik, mesaj reddediliyor.")
        ch.basic_ack(delivery_tag=method.delivery_tag)
        return

    try:
        result = transcribe_audio(body, filename)
        response_body = json.dumps({"ok": True, "result": result}).encode("utf-8")
    except Exception as ex:
        logger.exception(f"Transkripsiyon hatası: {ex}")
        response_body = json.dumps({"ok": False, "error": str(ex)}).encode("utf-8")

    try:
        ch.basic_publish(
            exchange="",
            routing_key=reply_to,
            properties=pika.BasicProperties(
                correlation_id=correlation_id,
                content_type="application/json",
            ),
            body=response_body,
        )
        logger.info(f"Yanıt gönderildi | corr_id={correlation_id} | size={len(response_body)} byte")
    except Exception as ex:
        logger.exception(f"Yanıt gönderilemedi: {ex}")

    ch.basic_ack(delivery_tag=method.delivery_tag)


def main():
    credentials = pika.PlainCredentials(RABBITMQ_USER, RABBITMQ_PASS)
    parameters = pika.ConnectionParameters(
        host=RABBITMQ_HOST,
        port=RABBITMQ_PORT,
        credentials=credentials,
        heartbeat=600,          # Whisper large-v3 CPU'da 90s+ sürebilir
        blocked_connection_timeout=300,
    )

    # Reconnect loop
    while True:
        try:
            logger.info(f"RabbitMQ'ya bağlanılıyor: {RABBITMQ_HOST}:{RABBITMQ_PORT}")
            connection = pika.BlockingConnection(parameters)
            channel = connection.channel()

            channel.queue_declare(queue=WHISPER_QUEUE, durable=False)
            # Tek tek tüket — Whisper CPU-bound, paralel iş yok
            channel.basic_qos(prefetch_count=1)
            channel.basic_consume(queue=WHISPER_QUEUE, on_message_callback=on_message, auto_ack=False)

            logger.info(f"Kuyruk dinleniyor: {WHISPER_QUEUE}")
            channel.start_consuming()
        except pika.exceptions.AMQPConnectionError as ex:
            logger.warning(f"RabbitMQ bağlantısı koptu, 5s sonra yeniden denenecek: {ex}")
            time.sleep(5)
        except KeyboardInterrupt:
            logger.info("Durduruluyor (KeyboardInterrupt)")
            try:
                channel.stop_consuming()
                connection.close()
            except Exception:
                pass
            sys.exit(0)
        except Exception as ex:
            logger.exception(f"Beklenmeyen hata: {ex}, 5s sonra yeniden denenecek")
            time.sleep(5)


if __name__ == "__main__":
    # SIGTERM (docker stop) için temiz kapanış
    signal.signal(signal.SIGTERM, lambda *_: sys.exit(0))
    main()
