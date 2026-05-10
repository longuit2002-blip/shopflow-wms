# Shopee mock-channel server.
# Built from the repo root: `docker build -f infrastructure/mock-channels/shopee-mock/Dockerfile infrastructure/mock-channels/`
# (the build context must be `infrastructure/mock-channels/` so we can COPY both the
# shared library and the server folder into the image.)

FROM node:22-alpine

WORKDIR /app

# Copy shared library first so the file: dependency resolves.
COPY _shared/package.json /app/_shared/package.json
COPY _shared/ /app/_shared/

# Copy server package.
COPY shopee-mock/package.json /app/shopee-mock/package.json
COPY shopee-mock/ /app/shopee-mock/

WORKDIR /app/shopee-mock

# Production install only. The shared library is pulled via the file: ref in package.json.
RUN npm install --omit=dev --no-audit --no-fund

ENV NODE_ENV=production
ENV PORT=7001
EXPOSE 7001

HEALTHCHECK --interval=10s --timeout=3s --retries=3 \
    CMD wget --quiet --spider http://localhost:7001/healthz || exit 1

CMD ["node", "index.js"]
