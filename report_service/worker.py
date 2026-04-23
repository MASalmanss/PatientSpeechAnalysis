"""
Report Worker
=============
RabbitMQ'dan analiz sonuçlarını alır, HTML rapor oluşturur ve SMTP ile mail gönderir.
Bağımlılık: sadece pika (stdlib: json, smtplib, email, os, time, logging)
"""

import json
import logging
import os
import smtplib
import time
from email.mime.multipart import MIMEMultipart
from email.mime.text import MIMEText

import pika
import pika.exceptions

# ── Loglama ─────────────────────────────────────────────────────────────────
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s | %(levelname)-8s | %(message)s",
    datefmt="%Y-%m-%d %H:%M:%S",
)
logger = logging.getLogger("report-worker")

# ── Config (env vars) ────────────────────────────────────────────────────────
RABBITMQ_HOST  = os.getenv("RABBITMQ_HOST", "localhost")
RABBITMQ_PORT  = int(os.getenv("RABBITMQ_PORT", "5672"))
RABBITMQ_USER  = os.getenv("RABBITMQ_USER", "guest")
RABBITMQ_PASS  = os.getenv("RABBITMQ_PASS", "guest")
REPORT_QUEUE   = os.getenv("REPORT_QUEUE", "report.requests")

SMTP_HOST      = os.getenv("SMTP_HOST", "smtp.gmail.com")
SMTP_PORT      = int(os.getenv("SMTP_PORT", "587"))
SMTP_USER      = os.getenv("SMTP_USER", "")
SMTP_PASS      = os.getenv("SMTP_PASS", "")
SMTP_FROM      = os.getenv("SMTP_FROM", SMTP_USER)
SMTP_TO        = os.getenv("SMTP_TO", "")        # virgülle ayrılmış alıcılar
SMTP_CC        = os.getenv("SMTP_CC", "")


# ── HTML Şablon ──────────────────────────────────────────────────────────────

def _score_color(score: int) -> str:
    if score <= 3:
        return "#e53e3e"
    if score <= 5:
        return "#dd6b20"
    return "#38a169"


def _emergency_badge(is_emergency: bool) -> str:
    if is_emergency:
        return (
            '<span style="background:#e53e3e;color:#fff;padding:4px 14px;'
            'border-radius:20px;font-weight:700;font-size:13px;">⚠ ACİL DURUM</span>'
        )
    return (
        '<span style="background:#38a169;color:#fff;padding:4px 14px;'
        'border-radius:20px;font-weight:700;font-size:13px;">✓ Normal</span>'
    )


def build_html(data: dict) -> str:
    patient_id      = data.get("patientId", "-")
    analysis_id     = data.get("analysisId", "-")
    sentence        = data.get("patientSentence", "-")
    mood            = data.get("mood", "-")
    is_emergency    = data.get("isEmergency", False)
    daily_score     = data.get("dailyScore", 0)
    summary         = data.get("summary", "-")
    analyzed_at     = data.get("analyzedAt", "-")

    score_color     = _score_color(daily_score)
    emergency_html  = _emergency_badge(is_emergency)

    banner_bg = "#fff5f5" if is_emergency else "#f0fff4"
    banner_border = "#feb2b2" if is_emergency else "#9ae6b4"

    return f"""<!DOCTYPE html>
<html lang="tr">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0"/>
  <title>Hasta Konuşma Analiz Raporu</title>
</head>
<body style="margin:0;padding:0;background:#f7fafc;font-family:Arial,Helvetica,sans-serif;">
  <table width="100%" cellpadding="0" cellspacing="0" style="background:#f7fafc;padding:32px 0;">
    <tr>
      <td align="center">
        <table width="600" cellpadding="0" cellspacing="0"
               style="background:#fff;border-radius:12px;overflow:hidden;
                      box-shadow:0 2px 12px rgba(0,0,0,.08);">

          <!-- Başlık -->
          <tr>
            <td style="background:linear-gradient(135deg,#2b6cb0,#553c9a);
                       padding:28px 32px;text-align:center;">
              <h1 style="margin:0;color:#fff;font-size:22px;font-weight:700;
                         letter-spacing:-0.3px;">
                🏥 Hasta Konuşma Analiz Raporu
              </h1>
              <p style="margin:8px 0 0;color:#bee3f8;font-size:13px;">
                AI destekli otomatik analiz sistemi
              </p>
            </td>
          </tr>

          <!-- Acil durum banner (sadece acil ise göster) -->
          {'<tr><td style="background:' + banner_bg + ';border-left:4px solid ' + banner_border + ';padding:14px 32px;">'
           '<strong style="color:#c53030;">⚠ Bu hasta için ACİL DURUM tespit edilmiştir. Lütfen derhal müdahale edin.</strong>'
           '</td></tr>' if is_emergency else ''}

          <!-- İçerik -->
          <tr>
            <td style="padding:28px 32px;">

              <!-- Meta bilgi -->
              <table width="100%" cellpadding="0" cellspacing="0"
                     style="background:#ebf8ff;border-radius:8px;
                            padding:14px 18px;margin-bottom:24px;">
                <tr>
                  <td style="font-size:13px;color:#2c5282;">
                    <strong>Hasta ID:</strong> #{patient_id} &nbsp;|&nbsp;
                    <strong>Analiz ID:</strong> #{analysis_id} &nbsp;|&nbsp;
                    <strong>Tarih:</strong> {analyzed_at}
                  </td>
                </tr>
              </table>

              <!-- Satırlar -->
              {_row("Transkript", f'<em>"{sentence}"</em>', "#2d3748")}
              {_row("Duygu Durumu", mood.capitalize(), "#2d3748")}
              {_row("Acil Durum", emergency_html, "#2d3748")}
              {_row("Günlük Skor",
                    f'<span style="font-size:22px;font-weight:700;color:{score_color};">'
                    f'{daily_score}<span style="font-size:14px;color:#718096;"> / 10</span></span>',
                    "#2d3748")}
              {_row("Özet", f'<span style="line-height:1.7;">{summary}</span>', "#2d3748")}

            </td>
          </tr>

          <!-- Footer -->
          <tr>
            <td style="background:#edf2f7;padding:16px 32px;text-align:center;
                       font-size:12px;color:#718096;">
              Bu rapor AI-Health Hasta Konuşma Analizi sistemi tarafından otomatik oluşturulmuştur.
            </td>
          </tr>

        </table>
      </td>
    </tr>
  </table>
</body>
</html>"""


def _row(label: str, value: str, color: str) -> str:
    return f"""
      <table width="100%" cellpadding="0" cellspacing="0"
             style="border-bottom:1px solid #e2e8f0;margin-bottom:4px;">
        <tr>
          <td style="padding:12px 0;width:130px;vertical-align:top;">
            <span style="font-size:11px;font-weight:700;text-transform:uppercase;
                         letter-spacing:.6px;color:#718096;">{label}</span>
          </td>
          <td style="padding:12px 0;font-size:14px;color:{color};vertical-align:top;">
            {value}
          </td>
        </tr>
      </table>"""


# ── Mail gönderme ────────────────────────────────────────────────────────────

def send_email(data: dict) -> None:
    if not SMTP_TO:
        logger.warning("SMTP_TO tanımlı değil, mail gönderilmiyor.")
        return

    patient_id = data.get("patientId", "?")
    is_emergency = data.get("isEmergency", False)
    prefix = "🚨 ACİL — " if is_emergency else ""
    subject = f"{prefix}Hasta #{patient_id} Analiz Raporu"

    html_body = build_html(data)

    msg = MIMEMultipart("alternative")
    msg["Subject"] = subject
    msg["From"] = SMTP_FROM
    msg["To"] = SMTP_TO
    if SMTP_CC:
        msg["Cc"] = SMTP_CC

    msg.attach(MIMEText(html_body, "html", "utf-8"))

    recipients = [r.strip() for r in SMTP_TO.split(",") if r.strip()]
    if SMTP_CC:
        recipients += [r.strip() for r in SMTP_CC.split(",") if r.strip()]

    with smtplib.SMTP(SMTP_HOST, SMTP_PORT, timeout=30) as server:
        server.ehlo()
        server.starttls()
        server.ehlo()
        if SMTP_USER and SMTP_PASS:
            server.login(SMTP_USER, SMTP_PASS)
        server.sendmail(SMTP_FROM, recipients, msg.as_string())

    logger.info("Mail gönderildi → %s | Hasta #%s", SMTP_TO, patient_id)


# ── RabbitMQ consumer ────────────────────────────────────────────────────────

def on_message(ch, method, properties, body: bytes) -> None:
    try:
        data = json.loads(body.decode("utf-8"))
        logger.info(
            "Rapor isteği alındı — Hasta #%s, Analiz #%s",
            data.get("patientId"), data.get("analysisId"),
        )
        send_email(data)
        ch.basic_ack(delivery_tag=method.delivery_tag)
    except json.JSONDecodeError as e:
        logger.error("JSON parse hatası: %s", e)
        ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)
    except smtplib.SMTPException as e:
        logger.error("SMTP hatası: %s — mesaj tekrar kuyruğa alınıyor", e)
        ch.basic_nack(delivery_tag=method.delivery_tag, requeue=True)
    except Exception as e:
        logger.error("Beklenmedik hata: %s", e, exc_info=True)
        ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)


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

    channel.queue_declare(queue=REPORT_QUEUE, durable=True)
    channel.basic_qos(prefetch_count=1)
    channel.basic_consume(queue=REPORT_QUEUE, on_message_callback=on_message)

    logger.info("Report worker hazır — kuyruk: %s | SMTP: %s", REPORT_QUEUE, SMTP_HOST)
    channel.start_consuming()


# ── Ana döngü ────────────────────────────────────────────────────────────────

def main() -> None:
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
