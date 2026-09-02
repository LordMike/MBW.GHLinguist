ARG RUBY_IMAGE
FROM ${RUBY_IMAGE}

RUN apt-get update \
    && apt-get install --yes --no-install-recommends build-essential cmake libicu-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /workspace

ENTRYPOINT ["bash", "/workspace/eng/linguist/build-linux.sh"]
