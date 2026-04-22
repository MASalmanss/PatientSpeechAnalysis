import logging
import time
import functools
import os
from logging.handlers import RotatingFileHandler
from pathlib import Path


def setup_logging():
    """TTS servisi için dosya tabanlı loglama yapılandırması."""
    log_dir = Path(__file__).parent / "logs"
    os.makedirs(log_dir, exist_ok=True)

    log_file = log_dir / "tts_service.log"

    formatter = logging.Formatter(
        "%(asctime)s | %(levelname)-5s | %(funcName)s | %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    )

    file_handler = RotatingFileHandler(
        log_file,
        maxBytes=10 * 1024 * 1024,
        backupCount=5,
        encoding="utf-8",
    )
    file_handler.setFormatter(formatter)

    console_handler = logging.StreamHandler()
    console_handler.setFormatter(formatter)

    root_logger = logging.getLogger()
    root_logger.setLevel(logging.INFO)
    root_logger.handlers.clear()
    root_logger.addHandler(file_handler)
    root_logger.addHandler(console_handler)


def log_timing(logger: logging.Logger):
    """Senkron fonksiyon çalışma süresini logla."""

    def decorator(func):
        @functools.wraps(func)
        def wrapper(*args, **kwargs):
            start = time.perf_counter()
            logger.info(f"STARTED {func.__name__}")
            try:
                result = func(*args, **kwargs)
                elapsed = time.perf_counter() - start
                logger.info(f"COMPLETED {func.__name__} in {elapsed:.3f}s")
                return result
            except Exception as e:
                elapsed = time.perf_counter() - start
                logger.error(f"FAILED {func.__name__} after {elapsed:.3f}s: {e}")
                raise

        return wrapper

    return decorator
