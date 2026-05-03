"""
TTS RabbitMQ Worker (gTTS)
--------------------------
Listens on `tts.requests` queue. Each message body is JSON: {"text": "..."}.
Uses gTTS (Google TTS) to generate MP3, converts to WAV via ffmpeg.
Reply body is raw WAV bytes (audio/wav). On error reply is JSON {"ok": false, "error": "..."}.
RPC pattern: publishes reply to message.reply_to with same correlation_id.
"""

import json
import logging
import os
import signal
import subprocess
import sys
import tempfile
import time

import pika
from gtts import gTTS
from pika.adapters.blocking_connection import BlockingChannel

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
TTS_LANG      = os.getenv("TTS_LANG", "tr")
TTS_SAMPLE_RATE = int(os.getenv("TTS_SAMPLE_RATE", "22050"))


@log_timing(logger)
def synthesize(text: str) -> bytes:
    """gTTS ile MP3 üret, ffmpeg ile WAV'a dönüştür, bytes döndür."""
    text = (text or "").strip()
    if not text:
        raise ValueError("Metin boş olamaz.")

    mp3_path = wav_path = None
    try:
        # 1. gTTS → MP3
        with tempfile.NamedTemporaryFile(suffix=".mp3", delete=False) as f:
            mp3_path = f.name
        gTTS(text=text, lang=TTS_LANG, slow=False).save(mp3_path)

        # 2. ffmpeg → WAV (mono, 22050 Hz)
        with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as f:
            wav_path = f.name
        subprocess.run(
            [
                "ffmpeg", "-y", "-i", mp3_path,
                "-ar", str(TTS_SAMPLE_RATE),
                "-ac", "1",
                wav_path,
            ],
            check=True,
            capture_output=True,
        )

        with open(wav_path, "rb") as f:
            wav_bytes = f.read()

        logger.info(f"WAV üretildi: {len(wav_bytes)} byte")
        return wav_bytes

    finally:
        for path in (mp3_path, wav_path):
            if path and os.path.exists(path):
                os.unlink(path)


def on_message(ch: BlockingChannel, method, properties, body: bytes):
    correlation_id = properties.correlation_id
    reply_to       = properties.reply_to

    logger.info(
        f"Mesaj alındı | size={len(body)} byte | corr_id={correlation_id} | reply_to={reply_to}"
    )

    if not reply_to:
        logger.error("reply_to header eksik, mesaj reddediliyor.")
        ch.basic_ack(delivery_tag=method.delivery_tag)
        return

    try:
        payload  = json.loads(body.decode("utf-8"))
        text     = payload.get("text", "")
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
    parameters  = pika.ConnectionParameters(
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
            channel    = connection.channel()

            channel.queue_declare(queue=TTS_QUEUE, durable=False)
            channel.basic_qos(prefetch_count=1)
            channel.basic_consume(queue=TTS_QUEUE, on_message_callback=on_message, auto_ack=False)

            logger.info(f"Kuyruk dinleniyor: {TTS_QUEUE} | dil: {TTS_LANG}")
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
