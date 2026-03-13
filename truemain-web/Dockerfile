FROM node:24-alpine AS deps
WORKDIR /app

COPY truemain-web/package.json ./
RUN npm install

FROM deps AS build
WORKDIR /app

COPY truemain-web/ ./
RUN npm run build

FROM node:24-alpine AS runner
WORKDIR /app

ENV NODE_ENV=production
ENV NITRO_HOST=0.0.0.0
ENV NITRO_PORT=3000

COPY --from=build /app/.output ./.output

EXPOSE 3000

CMD ["node", ".output/server/index.mjs"]
