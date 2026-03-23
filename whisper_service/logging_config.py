import logging
import time
import functools
import asyncio
import os
from logging.handlers import RotatingFileHandler
from pathlib import Path


def setup_logging():
    """Whisper servisi için dosya tabanlı loglama yapılandırması."""
    log_dir = Path(__file__).parent / "logs"
    os.makedirs(log_dir, exist_ok=True)

    log_file = log_dir / "whisper_service.log"

    formatter = logging.Formatter(
        "%(asctime)s | %(levelname)-5s | %(funcName)s | %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    )

    file_handler = RotatingFileHandler(
        log_file,
        maxBytes=10 * 1024 * 1024,  # 10 MB
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
    """Fonksiyon çalışma süresini logla (sync ve async destekli)."""

    def decorator(func):
        @functools.wraps(func)
        async def async_wrapper(*args, **kwargs):
            start = time.perf_counter()
            logger.info(f"STARTED {func.__name__}")
            try:
                result = await func(*args, **kwargs)
                elapsed = time.perf_counter() - start
                logger.info(f"COMPLETED {func.__name__} in {elapsed:.3f}s")
                return result
            except Exception as e:
                elapsed = time.perf_counter() - start
                logger.error(f"FAILED {func.__name__} after {elapsed:.3f}s: {e}")
                raise

        @functools.wraps(func)
        def sync_wrapper(*args, **kwargs):
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

        return async_wrapper if asyncio.iscoroutinefunction(func) else sync_wrapper

    return decorator
