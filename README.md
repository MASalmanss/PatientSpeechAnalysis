<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white"/>
  <img src="https://img.shields.io/badge/React-18-61DAFB?style=for-the-badge&logo=react&logoColor=black"/>
  <img src="https://img.shields.io/badge/Python-3.10%2B-3776AB?style=for-the-badge&logo=python&logoColor=white"/>
  <img src="https://img.shields.io/badge/RabbitMQ-3.13-FF6600?style=for-the-badge&logo=rabbitmq&logoColor=white"/>
  <img src="https://img.shields.io/badge/Whisper-large--v3-00B4D8?style=for-the-badge&logo=openai&logoColor=white"/>
  <img src="https://img.shields.io/badge/Gemini-2.0_Flash-4285F4?style=for-the-badge&logo=google&logoColor=white"/>
</p>

<h1 align="center">🧠 Patient Speech Analysis</h1>

<p align="center">
  <b>Hasta konuşmalarını gerçek zamanlı analiz eden, yapay zeka destekli klinik değerlendirme sistemi</b><br/>
  <i>Real-time AI-powered clinical speech analysis for patient monitoring</i>
</p>

---

## 🇹🇷 Türkçe

### Nedir?

**Patient Speech Analysis**, hastaların sözlü ifadelerini mikrofondan kaydedip anlık transkripsiyon yapan, ardından yapay zeka ile klinik analiz gerçekleştiren bir sistemdir. JWT kimlik doğrulama, semptom geçmişi kontrolü, otomatik raporlama ve acil durum bildirimi gibi özellikler sunarak sağlık profesyonellerine hızlı karar desteği sağlar.

### Özellikler

- 🔐 **JWT Kimlik Doğrulama** — Login ekranı yok; tek tuşla token üretilir. Access token bellekte, refresh token httpOnly cookie'de tutulur. Otomatik yenileme ve token rotation.
- 🎙️ **Gerçek Zamanlı Transkripsiyon** — Konuşma sırasında kelimeler anında ekranda belirir (WebSocket + faster-whisper)
- 🤖 **Yapay Zeka Analizi** — Google Gemini 2.0 Flash ile duygu durumu, acil durum tespiti ve klinik özet
- 🔍 **Semptom Geçmişi** — Her analizde hastanın geçmiş semptomları sorgulanır; benzer şikayet tespit edilirse tarih ve benzerlik yüzdesiyle uyarı döner
- 🔊 **Türkçe Sesli Yanıt** — "Dinle" butonu ile analiz özeti Coqui TTS (glow-tts) tarafından seslendirilir
- 📧 **Otomatik Raporlama** — Her analiz sonrası HTML rapor oluşturulup mail olarak gönderilir
- 🚨 **Acil Durum Bildirimi** — Kritik ifade tespit edildiğinde otomatik e-posta gönderilir
- 📊 **Günlük Skor** — Her seans için 0-10 arası psikometrik değerlendirme
- 🧬 **3D Animasyonlu Arayüz** — Three.js DNA sarmalı arka plan ve glassmorphism UI

### Mimari

```
┌─────────────────────────────────────────────────────────────────┐
│                        React Frontend                            │
│           Three.js · Glassmorphism · WebSocket · JWT            │
└──────────────────────────┬──────────────────────────────────────┘
                           │ HTTP / WebSocket (Bearer Token)
┌──────────────────────────▼──────────────────────────────────────┐
│                  .NET 8 Backend (ASP.NET Core)                   │
│        Minimal API · EF Core · SQLite · JWT Auth · Serilog      │
└───┬──────────────┬──────────────┬──────────────┬────────────────┘
    │ RabbitMQ RPC │ RabbitMQ RPC │ RabbitMQ RPC │ Fire-and-Forget
    │ (senkron)    │ (senkron)    │ (senkron)    │
    ▼              ▼              ▼              ▼
┌────────────┐ ┌──────────┐ ┌──────────────┐ ┌──────────────────┐
│  Whisper   │ │   TTS    │ │   Symptom    │ │     Report       │
│  Worker   │ │  Worker  │ │   Worker     │ │     Worker       │
│ faster-   │ │ Coqui    │ │ SQLite       │ │ SMTP · HTML      │
│ whisper   │ │ glow-tts │ │ Jaccard sim. │ │ Mail Report      │
└────────────┘ └──────────┘ └──────────────┘ └──────────────────┘
         Tümü RabbitMQ 3.13 üzerinden haberleşir
         whisper.requests · tts.requests · symptom.check · report.requests
```

### Teknoloji Yığını

| Katman | Teknoloji |
|---|---|
| Frontend | React 18, Three.js, Vite, WebSocket API |
| Backend | .NET 8, ASP.NET Core Minimal API, EF Core 8, SQLite |
| Kimlik Doğrulama | JWT Bearer, access/refresh token, httpOnly cookie, token rotation |
| Mesajlaşma | RabbitMQ 3.13, RabbitMQ.Client 7.0 (RPC + fire-and-forget) |
| Konuşma Tanıma | faster-whisper large-v3, CPU/int8 |
| Metin-Sesli | Coqui TTS 0.22.0, glow-tts (Türkçe) |
| Semptom Geçmişi | Python, SQLite, Jaccard benzerliği |
| Raporlama | Python, smtplib, HTML e-posta şablonu |
| Yapay Zeka | Google Gemini 2.0 Flash (OpenRouter API) |
| Loglama | Serilog (rolling file + console) |
| Containerizasyon | Docker, Docker Compose |

### Çalıştırma

#### Gereksinimler
- .NET 8 SDK
- Node.js 18+
- Python 3.10+
- Docker & Docker Compose

#### 1. RabbitMQ'yu Başlat
```bash
docker-compose up -d rabbitmq
```

#### 2. Python Worker'larını Başlat
```bash
# Whisper Worker
cd whisper_service
pip install -r requirements.txt
python worker.py

# TTS Worker (ayrı terminal)
cd tts_service
pip install -r requirements.txt
python worker.py

# Symptom Worker (ayrı terminal)
cd symptom_service
pip install -r requirements.txt
python worker.py

# Report Worker (ayrı terminal)
cd report_service
pip install -r requirements.txt
python worker.py
```

#### 3. .NET Backend'i Başlat
```bash
dotnet run
# → http://localhost:5233
```

#### 4. React Frontend'i Başlat
```bash
cd client
npm install
npm run dev
# → http://localhost:5173
```

#### 5. RabbitMQ Yönetim Paneli
```
http://localhost:15672  (guest / guest)
```

### Konfigürasyon

`appsettings.Development.json` dosyasına gerekli değerleri ekle:
```json
{
  "OpenRouter": {
    "ApiKey": "sk-or-v1-..."
  },
  "Jwt": {
    "Secret": "en-az-32-karakter-guclu-bir-anahtar!"
  }
}
```

Report Worker için SMTP ayarları `docker-compose.yml` içindeki `report-worker` servisinin `environment` bölümünden yapılır:
```yaml
SMTP_HOST: smtp.gmail.com
SMTP_USER: kullanici@gmail.com
SMTP_PASS: uygulama-sifresi
SMTP_TO:   doktor@hastane.com
```

### API Uç Noktaları

| Method | Endpoint | Auth | Açıklama |
|---|---|---|---|
| `POST` | `/api/auth/token` | — | Token çifti üret (giriş) |
| `POST` | `/api/auth/refresh` | cookie | Access token yenile |
| `POST` | `/api/auth/logout` | cookie | Oturumu kapat |
| `WS` | `/ws/analyze?patientId={id}&token={jwt}` | token | Gerçek zamanlı ses analizi |
| `POST` | `/api/analyze` | Bearer | Metin tabanlı analiz |
| `POST` | `/api/analyze/audio` | Bearer | Ses dosyası ile analiz |
| `POST` | `/api/tts` | Bearer | Metin → WAV ses sentezi |
| `GET` | `/swagger` | — | API dokümantasyonu |

### Nasıl Çalışır?

1. Uygulama açılır → sessizce `/api/auth/refresh` ile cookie kontrol edilir
2. Cookie yoksa "Token Al" butonu ile token çifti üretilir (access token bellekte, refresh cookie'de)
3. Hasta ID girilir, **Kayıt** butonuna basılır → WebSocket bağlantısı `?token=` ile açılır
4. `MediaRecorder` 250ms'lik ses parçalarını sunucuya aktarır
5. Her ~2 saniyede kısmi ses Whisper Worker'a gönderilir → kısmi transkript ekranda canlı görünür
6. Kayıt bitince tüm ses final transkripsiyon için Whisper'a gönderilir
7. Transkript Gemini 2.0 Flash'a iletilir → klinik analiz yapılır
8. **Semptom Worker**'a RPC çağrısı yapılır → bu hastanın geçmişi kontrol edilir, benzer şikayet varsa uyarı üretilir
9. Sonuç (duygu, acil durum, skor, özet, semptom uyarısı) SQLite'a kaydedilir ve WebSocket üzerinden frontend'e gönderilir
10. Arka planda **Report Worker**'a fire-and-forget mesaj gönderilir → HTML rapor maili oluşturulup gönderilir
11. "Dinle" butonuna basılınca özet TTS Worker'a iletilir → WAV döner, tarayıcıda çalar

### Proje Yapısı

```
PatientSpeechAnalysis/
├── Configuration/      # JwtOptions
├── Endpoints/          # Auth, Analysis API route tanımları
├── Messaging/          # RabbitMQ RPC client (CallAsync + PublishAsync)
├── Models/             # Domain modelleri (PatientAnalysis, RefreshToken)
├── Services/           # İş mantığı (Analysis, Token, Transcription, TTS, Gemini, Email)
├── Data/               # EF Core DbContext
├── Middleware/         # Request timing
├── whisper_service/    # Python worker — faster-whisper STT
├── tts_service/        # Python worker — Coqui TTS
├── symptom_service/    # Python worker — Semptom geçmişi (SQLite + Jaccard)
├── report_service/     # Python worker — HTML mail raporu (smtplib)
├── client/             # React frontend (Vite + Three.js)
├── docker-compose.yml  # RabbitMQ + tüm worker container'ları
└── appsettings.json    # Uygulama konfigürasyonu
```

---

## 🇬🇧 English

### What is it?

**Patient Speech Analysis** is an AI-powered clinical monitoring system that records patient speech via microphone, performs real-time transcription, and analyzes the content using a large language model. It features JWT authentication, symptom history tracking, automated HTML email reporting, and emergency alerting — all connected through a RabbitMQ message broker.

### Features

- 🔐 **JWT Authentication** — No login form; a single button issues an access/refresh token pair. Access token is kept in memory, refresh token in an httpOnly cookie with automatic rotation.
- 🎙️ **Real-Time Transcription** — Words appear on screen as the patient speaks (WebSocket + faster-whisper)
- 🤖 **AI Analysis** — Mood state, emergency detection, and clinical summary via Google Gemini 2.0 Flash
- 🔍 **Symptom History** — Every analysis queries the patient's past records; if a similar complaint is found, a warning with date and similarity percentage is returned
- 🔊 **Turkish TTS Playback** — "Listen" button synthesizes the analysis summary using Coqui TTS (glow-tts)
- 📧 **Automated Reporting** — An HTML email report is generated and sent after every analysis
- 🚨 **Emergency Alerting** — Automatic email notification on critical speech detection
- 📊 **Daily Score** — 0–10 psychometric score computed per session
- 🧬 **3D Animated UI** — Three.js DNA helix background with glassmorphism card design

### Architecture

The system follows a **message-driven microservice** architecture. The .NET backend accepts audio over WebSocket and routes work to specialized Python workers via **RabbitMQ RPC** (direct reply-to pattern) for synchronous calls, and simple **publish** for fire-and-forget tasks:

```
Browser → WebSocket (JWT) → .NET API
                                ├─► RabbitMQ RPC → Whisper Worker  → transcription
                                ├─► RabbitMQ RPC → TTS Worker      → audio bytes
                                ├─► RabbitMQ RPC → Symptom Worker  → history warning
                                ├─► OpenRouter/Gemini              → AI analysis
                                ├─► SQLite                         → persistence
                                └─► RabbitMQ pub → Report Worker   → HTML email
```

### Technology Stack

| Layer | Technology |
|---|---|
| Frontend | React 18, Three.js, Vite, native WebSocket API |
| Backend | .NET 8, ASP.NET Core Minimal API, EF Core 8, SQLite |
| Authentication | JWT Bearer, access/refresh token pair, httpOnly cookie, token rotation |
| Messaging | RabbitMQ 3.13, RabbitMQ.Client 7.0 (RPC + fire-and-forget publish) |
| Speech-to-Text | faster-whisper large-v3, CPU/int8 quantization |
| Text-to-Speech | Coqui TTS 0.22.0, glow-tts (Turkish) |
| Symptom History | Python, SQLite (stdlib), Jaccard keyword similarity |
| Reporting | Python, smtplib (stdlib), HTML email template |
| AI / LLM | Google Gemini 2.0 Flash via OpenRouter |
| Logging | Serilog with rolling file sink |
| Containers | Docker, Docker Compose |

### Quick Start

#### Prerequisites
- .NET 8 SDK
- Node.js 18+
- Python 3.10+
- Docker & Docker Compose

#### 1. Start RabbitMQ
```bash
docker-compose up -d rabbitmq
```

#### 2. Start Python Workers
```bash
cd whisper_service  && pip install -r requirements.txt && python worker.py
cd tts_service      && pip install -r requirements.txt && python worker.py
cd symptom_service  && pip install -r requirements.txt && python worker.py
cd report_service   && pip install -r requirements.txt && python worker.py
```

#### 3. Start .NET Backend
```bash
# Add OpenRouter key and JWT secret to appsettings.Development.json first
dotnet run   # → http://localhost:5233
```

#### 4. Start React Frontend
```bash
cd client && npm install && npm run dev   # → http://localhost:5173
```

### Configuration

Create `appsettings.Development.json`:
```json
{
  "OpenRouter": { "ApiKey": "sk-or-v1-..." },
  "Jwt":        { "Secret": "at-least-32-char-strong-secret!" }
}
```

SMTP settings for the report worker are configured via environment variables in `docker-compose.yml` under the `report-worker` service.

Symptom worker tuning (also via env vars):

| Variable | Default | Description |
|---|---|---|
| `SIMILARITY_THRESHOLD` | `0.25` | Jaccard similarity cutoff (0–1) |
| `LOOKBACK_DAYS` | `0` | Days of history to search (0 = unlimited) |

### How It Works

1. App loads → silently attempts `POST /api/auth/refresh` to restore session from cookie
2. If no cookie → auth screen with **"Token Al"** button; clicking it calls `POST /api/auth/token`
3. Access token stored in memory (`useRef`), refresh token in httpOnly cookie; auto-retry on 401
4. Patient ID entered, **Record** clicked → WebSocket opens with `?token=` query param
5. `MediaRecorder` streams 250ms audio chunks to the server
6. Every ~2 s a partial snapshot is sent to **Whisper Worker** via RabbitMQ RPC; partial transcript appears live
7. On stop, full audio is sent for final transcription
8. Transcript forwarded to **Gemini 2.0 Flash** for clinical analysis
9. **Symptom Worker** is called synchronously via RabbitMQ RPC → checks patient history, returns warning if similar complaint found
10. Result (mood, emergency flag, score, summary, symptom warning) saved to SQLite and pushed back over WebSocket
11. In the background, **Report Worker** receives a fire-and-forget message and sends an HTML email report
12. Clicking **"Dinle"** sends the summary to **TTS Worker** → WAV returned and played in-browser

### Project Structure

```
PatientSpeechAnalysis/
├── Configuration/      # JwtOptions
├── Endpoints/          # Auth + Analysis API routes
├── Messaging/          # RabbitMQ RPC client (CallAsync + PublishAsync)
├── Models/             # Domain models (PatientAnalysis, RefreshToken)
├── Services/           # Business logic (Analysis, Token, Transcription, TTS, Gemini, Email)
├── Data/               # EF Core DbContext
├── Middleware/         # Request timing
├── whisper_service/    # Python worker — faster-whisper STT
├── tts_service/        # Python worker — Coqui TTS
├── symptom_service/    # Python worker — symptom history (SQLite + Jaccard similarity)
├── report_service/     # Python worker — HTML email report (smtplib)
├── client/             # React frontend (Vite + Three.js)
├── docker-compose.yml  # RabbitMQ + all worker containers
└── appsettings.json    # Application configuration
```

---

<p align="center">
  Built with ❤️ for clinical AI research &nbsp;·&nbsp;
  <a href="https://github.com/MASalmanss/PatientSpeechAnalysis">GitHub</a>
</p>
