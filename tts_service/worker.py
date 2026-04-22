"""
TTS RabbitMQ Worker
-------------------
Listens on `tts.requests` queue. Each message body is JSON: {"text": "..."}.
Reply body is raw WAV bytes (audio/wav). On error reply is JSON {"ok": false, "error": "..."}.
RPC pattern: publishes reply to message.reply_to with same correlation_id.
"""

import io
import json
import os
import time
import wave
import logging
import signal
import sys

import numpy as np
import pika
from pika.adapters.blocking_connection import BlockingChannel
from TTS.api import TTS as CoquiTTS

from logging_config import setup_logging, log_timing

# ── Logging ──────────────────────────────────────────────────────────────────
setup_logging()
logger = logging.getLogger("tts_worker")

# ── Config ───────────────────────────────────────────────────────────────────
RABBITMQ_HOST = os.getenv("RABBITMQ_HOST", "localhost")
RABBITMQ_PORT = int(os.getenv("RABBITMQ_PORT", "5672"))
RABBITMQ_USER = os.getenv("RABBITMQ_USER", "guest")
RABBITMQ_PASS = os.getenv("RABBITMQ_PASS", "guest")
TTS_QUEUE     = os.getenv("TTS_QUEUE", "tts.requests")

TTS_MODEL_NAME  = os.getenv("TTS_MODEL", "tts_models/tr/common-voice/glow-tts")
TTS_SAMPLE_RATE = int(os.getenv("TTS_SAMPLE_RATE", "22050"))

# ── Model (load once at startup) ─────────────────────────────────────────────
logger.info(f"TTS modeli yükleniyor: {TTS_MODEL_NAME}")
_t0 = time.perf_counter()
tts_model: CoquiTTS = CoquiTTS(TTS_MODEL_NAME, progress_bar=False, gpu=False)
logger.info(f"TTS hazır ({time.perf_counter() - _t0:.2f}s)")


@log_timing(logger)
def synthesize(text: str) -> bytes:
    text = (text or "").strip()
    if not text:
        raise ValueError("Metin boş olamaz.")

    samples = tts_model.tts(text=text)
    samples_int16 = (np.array(samples, dtype=np.float32) * 32767).astype(np.int16)

    buf = io.BytesIO()
    with wave.open(buf, "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(TTS_SAMPLE_RATE)
        wf.writeframes(samples_int16.tobytes())

    audio_bytes = buf.getvalue()
    logger.info(f"WAV üretildi: {len(audio_bytes)} byte")
    return audio_bytes


def on_message(ch: BlockingChannel, method, properties, body: bytes):
    correlation_id = properties.correlation_id
    reply_to = properties.reply_to

    logger.info(
        f"Mesaj alındı | size={len(body)} byte | corr_id={correlation_id} | reply_to={reply_to}"
    )

    if not reply_to:
        logger.error("reply_to header eksik, mesaj reddediliyor.")
        ch.basic_ack(delivery_tag=method.delivery_tag)
        return

    try:
        payload = json.loads(body.decode("utf-8"))
        text = payload.get("text", "")
        wav_bytes = synthesize(text)

        ch.basic_publish(
            exchange="",
            routing_key=reply_to,
            properties=pika.BasicProperties(
                correlation_id=correlation_id,
                content_type="audio/wav",
            ),
            body=wav_bytes,
        )
        logger.info(f"Yanıt gönderildi | corr_id={correlation_id} | size={len(wav_bytes)} byte")
    except Exception as ex:
        logger.exception(f"TTS hatası: {ex}")
        error_body = json.dumps({"ok": False, "error": str(ex)}).encode("utf-8")
        try:
            ch.basic_publish(
                exchange="",
                routing_key=reply_to,
                properties=pika.BasicProperties(
                    correlation_id=correlation_id,
                    content_type="application/json",
                ),
                body=error_body,
            )
        except Exception:
            logger.exception("Hata yanıtı da gönderilemedi.")

    ch.basic_ack(delivery_tag=method.delivery_tag)


def main():
    credentials = pika.PlainCredentials(RABBITMQ_USER, RABBITMQ_PASS)
    parameters = pika.ConnectionParameters(
        host=RABBITMQ_HOST,
        port=RABBITMQ_PORT,
        credentials=credentials,
        heartbeat=60,
        blocked_connection_timeout=300,
    )

    while True:
        try:
            logger.info(f"RabbitMQ'ya bağlanılıyor: {RABBITMQ_HOST}:{RABBITMQ_PORT}")
            connection = pika.BlockingConnection(parameters)
            channel = connection.channel()

            channel.queue_declare(queue=TTS_QUEUE, durable=False)
            channel.basic_qos(prefetch_count=1)
            channel.basic_consume(queue=TTS_QUEUE, on_message_callback=on_message, auto_ack=False)

            logger.info(f"Kuyruk dinleniyor: {TTS_QUEUE}")
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
    signal.signal(signal.SIGTERM, lambda *_: sys.exit(0))
    main()
