# Lazada mock-channel server.
# Built from the repo root: `docker build -f infrastructure/mock-channels/lazada-mock/Dockerfile infrastructure/mock-channels/`
# (the build context must be `infrastructure/mock-channels/` so we can COPY both the
# shared library and the server folder into the image.)

FROM node:22-alpine

WORKDIR /app

COPY _shared/package.json /app/_shared/package.json
COPY _shared/ /app/_shared/

COPY lazada-mock/package.json /app/lazada-mock/package.json
COPY lazada-mock/ /app/lazada-mock/

WORKDIR /app/lazada-mock

RUN npm install --omit=dev --no-audit --no-fund

ENV NODE_ENV=production
ENV PORT=7002
EXPOSE 7002

HEALTHCHECK --interval=10s --timeout=3s --retries=3 \
    CMD wget --quiet --spider http://localhost:7002/healthz || exit 1

CMD ["node", "index.js"]
