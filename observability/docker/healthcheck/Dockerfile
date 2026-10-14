ARG BASE_IMAGE=busybox:1.37.0-uclibc

FROM busybox:1.37.0-uclibc AS healthcheck

FROM ${BASE_IMAGE}

COPY --from=healthcheck /bin/busybox /bin/busybox
