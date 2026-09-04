ARG RUBY_IMAGE=ruby:4.0.6-bookworm@sha256:b4aa7093ffca123d849e79b3e2bc582064f6dd9c22940e69c08bc47d74e355db
FROM ${RUBY_IMAGE}

RUN apt-get update \
    && apt-get install --yes --no-install-recommends binutils build-essential cmake libicu-dev patchelf \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /workspace

ENTRYPOINT ["bash", "/workspace/eng/linguist/build-linux.sh"]
