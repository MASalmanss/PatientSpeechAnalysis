FROM python:3.11-slim

# ffmpeg: MP3 → WAV dönüşümü için
RUN apt-get update && apt-get install -y --no-install-recommends \
        ffmpeg \
        ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY logging_config.py worker.py ./

CMD ["python", "-u", "worker.py"]
