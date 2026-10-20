FROM python:3.14.7-alpine3.24

WORKDIR /app
COPY observability/container-exporter/exporter.py /app/exporter.py

USER 65534:65534
EXPOSE 9400
ENTRYPOINT ["python", "/app/exporter.py"]
