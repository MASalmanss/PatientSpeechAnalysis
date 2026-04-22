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

**Patient Speech Analysis**, hastaların sözlü ifadelerini mikrofondan kaydedip anlık transkripsiyon yapan, ardından yapay zeka ile klinik analiz gerçekleştiren bir sistemdir. Acil durum tespiti, duygu durumu analizi ve günlük skor hesaplama gibi özellikler sunarak sağlık profesyonellerine hızlı karar desteği sağlar.

### Özellikler

- 🎙️ **Gerçek Zamanlı Transkripsiyon** — Konuşma sırasında kelimeler anında ekranda belirir (WebSocket + faster-whisper)
- 🤖 **Yapay Zeka Analizi** — Google Gemini 2.0 Flash ile duygu durumu, acil durum tespiti ve klinik özet
- 🔊 **Türkçe Sesli Yanıt** — "Dinle" butonu ile analiz sonucu Coqui TTS (glow-tts) tarafından seslendirilir
- 🚨 **Acil Durum Bildirimi** — Kritik ifade tespit edildiğinde otomatik e-posta gönderilir
- 📊 **Günlük Skor** — Her seans için 0-10 arası psikometrik değerlendirme
- 🧬 **3D Animasyonlu Arayüz** — Three.js DNA sarmalı arka plan ve glassmorphism UI

### Mimari

```
┌─────────────────────────────────────────────────────┐
│                   React Frontend                     │
│         Three.js · Glassmorphism · WebSocket        │
└────────────────────┬────────────────────────────────┘
                     │ HTTP / WebSocket
┌────────────────────▼────────────────────────────────┐
│              .NET 8 Backend (ASP.NET Core)           │
│     Minimal API · EF Core · SQLite · Serilog        │
└──────────┬───────────────────────┬──────────────────┘
           │ AMQP (RabbitMQ RPC)   │ AMQP (RabbitMQ RPC)
┌──────────▼──────────┐  ┌─────────▼──────────────────┐
│   Whisper Worker    │  │       TTS Worker            │
│  faster-whisper     │  │   Coqui TTS (glow-tts)     │
│  large-v3 · CPU     │  │   Turkish · 22050Hz        │
└─────────────────────┘  └────────────────────────────┘
           │                        │
┌──────────▼────────────────────────▼──────────────────┐
│                  RabbitMQ 3.13                        │
│     whisper.requests  ·  tts.requests               │
└──────────────────────────────────────────────────────┘
```

### Teknoloji Yığını

| Katman | Teknoloji |
|---|---|
| Frontend | React 18, Three.js, Vite, WebSocket API |
| Backend | .NET 8, ASP.NET Core Minimal API, EF Core 8, SQLite |
| Mesajlaşma | RabbitMQ 3.13, RabbitMQ.Client 7.0 (RPC pattern) |
| Konuşma Tanıma | faster-whisper large-v3, CPU/int8 |
| Metin-Sesli | Coqui TTS 0.22.0, glow-tts (Türkçe) |
| Yapay Zeka | Google Gemini 2.0 Flash (OpenRouter API) |
| Loglama | Serilog (rolling file + console) |
| Containerizasyon | Docker, Docker Compose |

### Çalıştırma

#### Gereksinimler
- .NET 8 SDK
- Node.js 18+
- Python 3.10 (conda önerilir)
- Docker & Docker Compose (RabbitMQ için)

#### 1. RabbitMQ'yu Başlat
```bash
docker-compose up -d rabbitmq
```

#### 2. Python Worker'larını Başlat (host üzerinde)
```bash
# Whisper Worker
cd whisper_service
pip install -r requirements.txt
python worker.py

# TTS Worker (ayrı terminal)
cd tts_service
pip install -r requirements.txt
python worker.py
```

#### 3. .NET Backend'i Başlat
```bash
# appsettings.Development.json içine OpenRouter API anahtarını ekle
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

`appsettings.Development.json` dosyasına OpenRouter API anahtarını ekle:
```json
{
  "OpenRouter": {
    "ApiKey": "sk-or-v1-..."
  }
}
```

### API Uç Noktaları

| Method | Endpoint | Açıklama |
|---|---|---|
| `WS` | `/ws/analyze?patientId={id}` | Gerçek zamanlı ses analizi (WebSocket) |
| `POST` | `/api/analyze` | Metin tabanlı analiz |
| `POST` | `/api/analyze/audio` | Ses dosyası yükleme ile analiz |
| `POST` | `/api/tts` | Metin → WAV ses sentezi |
| `GET` | `/swagger` | API dokümantasyonu |

---

## 🇬🇧 English

### What is it?

**Patient Speech Analysis** is an AI-powered clinical monitoring system that records patient speech via microphone, performs real-time transcription, and analyzes the content using a large language model. It provides mood detection, emergency triage, and daily psychometric scoring to support healthcare professionals in making faster, data-driven decisions.

### Features

- 🎙️ **Real-Time Transcription** — Words appear on screen as the patient speaks (WebSocket + faster-whisper)
- 🤖 **AI Analysis** — Mood state, emergency detection, and clinical summary via Google Gemini 2.0 Flash
- 🔊 **Turkish TTS Playback** — "Listen" button synthesizes the analysis summary using Coqui TTS (glow-tts)
- 🚨 **Emergency Alerting** — Automatic email notification on critical speech detection
- 📊 **Daily Score** — 0–10 psychometric score computed per session
- 🧬 **3D Animated UI** — Three.js DNA helix background with glassmorphism card design

### Architecture

The system follows a **message-driven microservice** architecture. The .NET backend accepts audio over WebSocket and routes work to specialized Python workers via **RabbitMQ RPC** (direct reply-to pattern), keeping services fully decoupled:

```
Browser → WebSocket → .NET API → RabbitMQ → Whisper Worker → transcription
                               → RabbitMQ → TTS Worker     → audio bytes
                    → OpenRouter/Gemini    → AI analysis
                    → SQLite              → persistence
```

### Technology Stack

| Layer | Technology |
|---|---|
| Frontend | React 18, Three.js, Vite, native WebSocket API |
| Backend | .NET 8, ASP.NET Core Minimal API, EF Core 8, SQLite |
| Messaging | RabbitMQ 3.13, RabbitMQ.Client 7.0 (RPC pattern) |
| Speech-to-Text | faster-whisper large-v3, CPU/int8 quantization |
| Text-to-Speech | Coqui TTS 0.22.0, glow-tts (Turkish) |
| AI / LLM | Google Gemini 2.0 Flash via OpenRouter |
| Logging | Serilog with rolling file sink |
| Containers | Docker, Docker Compose |

### Quick Start

#### Prerequisites
- .NET 8 SDK
- Node.js 18+
- Python 3.10 (conda recommended)
- Docker & Docker Compose

#### 1. Start RabbitMQ
```bash
docker-compose up -d rabbitmq
```

#### 2. Start Python Workers
```bash
# Whisper Worker
cd whisper_service && pip install -r requirements.txt && python worker.py

# TTS Worker (new terminal)
cd tts_service && pip install -r requirements.txt && python worker.py
```

#### 3. Start .NET Backend
```bash
# Add your OpenRouter API key to appsettings.Development.json first
dotnet run   # → http://localhost:5233
```

#### 4. Start React Frontend
```bash
cd client && npm install && npm run dev   # → http://localhost:5173
```

### Configuration

Create / update `appsettings.Development.json` with your OpenRouter key:
```json
{
  "OpenRouter": {
    "ApiKey": "sk-or-v1-..."
  }
}
```

RabbitMQ settings live in `appsettings.json` under the `"RabbitMQ"` section and default to `localhost:5672` with `guest/guest` credentials.

### How It Works

1. User opens the React app and selects a patient ID
2. Clicking **Record** opens a WebSocket connection to `/ws/analyze`
3. `MediaRecorder` streams 250ms audio chunks to the server
4. Every ~2 seconds the .NET backend sends a partial audio snapshot to the **Whisper Worker** via RabbitMQ; transcribed words stream back and appear live on screen
5. When recording stops, the full audio is sent for final transcription
6. The transcribed text is forwarded to **Gemini 2.0 Flash** for clinical analysis
7. Result (mood, emergency flag, daily score, summary) is saved to SQLite and pushed back over WebSocket
8. User can click **"Dinle"** to have the summary read aloud — the text is sent to the **TTS Worker** via RabbitMQ and returned as a WAV file played directly in the browser

### Project Structure

```
PatientSpeechAnalysis/
├── Endpoints/          # API route definitions (WebSocket + REST)
├── Messaging/          # RabbitMQ RPC client & options
├── Models/             # Domain models & DTOs
├── Services/           # Business logic (Analysis, Transcription, TTS, Gemini, Email)
├── Data/               # EF Core DbContext
├── Middleware/         # Request timing
├── whisper_service/    # Python RabbitMQ worker — faster-whisper STT
├── tts_service/        # Python RabbitMQ worker — Coqui TTS
├── client/             # React frontend (Vite)
├── docker-compose.yml  # RabbitMQ + worker containers
└── appsettings.json    # App configuration
```

---

<p align="center">
  Built with ❤️ for clinical AI research &nbsp;·&nbsp;
  <a href="https://github.com/MASalmanss/PatientSpeechAnalysis">GitHub</a>
</p>
