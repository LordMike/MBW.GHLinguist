ARG RUBY_IMAGE=ruby:4.0.1-bookworm@sha256:4ca45c63e012dd4ace6d74ef21192591994015a761a59b8b267cde348f937847
FROM ${RUBY_IMAGE}

RUN apt-get update \
    && apt-get install --yes --no-install-recommends binutils build-essential cmake libicu-dev patchelf \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /workspace

ENTRYPOINT ["bash", "/workspace/eng/linguist/build-linux.sh"]
