// https://nuxt.com/docs/api/configuration/nuxt-config

// Most upstream image sources we hit (DDragon item/champion/spell icons,
// CommunityDragon perks) are content-addressed per (patch, asset), so once
// IPX has processed a URL the resulting bytes never change. Position icons
// used to resolve through CommunityDragon's `/latest/` path but are now
// bundled under /public/positions to avoid hitting a third party on cold
// loads. Cache the remaining upstream assets aggressively in the browser to
// avoid re-issuing dozens of requests on each client-side navigation.
const IPX_IMAGE_CACHE_SECONDS = 60 * 60 * 24 * 7 // 7 days

export default defineNuxtConfig({
  modules: ['@nuxt/ui', '@nuxt/image'],
  css: ['./app/assets/css/main.css'],
  compatibilityDate: '2026-05-15',
  devtools: { enabled: true },
  colorMode: {
    preference: 'dark',
    fallback: 'dark',
  },
  image: {
    domains: ['ddragon.leagueoflegends.com', 'raw.communitydragon.org'],
    // Tell IPX's HTTP storage to ignore upstream Cache-Control (CommunityDragon
    // `latest` redirects use a short TTL) and serve our own long max-age. The
    // outgoing Cache-Control on /_ipx/** is still pinned via routeRules below
    // so it includes `immutable`, which IPX's built-in header does not.
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
    apiBaseUrl: process.env.NUXT_API_BASE_URL
      ?? 'http://localhost:5008',
    public: {},
  },
})
