// https://nuxt.com/docs/api/configuration/nuxt-config
import { fileURLToPath } from 'node:url'
import { addServerHandler, defineNuxtModule } from '@nuxt/kit'
import { IPX_CACHE_SECONDS } from './shared/utils/ipx'

// Claims `/_ipx/**` before @nuxt/image sets up its own handler. The module
// checks `nuxt.options.serverHandlers` for the route and steps aside when it
// finds one (`hasUserProvidedIPX`), which is exactly what we want: everything
// else about the module stays in place, but the route is served by
// server/handlers/ipx-cached.ts, which adds the response cache IPX lacks.
// Registered as a module (not `nitro.handlers`) because that is the only form
// that also applies to `nuxt dev`.
const cachedIpxHandler = defineNuxtModule({
  meta: { name: 'truemain-cached-ipx' },
  setup() {
    addServerHandler({
      route: '/_ipx/**',
      handler: fileURLToPath(new URL('./server/handlers/ipx-cached.ts', import.meta.url)),
    })
  },
})

export default defineNuxtConfig({
  // `cachedIpxHandler` must come before `@nuxt/image` so the route is already
  // registered when the module decides whether to install its own.
  modules: [cachedIpxHandler, '@nuxt/ui', '@nuxt/image', '@nuxt/fonts', 'nuxt-charts', '@nuxtjs/seo'],
  // Canonical site identity for SEO (canonical links, sitemap, robots, OG/
  // schema.org defaults). `url` is the production default; override per
  // environment with `NUXT_PUBLIC_SITE_URL` (nuxt-site-config reads it
  // automatically) so preview/staging deploys don't advertise the prod host.
  site: {
    url: 'https://truemain.lol',
    name: 'TrueMain',
    description: 'League of Legends champion builds, runes and skill orders from true main players.',
    // seo-utils appends `%separator %siteName` to every page title — pages
    // must NOT hardcode the brand themselves or it shows up twice in search
    // results. `·` matches the separator style used inside compound titles.
    separator: '·',
  },
  // The brand entity. Without this, nuxt-schema-org emits only `WebSite` +
  // `WebPage` + `BreadcrumbList` — nothing a search engine can attach a *brand*
  // to, which is why `truemain lol` resolves to the site but the bare
  // `truemain` does not (#1122). Setting `identity` emits an `Organization`
  // node and makes the `WebSite` node carry it as `publisher`, linking every
  // page to the brand rather than leaving them as orphan documents.
  //
  // Declared here rather than in `app.vue` because none of it depends on
  // runtime data — the module's own guidance.
  //
  // Deliberately **no `sameAs`**: it is the field that ties the brand to its
  // social profiles, and TrueMain has none it owns yet. A guessed or
  // aspirational URL is worse than an absent one — it points the entity graph
  // at an account someone else controls. Add the real handles when they exist.
  schemaOrg: {
    identity: {
      type: 'Organization',
      name: 'TrueMain',
      // No `url` — the module resolves it from `site.url` above, which
      // `NUXT_PUBLIC_SITE_URL` overrides per environment. Repeating the prod
      // host here would make preprod's Organization node advertise prod, the
      // one thing that override exists to prevent.
      // Square, explicitly sized — see the comment in the file itself.
      logo: '/brand/truemain-logo.svg',
      description: 'League of Legends champion builds, runes and skill orders computed from the games of players who actually main the champion.',
    },
  },
  sitemap: {
    // Static pages are auto-discovered from the file-based routes; the dynamic
    // champion and truemain URLs come from this endpoint (see
    // server/routes/__sitemap__/urls.ts). `/dev/*` is stripped from the prod
    // build entirely (hook below) but exclude it here too so a dev-mode
    // sitemap stays clean.
    sources: ['/__sitemap__/urls'],
    // `/truemains/favorites` renders a per-visitor localStorage list — there is
    // nothing stable for a crawler to index (the page also sets `noindex`).
    // `/builder` is the legacy redirect to `/matchup` (#939) — kept for old
    // links, not something to advertise.
    exclude: ['/dev/**', '/truemains/favorites', '/builder'],
  },
  // On-demand social-share artwork (#926). This was `enabled: false` from the
  // SEO foundation (#551) for one reason only — "no dedicated share artwork
  // yet, so the Satori/resvg toolchain would be build weight for no benefit".
  // #926 supplies the artwork (app/components/OgImage/*.satori.vue), so the
  // trade flips; the *cost* half of that note still stands and is why the
  // setup below stays deliberately narrow:
  //   - only the two pages that have a card call `defineOgImageComponent()`;
  //     every other page keeps the plain og:title/og:description seo-utils
  //     already derives, and never touches the renderer;
  //   - the `.satori.vue` suffix pins the renderer to Satori + resvg (added as
  //     explicit deps). No `.browser.vue` component exists, so playwright and
  //     a headless Chromium are never pulled into the image;
  //   - rendering happens in the web container, which shares a small VPS with
  //     Postgres/Mongo/the ingestor, so every render is cached and the
  //     crawler-only traffic pattern keeps it cold in practice.
  // Fonts come from @nuxt/fonts (Inter) — the module re-downloads them in a
  // Satori-compatible static format at build time, so no runtime font fetch.
  ogImage: {
    // 1 h, mirroring the app-wide cache TTL (utils/static-cache.ts and the
    // server `defineCachedEventHandler`s). Long enough that a burst of
    // unfurls costs one render, short enough that a player's LP or a
    // champion's win rate on the card is never more than an hour behind the
    // page it was shared from.
    cacheMaxAgeSeconds: 60 * 60,
    defaults: {
      // Discord/X render 2:1 previews; 1200×630 is the size both crop to
      // without letterboxing.
      width: 1200,
      height: 630,
    },
  },
  // Self-host the two families the app uses (see the `--font-*` vars in
  // main.css): Inter for everything the reader reads, measurements included, and
  // Geist Mono for the few places monospace is the meaning — tier letters, the
  // empty-slot glyph, hex codes. Declared explicitly so the download doesn't rely
  // on CSS scanning of the *theme vars* — the family names only ever appear
  // inside `--font-sans` / `--font-mono`.
  //
  // Deliberately **no `weights`**. Pinning the list looks like a free saving and
  // is a trap: the module already discovers the weights it needs from the
  // `font-weight` declarations Tailwind emits, so a pin can only ever be a
  // second, staler copy of that answer. A first cut here pinned 400/500/600/700
  // and silently dropped `font-extrabold` (TierBadge's tier letters) and
  // `font-light` (the footer links) — the browser doesn't error on a missing
  // face, it fakes one, so the regression would have shipped looking merely
  // slightly off. Any future `font-*` utility would have re-armed it.
  fonts: {
    families: [
      { name: 'Inter', provider: 'google' },
      { name: 'Geist Mono', provider: 'google' },
    ],
  },
  // Namespace upstream nuxt-charts components under `Nc*` so our own
  // wrappers (e.g. `components/charts/LineChart.vue` → `<ChartsLineChart>`)
  // can use the upstream chart in their template without colliding with
  // their own auto-resolved name.
  nuxtCharts: {
    prefix: 'Nc',
  },
  app: {
    head: {
      // The app is dark-only. Nuxt UI keys its own theme off the `.dark` class,
      // and the surface ladder in main.css is written to out-specify it either
      // way, but pinning the class server-side means the very first painted
      // frame is already dark — without it the document flashes Nuxt UI's light
      // defaults until @nuxtjs/color-mode's script runs.
      htmlAttrs: { class: 'dark' },
      link: [
        // .ico first as the universal fallback; SVG last so browsers that
        // support it (all modern ones) pick the crisp vector M-check mark.
        { rel: 'icon', href: '/favicon.ico', sizes: 'any' },
        { rel: 'icon', type: 'image/svg+xml', href: '/favicon.svg' },
      ],
    },
  },
  css: ['./app/assets/css/main.css'],
  compatibilityDate: '2026-05-15',
  devtools: { enabled: true },
  // Dark-only: there is no colour-mode toggle in the header any more. The
  // module stays installed because @nuxt/ui depends on it, and it has no
  // "forced" switch — `preference` is only a *default*, and a returning visitor
  // who had toggled light before the button was removed still carries
  // `nuxt-color-mode=light` in localStorage. Nothing would ever write over it
  // again, so they would be pinned for good to a theme that is no longer
  // designed or tested. Moving to a fresh storage key retires those values in
  // one line: the new key is never written (no toggle exists), so every visit
  // falls through to the preference below.
  colorMode: {
    preference: 'dark',
    fallback: 'dark',
    storageKey: 'truemain-color-mode',
  },
  image: {
    // Allow-list for the ipx provider's URL generation. The storage options
    // themselves live in server/handlers/ipx-cached.ts, which owns `/_ipx/**`.
    domains: ['ddragon.leagueoflegends.com', 'raw.communitydragon.org'],
  },
  routeRules: {
    // IPX responses are deterministic per (source URL, modifiers) — safe to
    // mark immutable and cache for a week in shared/private caches. Note this
    // is the *browser* cache; the server-side one is in the handler.
    '/_ipx/**': {
      headers: {
        'cache-control': `public, max-age=${IPX_CACHE_SECONDS}, immutable`,
      },
    },
  },
  runtimeConfig: {
    apiBaseUrl: process.env.NUXT_API_BASE_URL
      ?? 'http://localhost:5008',
    public: {
      // Which deployed environment this container is (`preprod` / `production`),
      // and the build running in it — the preprod pipeline stamps a prerelease
      // version (`1.20.0-rc.4`), the prod deploy the release tag (`1.19.0`).
      // Both are read at *runtime* from NUXT_PUBLIC_APP_ENV / NUXT_PUBLIC_APP_VERSION
      // rather than baked in at image build time, so one image can be promoted
      // and no Docker layer is invalidated by a version that changes every
      // merge. Empty locally, which is what makes the footer label disappear
      // in dev (see app/utils/app-version.ts).
      appEnv: '',
      appVersion: '',
      // Self-hosted Umami analytics (app/plugins/umami.client.ts). Both must
      // be set (NUXT_PUBLIC_UMAMI_HOST / NUXT_PUBLIC_UMAMI_WEBSITE_ID) for the
      // tracker to load — dev and preview environments leave them empty, so
      // no tracking script ships there.
      umami: {
        host: '',
        websiteId: '',
      },
    },
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
