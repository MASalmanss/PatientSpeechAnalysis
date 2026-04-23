"""
Symptom History Worker
======================
Hasta semptom geçmişini canonical key bazında yönetir.
- .NET'ten yapılandırılmış semptom listesi (canonical_key dahil) alır
- Aynı hasta + aynı canonical_key geçmişte varsa uyarı döner
- Her durumda yeni semptomları kendi SQLite DB'sine kaydeder
- İletişim: RabbitMQ RPC (direct reply-to)
- Bağımlılık: sadece pika (stdlib: sqlite3, json, logging, os, time)
"""

import json
import logging
import os
import sqlite3
import time

import pika
import pika.exceptions

# ── Loglama ─────────────────────────────────────────────────────────────────
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s | %(levelname)-8s | %(message)s",
    datefmt="%Y-%m-%d %H:%M:%S",
)
logger = logging.getLogger("symptom-worker")

# ── Config ───────────────────────────────────────────────────────────────────
RABBITMQ_HOST = os.getenv("RABBITMQ_HOST", "localhost")
RABBITMQ_PORT = int(os.getenv("RABBITMQ_PORT", "5672"))
RABBITMQ_USER = os.getenv("RABBITMQ_USER", "guest")
RABBITMQ_PASS = os.getenv("RABBITMQ_PASS", "guest")
SYMPTOM_QUEUE = os.getenv("SYMPTOM_QUEUE", "symptom.check")
DB_PATH       = os.getenv("DB_PATH", "/data/symptom_history.db")


# ── Veritabanı ───────────────────────────────────────────────────────────────

def get_db() -> sqlite3.Connection:
    os.makedirs(os.path.dirname(DB_PATH), exist_ok=True)
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    return conn


def init_db() -> None:
    """DB'yi oluştur. Eski Jaccard tablosu varsa migrate et."""
    with get_db() as conn:
        # Eski şema kontrolü: keywords kolonu varsa eski tablodur, drop et
        cols = {row[1] for row in conn.execute("PRAGMA table_info(symptom_history)")}
        if cols and "keywords" in cols:
            logger.warning("Eski şema tespit edildi (Jaccard tablosu). Yeniden oluşturuluyor...")
            conn.execute("DROP TABLE symptom_history")

        conn.execute("""
            CREATE TABLE IF NOT EXISTS symptom_history (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                patient_id    INTEGER NOT NULL,
                canonical_key TEXT NOT NULL,
                semptom       TEXT NOT NULL,
                zaman         TEXT,
                siklik        TEXT,
                siddet_seyri  TEXT,
                tetikleyici   TEXT,        -- JSON array
                analyzed_at   TEXT NOT NULL
            )
        """)
        conn.execute("""
            CREATE INDEX IF NOT EXISTS idx_patient_canonical
            ON symptom_history (patient_id, canonical_key)
        """)
    logger.info("DB hazır: %s", DB_PATH)


# ── İş mantığı ───────────────────────────────────────────────────────────────

def find_matches(conn: sqlite3.Connection, patient_id: int, canonical_keys: list[str]) -> list[dict]:
    """
    Gelen canonical key'lerin her biri için hastanın geçmişinde eşleşme ara.
    En son kaydı döner.
    """
    matches = []
    for key in canonical_keys:
        row = conn.execute(
            """SELECT * FROM symptom_history
               WHERE patient_id = ? AND canonical_key = ?
               ORDER BY analyzed_at DESC LIMIT 1""",
            (patient_id, key),
        ).fetchone()
        if row:
            matches.append(dict(row))
    return matches


def save_symptoms(conn: sqlite3.Connection, patient_id: int, symptoms: list[dict], analyzed_at: str) -> None:
    for s in symptoms:
        tetikleyici = json.dumps(s.get("tetikleyiciAzaltan") or [], ensure_ascii=False)
        conn.execute(
            """INSERT INTO symptom_history
               (patient_id, canonical_key, semptom, zaman, siklik, siddet_seyri, tetikleyici, analyzed_at)
               VALUES (?, ?, ?, ?, ?, ?, ?, ?)""",
            (
                patient_id,
                s.get("canonicalKey", ""),
                s.get("semptom", ""),
                s.get("zaman"),
                s.get("siklik"),
                s.get("siddetSeyri"),
                tetikleyici,
                analyzed_at,
            ),
        )
    conn.commit()


def build_warning(matches: list[dict]) -> str:
    """Eşleşen semptomlardan okunabilir uyarı metni üret."""
    if len(matches) == 1:
        m = matches[0]
        date_str = m["analyzed_at"][:10]
        return (
            f"Bu hasta {date_str} tarihinde '{m['semptom'].replace('_', ' ')}' "
            f"şikayetiyle başvurmuştu. Lütfen semptom seyrini takip edin."
        )

    # Birden fazla eşleşme
    semptom_listesi = ", ".join(
        f"'{m['semptom'].replace('_', ' ')}' ({m['analyzed_at'][:10]})"
        for m in matches
    )
    return (
        f"Bu hasta geçmişte şu semptomları tekrar geliştirdi: {semptom_listesi}. "
        f"Kronik seyir değerlendirmesi önerilir."
    )


def process(data: dict) -> dict:
    patient_id  = data["patientId"]
    symptoms    = data.get("symptoms", [])
    analyzed_at = data.get("analyzedAt", time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()))

    if not symptoms:
        logger.info("Semptom listesi boş - Hasta #%s, kayıt yapılmıyor", patient_id)
        return {"hasWarning": False, "warning": None, "previousDate": None}

    canonical_keys = [s.get("canonicalKey", "") for s in symptoms if s.get("canonicalKey")]
    logger.info(
        "İstek alındı - Hasta #%s | %d semptom | Keys: %s",
        patient_id, len(symptoms), ", ".join(canonical_keys),
    )

    with get_db() as conn:
        matches = find_matches(conn, patient_id, canonical_keys)
        save_symptoms(conn, patient_id, symptoms, analyzed_at)

    if matches:
        warning = build_warning(matches)
        previous_date = matches[0]["analyzed_at"]
        logger.info("Eşleşme bulundu - Hasta #%s → %d semptom geçmişte var", patient_id, len(matches))
        return {"hasWarning": True, "warning": warning, "previousDate": previous_date}

    logger.info("Geçmiş eşleşme yok - Hasta #%s", patient_id)
    return {"hasWarning": False, "warning": None, "previousDate": None}


# ── RabbitMQ consumer ────────────────────────────────────────────────────────

def on_message(ch, method, properties, body: bytes) -> None:
    result = {"hasWarning": False, "warning": None, "previousDate": None}
    try:
        data   = json.loads(body.decode("utf-8"))
        result = process(data)
        ch.basic_ack(delivery_tag=method.delivery_tag)
    except json.JSONDecodeError as e:
        logger.error("JSON parse hatası: %s", e)
        ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)
    except Exception as e:
        logger.error("Beklenmedik hata: %s", e, exc_info=True)
        ch.basic_ack(delivery_tag=method.delivery_tag)
    finally:
        # Her durumda yanıt gönder — .NET timeout almasın
        if properties.reply_to:
            try:
                ch.basic_publish(
                    exchange="",
                    routing_key=properties.reply_to,
                    properties=pika.BasicProperties(
                        correlation_id=properties.correlation_id,
                        content_type="application/json",
                    ),
                    body=json.dumps(result).encode("utf-8"),
                )
            except Exception as pub_err:
                logger.error("Yanıt gönderilemedi: %s", pub_err)


def connect_and_consume() -> None:
    credentials = pika.PlainCredentials(RABBITMQ_USER, RABBITMQ_PASS)
    params = pika.ConnectionParameters(
        host=RABBITMQ_HOST,
        port=RABBITMQ_PORT,
        credentials=credentials,
        heartbeat=60,
        blocked_connection_timeout=30,
    )
    logger.info("RabbitMQ bağlantısı: %s:%s", RABBITMQ_HOST, RABBITMQ_PORT)
    connection = pika.BlockingConnection(params)
    channel = connection.channel()

    channel.queue_declare(queue=SYMPTOM_QUEUE, durable=True)
    channel.basic_qos(prefetch_count=1)
    channel.basic_consume(queue=SYMPTOM_QUEUE, on_message_callback=on_message)

    logger.info("Symptom worker hazır — kuyruk: %s | DB: %s", SYMPTOM_QUEUE, DB_PATH)
    channel.start_consuming()


# ── Ana döngü ────────────────────────────────────────────────────────────────

def main() -> None:
    init_db()
    retry_delay = 5
    while True:
        try:
            connect_and_consume()
        except pika.exceptions.AMQPConnectionError as e:
            logger.error("Bağlantı hatası: %s — %ds sonra yeniden denenecek", e, retry_delay)
            time.sleep(retry_delay)
        except KeyboardInterrupt:
            logger.info("Worker kapatılıyor...")
            break


if __name__ == "__main__":
    main()
