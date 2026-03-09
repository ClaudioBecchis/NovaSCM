FROM python:3.12-slim

WORKDIR /app

# Dipendenze
RUN pip install --no-cache-dir "flask>=3.1.3" gunicorn flask-limiter

COPY api.py .

# Volume per dati persistenti
VOLUME ["/data"]

ENV NOVASCM_DB=/data/novascm.db
ENV PORT=9091

EXPOSE 9091

# Gunicorn: 2 worker, timeout 30s
CMD ["gunicorn", "--workers=1", "--threads=4", "--timeout=30", "--bind=0.0.0.0:9091", "api:app"]
