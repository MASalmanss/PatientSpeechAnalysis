FROM python:3.10-slim

# Coqui TTS needs build tools and ffmpeg/libsndfile for audio I/O
RUN apt-get update && apt-get install -y --no-install-recommends \
        build-essential \
        ffmpeg \
        libsndfile1 \
        espeak-ng \
        ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY logging_config.py worker.py ./

# Modeller volume'da cache'lenir
ENV TTS_HOME=/root/.local/share/tts

CMD ["python", "-u", "worker.py"]
