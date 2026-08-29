# syntax=docker/dockerfile:1

FROM node:24-alpine AS build
WORKDIR /source
COPY src/web/package.json src/web/package-lock.json ./
RUN npm ci
COPY src/web .
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.28-alpine AS web
COPY src/web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /source/dist /usr/share/nginx/html
EXPOSE 3000
USER 101
