FROM node:26-alpine AS deps
WORKDIR /app

COPY package.json ./
COPY package-lock.json ./
RUN npm ci

FROM deps AS build
WORKDIR /app

COPY . ./
RUN npm run build \
    && test -f .output/server/node_modules/harfbuzzjs/hb.wasm

FROM node:26-alpine AS runner
WORKDIR /app

ENV NODE_ENV=production
ENV NITRO_HOST=0.0.0.0
ENV NITRO_PORT=3000

COPY --from=build /app/.output ./.output

USER node

EXPOSE 3000

HEALTHCHECK --interval=30s --timeout=20s --start-period=180s --retries=10 \
    CMD wget --quiet --tries=1 --spider http://127.0.0.1:3000/ || exit 1

CMD ["node", ".output/server/index.mjs"]
