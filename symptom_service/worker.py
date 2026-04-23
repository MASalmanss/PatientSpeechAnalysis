"""
Symptom History Worker
======================
Hasta semptom geçmişini yönetir.
- .NET'ten gelen analizi kendi SQLite DB'sine kaydeder
- Aynı hasta için benzer geçmiş semptom varsa uyarı döner
- İletişim: RabbitMQ RPC (direct reply-to)
- Bağımlılık: sadece pika (stdlib: sqlite3, json, logging, os, time, re)
"""

import json
import logging
import os
import re
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
RABBITMQ_HOST  = os.getenv("RABBITMQ_HOST", "localhost")
RABBITMQ_PORT  = int(os.getenv("RABBITMQ_PORT", "5672"))
RABBITMQ_USER  = os.getenv("RABBITMQ_USER", "guest")
RABBITMQ_PASS  = os.getenv("RABBITMQ_PASS", "guest")
SYMPTOM_QUEUE  = os.getenv("SYMPTOM_QUEUE", "symptom.check")
DB_PATH        = os.getenv("DB_PATH", "/data/symptom_history.db")

# Benzerlik eşiği: summary'deki kelimelerin kaçta kaçı eşleşirse benzer sayılır
SIMILARITY_THRESHOLD = float(os.getenv("SIMILARITY_THRESHOLD", "0.25"))

# Kaç günlük geçmişe bakılsın (0 = sınırsız)
LOOKBACK_DAYS = int(os.getenv("LOOKBACK_DAYS", "0"))

# Filtrelenen anlamsız kelimeler (Türkçe + İngilizce temel stop words)
STOP_WORDS = {
    "ve", "veya", "ile", "bu", "bir", "için", "de", "da", "ki",
    "çok", "daha", "olan", "olan", "gibi", "ama", "fakat", "ancak",
    "the", "and", "or", "is", "are", "was", "were", "a", "an",
    "in", "on", "at", "to", "for", "of", "with", "has", "have",
    "that", "this", "it", "be", "been", "being",
}


# ── Veritabanı ───────────────────────────────────────────────────────────────

def get_db() -> sqlite3.Connection:
    os.makedirs(os.path.dirname(DB_PATH), exist_ok=True)
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    return conn


def init_db() -> None:
    with get_db() as conn:
        conn.execute("""
            CREATE TABLE IF NOT EXISTS symptom_history (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                patient_id  INTEGER NOT NULL,
                mood        TEXT NOT NULL,
                keywords    TEXT NOT NULL,   -- virgülle ayrılmış anahtar kelimeler
                summary     TEXT NOT NULL,
                analyzed_at TEXT NOT NULL    -- ISO 8601
            )
        """)
        conn.execute("""
            CREATE INDEX IF NOT EXISTS idx_patient_id
            ON symptom_history (patient_id)
        """)
    logger.info("DB hazır: %s", DB_PATH)


# ── Benzerlik ────────────────────────────────────────────────────────────────

def extract_keywords(text: str) -> set[str]:
    """Metinden anlamlı kelimeleri çıkar (küçük harf, noktalama temizle)."""
    words = re.findall(r"\b[a-zA-ZçğıöşüÇĞİÖŞÜ]{3,}\b", text.lower())
    return {w for w in words if w not in STOP_WORDS}


def jaccard_similarity(set_a: set, set_b: set) -> float:
    """İki küme arasındaki Jaccard benzerliği."""
    if not set_a or not set_b:
        return 0.0
    intersection = len(set_a & set_b)
    union = len(set_a | set_b)
    return intersection / union


def find_similar(conn: sqlite3.Connection, patient_id: int, mood: str, keywords: set[str]) -> dict | None:
    """
    Aynı hastanın geçmiş kayıtlarında benzer semptom ara.
    Öncelik sırası:
      1. Hem mood hem keyword benzerliği yüksek
      2. Sadece mood eşleşmesi
    En yakın eşleşmeyi döner, bulamazsa None.
    """
    query = "SELECT * FROM symptom_history WHERE patient_id = ?"
    params: list = [patient_id]

    if LOOKBACK_DAYS > 0:
        query += " AND analyzed_at >= datetime('now', ?)"
        params.append(f"-{LOOKBACK_DAYS} days")

    query += " ORDER BY analyzed_at DESC"

    rows = conn.execute(query, params).fetchall()
    if not rows:
        return None

    best_match = None
    best_score = 0.0

    for row in rows:
        past_keywords = set(row["keywords"].split(",")) if row["keywords"] else set()
        kw_sim = jaccard_similarity(keywords, past_keywords)
        mood_match = row["mood"].lower() == mood.lower()

        # Toplam skor: keyword benzerliği ağırlıklı + mood bonusu
        score = kw_sim + (0.3 if mood_match else 0)

        if score > best_score:
            best_score = score
            best_match = dict(row)
            best_match["_score"] = score
            best_match["_mood_match"] = mood_match
            best_match["_kw_sim"] = kw_sim

    if best_match is None:
        return None

    # Eşik: ya keyword benzerliği yeterli ya da mood birebir aynı
    kw_sim = best_match["_kw_sim"]
    mood_match = best_match["_mood_match"]

    if kw_sim >= SIMILARITY_THRESHOLD or mood_match:
        return best_match

    return None


def save_record(conn: sqlite3.Connection, patient_id: int, mood: str,
                keywords: set[str], summary: str, analyzed_at: str) -> None:
    conn.execute(
        """INSERT INTO symptom_history (patient_id, mood, keywords, summary, analyzed_at)
           VALUES (?, ?, ?, ?, ?)""",
        (patient_id, mood, ",".join(sorted(keywords)), summary, analyzed_at),
    )
    conn.commit()


# ── İş mantığı ───────────────────────────────────────────────────────────────

def process(data: dict) -> dict:
    patient_id  = data["patientId"]
    mood        = data.get("mood", "")
    summary     = data.get("summary", "")
    analyzed_at = data.get("analyzedAt", time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()))

    keywords = extract_keywords(summary)

    with get_db() as conn:
        match = find_similar(conn, patient_id, mood, keywords)

        # Her durumda yeni kaydı ekle
        save_record(conn, patient_id, mood, keywords, summary, analyzed_at)

    if match:
        date_str = match["analyzed_at"][:10]  # YYYY-MM-DD
        mood_part = f"Duygu durumu: {match['mood']}." if match["_mood_match"] else ""
        sim_pct   = int(match["_kw_sim"] * 100)
        sim_part  = f" Semptom benzerliği: %{sim_pct}." if sim_pct > 0 else ""

        warning = (
            f"Bu hasta {date_str} tarihinde benzer semptomlar geliştirdi. "
            f"{mood_part}{sim_part} Lütfen dikkatli takip edin."
        ).strip()

        logger.info("Uyarı üretildi - Hasta #%s → %s", patient_id, warning)
        return {"hasWarning": True, "warning": warning, "previousDate": match["analyzed_at"]}

    logger.info("Geçmiş benzer semptom yok - Hasta #%s", patient_id)
    return {"hasWarning": False, "warning": None, "previousDate": None}


# ── RabbitMQ consumer ────────────────────────────────────────────────────────

def on_message(ch, method, properties, body: bytes) -> None:
    try:
        data = json.loads(body.decode("utf-8"))
        logger.info("İstek alındı - Hasta #%s", data.get("patientId"))

        result = process(data)

        reply_body = json.dumps(result).encode("utf-8")
        ch.basic_publish(
            exchange="",
            routing_key=properties.reply_to,
            properties=pika.BasicProperties(
                correlation_id=properties.correlation_id,
                content_type="application/json",
            ),
            body=reply_body,
        )
        ch.basic_ack(delivery_tag=method.delivery_tag)

    except json.JSONDecodeError as e:
        logger.error("JSON parse hatası: %s", e)
        ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)
    except Exception as e:
        logger.error("Beklenmedik hata: %s", e, exc_info=True)
        # Hata cevabı gönder — .NET timeout almak yerine temiz hata alsın
        try:
            error_reply = json.dumps({"hasWarning": False, "warning": None, "previousDate": None}).encode()
            ch.basic_publish(
                exchange="",
                routing_key=properties.reply_to,
                properties=pika.BasicProperties(
                    correlation_id=properties.correlation_id,
                    content_type="application/json",
                ),
                body=error_reply,
            )
        except Exception:
            pass
        ch.basic_ack(delivery_tag=method.delivery_tag)


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
