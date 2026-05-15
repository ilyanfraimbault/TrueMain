FROM node:26-alpine AS deps
WORKDIR /app

COPY package.json ./
COPY package-lock.json ./
RUN npm ci

FROM deps AS build
WORKDIR /app

COPY . ./
RUN npm run typecheck
RUN npm run build

FROM node:26-alpine AS runner
WORKDIR /app

ENV NODE_ENV=production
ENV NITRO_HOST=0.0.0.0
ENV NITRO_PORT=3000

COPY --from=build /app/.output ./.output

# node:26-alpine ships a non-root "node" user (uid 1000). The Nitro
# server binds port 3000 (>1024) so it does not need root capabilities.
USER node

EXPOSE 3000

HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=5 \
    CMD wget --quiet --tries=1 --spider http://localhost:3000/ || exit 1

CMD ["node", ".output/server/index.mjs"]
