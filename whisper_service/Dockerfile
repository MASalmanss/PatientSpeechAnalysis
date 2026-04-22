FROM python:3.11-slim

# ffmpeg for audio decoding (faster-whisper relies on it for non-WAV formats)
RUN apt-get update && apt-get install -y --no-install-recommends \
        ffmpeg \
        ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY logging_config.py worker.py ./

# Modeller volume'da cache'lenir (huggingface)
ENV HF_HOME=/root/.cache/huggingface

CMD ["python", "-u", "worker.py"]
