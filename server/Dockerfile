FROM python:3.12-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY api.py pxe_server.py ./
COPY web/ ./web/

# Prima crea utente e directory, POI dichiara il volume
RUN adduser --disabled-password --gecos "" novascm \
    && mkdir -p /data \
    && chown -R novascm:novascm /app /data

VOLUME ["/data"]

USER novascm

ENV NOVASCM_DB=/data/novascm.db
ENV PORT=9091
EXPOSE 9091

CMD ["gunicorn", "--workers=1", "--threads=4", "--timeout=300", "--bind=0.0.0.0:9091", "api:app"]
