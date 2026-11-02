FROM node:24-bookworm-slim AS build
WORKDIR /repo
COPY package.json package-lock.json ./
COPY apps/web/package.json apps/web/package.json
COPY tests/e2e/package.json tests/e2e/package.json
RUN npm ci
COPY apps/web/ apps/web/
COPY apps/mobile/assets/fonts/ apps/mobile/assets/fonts/
COPY tests/e2e/support/ tests/e2e/support/
ARG API_INTERNAL_URL=http://api:8080
ARG ACADEMY_EDGE_API=false
ENV ACADEMY_EDGE_API=$ACADEMY_EDGE_API
ENV API_INTERNAL_URL=$API_INTERNAL_URL NEXT_TELEMETRY_DISABLED=1 ACADEMY_WEB_STANDALONE=true
RUN npm run build:web

FROM node:24-bookworm-slim
WORKDIR /app
ENV NODE_ENV=production NEXT_TELEMETRY_DISABLED=1 PORT=3000 HOSTNAME=0.0.0.0
COPY --from=build --chown=node:node /repo/apps/web/.next/standalone/ ./
COPY --from=build --chown=node:node /repo/apps/web/.next/static/ ./apps/web/.next/static/
COPY --from=build --chown=node:node /repo/apps/web/public/ ./apps/web/public/
USER node
EXPOSE 3000
CMD ["node", "apps/web/server.js"]
