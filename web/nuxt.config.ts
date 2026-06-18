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
  modules: ['@nuxt/ui', '@nuxt/image', '@nuxt/fonts', 'nuxt-charts', '@nuxtjs/seo'],
  // Canonical site identity for SEO (canonical links, sitemap, robots, OG/
  // schema.org defaults). `url` is the production default; override per
  // environment with `NUXT_PUBLIC_SITE_URL` (nuxt-site-config reads it
  // automatically) so preview/staging deploys don't advertise the prod host.
  site: {
    url: 'https://truemain.lol',
    name: 'TrueMain',
    description: 'League of Legends champion builds, runes and skill orders from true main players.',
  },
  sitemap: {
    // Static pages are auto-discovered from the file-based routes; the dynamic
    // champion and truemain URLs come from this endpoint (see
    // server/routes/__sitemap__/urls.ts). `/dev/*` is stripped from the prod
    // build entirely (hook below) but exclude it here too so a dev-mode
    // sitemap stays clean.
    sources: ['/__sitemap__/urls'],
    exclude: ['/dev/**'],
  },
  // No dedicated social-share artwork yet — skip the on-demand OG image
  // renderer (it would pull a Satori/resvg toolchain into the build for no
  // benefit). seo-utils still derives og:title/og:description/twitter:card
  // from each page's useSeoMeta() title + description.
  ogImage: { enabled: false },
  // Self-host a single family — Inter — used across the whole app (see the
  // `--font-*` vars in main.css). Declared explicitly so the download doesn't
  // rely on CSS scanning of the theme vars.
  fonts: {
    families: [
      { name: 'Inter', provider: 'google' },
    ],
  },
  // Namespace upstream nuxt-charts components under `Nc*` so our own
  // wrappers (e.g. `components/charts/LineChart.vue` → `<ChartsLineChart>`)
  // can use the upstream chart in their template without colliding with
  // their own auto-resolved name.
  nuxtCharts: {
    prefix: 'Nc',
  },
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
  // Production-only overrides. `$production` applies on `nuxt build` and is
  // skipped under `nuxt dev`, so the dev playground stays available locally.
  $production: {
    hooks: {
      // Drop the `/dev/*` playground pages from the build entirely — they
      // exercise components with mock data and must never reach end users.
      'pages:extend'(pages) {
        const stripDev = (list: typeof pages) => {
          for (let i = list.length - 1; i >= 0; i--) {
            const page = list[i]!
            if (page.path === '/dev' || page.path.startsWith('/dev/')) {
              list.splice(i, 1)
            }
            else if (page.children?.length) {
              stripDev(page.children)
            }
          }
        }
        stripDev(pages)
      },
    },
  },
})
