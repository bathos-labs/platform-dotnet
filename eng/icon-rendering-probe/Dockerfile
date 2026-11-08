FROM ubuntu:24.04
RUN apt-get update -qq && apt-get install -y --no-install-recommends python3 python3-gi gir1.2-gdkpixbuf-2.0 gir1.2-rsvg-2.0 python3-cairo python3-gi-cairo && rm -rf /var/lib/apt/lists/*
COPY probe.py /probe.py
CMD ["python3", "/probe.py"]
