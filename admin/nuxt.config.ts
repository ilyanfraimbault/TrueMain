// https://nuxt.com/docs/api/configuration/nuxt-config

// DDragon/CommunityDragon icons are content-addressed per (patch, asset), so
// once IPX has processed a URL the resulting bytes never change. The admin
// dashboard maps championId → name/icon via the same upstream sources as
// `web/`, so cache aggressively to avoid re-fetching on every navigation.
const IPX_IMAGE_CACHE_SECONDS = 60 * 60 * 24 * 7 // 7 days

export default defineNuxtConfig({
  modules: [
    '@nuxt/ui',
    '@nuxt/image',
    'nuxt-charts',
    '@vueuse/nuxt',
    'nuxt-auth-utils',
  ],
  // Namespace upstream nuxt-charts components under `Nc*` (same convention as
  // `web/`) so app-level chart wrappers can embed the upstream chart without
  // colliding with their own auto-resolved name.
  nuxtCharts: {
    prefix: 'Nc',
  },
  css: ['./app/assets/css/main.css'],
  compatibilityDate: '2026-06-09',
  devtools: { enabled: true },
  colorMode: {
    preference: 'dark',
    fallback: 'dark',
  },
  image: {
    domains: ['ddragon.leagueoflegends.com', 'raw.communitydragon.org'],
    providers: {
      ipx: {
        options: {
          http: {
            maxAge: IPX_IMAGE_CACHE_SECONDS,
            ignoreCacheControl: true,
          },
        },
      },
    },
  },
  routeRules: {
    // IPX responses are deterministic per (source URL, modifiers) — safe to
    // mark immutable and cache for a week in shared/private caches.
    '/_ipx/**': {
      headers: {
        'cache-control': `public, max-age=${IPX_IMAGE_CACHE_SECONDS}, immutable`,
      },
    },
  },
  runtimeConfig: {
    // Server-side base URL for the backend ops API. The ops proxy
    // (`server/api/ops/[...path].ts`) prefixes `/ops` and injects the
    // `X-Ops-Key` header so the key never reaches the browser.
    opsApiBaseUrl: process.env.NUXT_OPS_API_BASE_URL ?? 'http://localhost:5008',
    opsKey: process.env.NUXT_OPS_KEY ?? '',
    // Single-operator credentials checked by the login endpoint. Both default
    // to `truemain` for local dev; override per environment.
    adminUsername: process.env.NUXT_ADMIN_USERNAME ?? 'truemain',
    adminPassword: process.env.NUXT_ADMIN_PASSWORD ?? 'truemain',
    // `session.password` is read by nuxt-auth-utils from NUXT_SESSION_PASSWORD
    // (>= 32 chars). Declared so a misconfigured env surfaces clearly.
    session: {
      password: process.env.NUXT_SESSION_PASSWORD ?? '',
    },
    public: {},
  },
})
