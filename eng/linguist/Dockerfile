ARG RUBY_IMAGE=ruby:4.0.6-bookworm@sha256:b4aa7093ffca123d849e79b3e2bc582064f6dd9c22940e69c08bc47d74e355db
FROM ${RUBY_IMAGE}

COPY native-dependencies.json /tmp/native-dependencies.json
COPY install-linux-prerequisites.sh /usr/local/bin/install-linux-prerequisites

RUN bash /usr/local/bin/install-linux-prerequisites /tmp/native-dependencies.json

WORKDIR /workspace

ENTRYPOINT ["bash", "/workspace/eng/linguist/build-linux.sh"]
