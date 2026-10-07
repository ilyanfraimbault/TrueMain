// https://nuxt.com/docs/api/configuration/nuxt-config
import { fileURLToPath } from 'node:url'
import { addServerHandler, defineNuxtModule } from '@nuxt/kit'
import { IPX_CACHE_SECONDS, IPX_ROUTE_BASE } from './shared/utils/ipx'

// How long a rendered text page stays in the Nitro cache before it is
// re-rendered in the background (see the `routeRules` entries below).
const STATIC_PAGE_SWR_SECONDS = 60 * 60 // 1 hour

// The static Inter cuts the share cards render with (see `ogImage` below):
// latin only, the weights `app/components/OgImage/*` set. From @fontsource/inter
// (Google Fonts' Inter, SIL Open Font License 1.1, licence beside the files).
const OG_IMAGE_FONTS = [400, 600, 700].map((weight) => {
  const src = `/fonts/og/inter-latin-${weight}-normal.woff`
  return { family: 'Inter', weight, style: 'normal', src, satoriSrc: src }
})

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
      route: `${IPX_ROUTE_BASE}/**`,
      handler: fileURLToPath(new URL('./server/handlers/ipx-cached.ts', import.meta.url)),
    })
  },
})

export default defineNuxtConfig({
  // `cachedIpxHandler` must come before `@nuxt/image` so the route is already
  // registered when the module decides whether to install its own.
  modules: [cachedIpxHandler, '@nuxt/ui', '@nuxt/image', '@nuxt/fonts', 'nuxt-charts', '@nuxtjs/seo', '@nuxt/scripts', '@nuxt/eslint'],
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
    // champion URLs come from this endpoint (see
    // server/routes/__sitemap__/urls.ts), which is also where the reason player
    // profiles are *not* advertised is written down (#1337). `/dev/*` is
    // stripped from the prod build entirely (hook below) but exclude it here
    // too so a dev-mode sitemap stays clean.
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
  //   - only the two pages that have a card call `defineOgImage()`;
  //     every other page keeps the plain og:title/og:description seo-utils
  //     already derives, and never touches the renderer;
  //   - the `.satori.vue` suffix pins the renderer to Satori + resvg (added as
  //     explicit deps). No `.browser.vue` component exists, so playwright and
  //     a headless Chromium are never pulled into the image;
  //   - rendering happens in the web container, which shares a small VPS with
  //     Postgres/Mongo/the ingestor, so every render is cached and the
  //     crawler-only traffic pattern keeps it cold in practice.
  // Fonts: Satori reads neither WOFF2 nor variable fonts, so the cards cannot
  // use the files the site is set in. They get static latin cuts of Inter
  // instead — the three weights the cards set — committed under
  // `public/fonts/og/` and handed to the module by the `nitro:config` hook
  // below; each render loads them with an internal request (nothing leaves the
  // container, and nothing is downloaded at build time either, #1106).
  ogImage: {
    // 1 h, mirroring the app-wide cache TTL (utils/static-cache.ts and the
    // server `defineCachedEventHandler`s). Long enough that a burst of
    // unfurls costs one render, short enough that a player's LP or a
    // champion's win rate on the card is never more than an hour behind the
    // page it was shared from.
    cacheMaxAgeSeconds: 60 * 60,
    security: {
      // The module's 15 s default is shorter than a cold champion card (#1545).
      // Measured on preprod: island-fetch 13.5 s + render 3.8 s = 17.4 s, the
      // island almost entirely waiting on `GET /champions` for the card's
      // filter — a slice only the cards read, so nothing else keeps the API's
      // cache warm for it, and a cold key cost 6–12.5 s there. At 15 s the
      // first crawler got a 408 and, worse, nothing was cached, so the next
      // unfurl started from zero again. 30 s covers the measured worst case
      // with headroom. It is a ceiling, not a cost: a warm render answers from
      // the cache in milliseconds, and the 408 still bounds a stuck upstream.
      // The same value bounds the island fetch and the renderer hooks.
      renderTimeout: 30_000,
    },
    defaults: {
      // Discord/X render 2:1 previews; 1200×630 is the size both crop to
      // without letterboxing.
      width: 1200,
      height: 630,
    },
  },
  // Namespace upstream nuxt-charts components under `Nc*` so our own
  // wrappers (e.g. `components/charts/LineChart.vue` → `<ChartsLineChart>`)
  // can use the upstream chart in their template without colliding with
  // their own auto-resolved name.
  nuxtChartsLegacy: {
    prefix: 'Nc',
  },
  app: {
    head: {
      // The app is dark-only. Nuxt UI keys its own theme off the `.dark` class,
      // and the surface ladder in theme.css is written to out-specify it either
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
    // Page transitions (#1621, #1689); the motion itself is in main.css. A Vue
    // `<Transition>` around the page's `<Suspense>`, not the View Transitions
    // API: it only starts once the destination has resolved its awaited data,
    // so the outgoing page and the loading bar stay live during the wait,
    // where a view transition freezes the whole frame until `page:finish`.
    // No `mode: 'out-in'`: a navigation during its leave left a blank page for
    // good (#1714) — main.css makes the leave instant instead.
    pageTransition: { name: 'page' },
  },
  // `~/…`, not a root-relative `./app/…`: since the Nuxt 4.5 / Vite 8.2 bump
  // the relative form is resolved against the build dir (`.nuxt/`) in dev, so
  // `nuxt dev` failed to resolve the stylesheet at all on a clean install and
  // the client bundle never booted. The alias resolves off srcDir in both.
  css: ['~/assets/css/main.css'],
  // Registers Nuxt UI's `Prose*` components without @nuxt/content (#1624):
  // the text pages (/about, /privacy, /terms) are written with them, so their
  // typography is themed once under `ui.prose` in `app.config.ts`.
  ui: {
    prose: true,
    // Without it Nuxt UI hands Tailwind the theme of *every* component it ships
    // as a source, used or not, and all their utilities land in the stylesheet
    // every page downloads. Detection scans the layers' `app/` dirs for `U*`
    // names (templates and scripts) and keeps only those themes plus their
    // dependencies: the entry CSS drops from 38.9 to 29.5 KB gzip (#1641,
    // measured). Its one blind spot is a Nuxt UI component chosen at runtime
    // from a string — list such a component here (`['Modal']`) instead of `true`.
    experimental: {
      componentDetection: true,
    },
  },
  hooks: {
    // Nuxt UI registers the ~45 `Prose*` components as *global*, which is what
    // MDC needs to resolve them by name at runtime — and which puts one lazy
    // registration per component in the entry script of every page (+2.5 kB
    // gzip, measured). Nothing here renders MDC: the pages name the components
    // in their templates, so plain auto-imports resolve them at compile time
    // and only the pages that use them pay for them.
    'components:extend'(components) {
      for (const component of components) {
        if (component.pascalName.startsWith('Prose')) {
          component.global = false
        }
      }
    },
    // @nuxt/scripts registers its first-party proxy (`/_scripts/p/**`) on every
    // server build, configured or not. Nothing here proxies a script (Umami is
    // loaded from our own instance, unbundled), so the route only answered
    // 500 "First-party proxy not configured" to whoever probed it, and its
    // undici-based fetcher added ~1 MB to the server output (#1622, measured).
    //
    // nuxt-og-image builds the cards' font list (`#og-image/fonts`) from what
    // @nuxt/fonts emits: here, variable WOFF2 that Satori cannot read, so left
    // alone it would download static stand-ins from Google at build time — the
    // network dependency #1106 removes — and fall back to its bundled Inter
    // 400/700 when that fails, setting every `fontWeight: 600` in the wrong
    // weight. The list is replaced with the committed files instead.
    'nitro:config'(config) {
      config.handlers = config.handlers?.filter(handler => handler?.route !== '/_scripts/p/**')
      config.virtual = { ...config.virtual, '#og-image/fonts': `export default ${JSON.stringify(OG_IMAGE_FONTS)}` }
    },
  },
  experimental: {
    // Inline the payload in the HTML of the first render and keep
    // `_payload.json` for client-side navigation only (#1618). It only ever
    // applies to the `swr` text pages below — Nuxt extracts payloads at
    // runtime for cached routes alone; every other page always inlines.
    // Measured: one fewer request on a first visit to those pages, client
    // navigation unchanged. It is Nuxt 5's default.
    payloadExtraction: 'client',
  },
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
  // falls through to the preference (dark, set by `layers/common`).
  colorMode: {
    storageKey: 'truemain-color-mode',
  },
  image: {
    // Allow-list for the ipx provider's URL generation. The storage options
    // themselves live in server/handlers/ipx-cached.ts, which owns `/_ipx/**`.
    domains: ['ddragon.leagueoflegends.com', 'raw.communitydragon.org'],
  },
  // Satori shapes text with harfbuzzjs, which loads `hb.wasm` from a path it
  // computes at runtime. Nitro's dependency trace only follows static
  // imports, so the `.wasm` never reached `.output/server/node_modules/` and
  // every `/_og/**` render died with `ENOENT ... hb.wasm` — a 500, i.e. a
  // share card that unfurls on X/Discord with no image at all. Listing the
  // file puts it back in the trace; it is the only asset in the OG chain
  // loaded that way, which is why this is one entry and not a pattern.
  nitro: {
    // One worker per useful core behind the same port (#1579); see docs/ci.md.
    preset: 'node-cluster',
    externals: {
      traceInclude: [fileURLToPath(import.meta.resolve('harfbuzzjs/hb.wasm'))],
    },
  },
  routeRules: {
    // IPX responses are deterministic per (source URL, modifiers) — safe to
    // mark immutable and cache for a week in shared/private caches. Note this
    // is the *browser* cache; the server-side one is in the handler.
    [`${IPX_ROUTE_BASE}/**`]: {
      headers: {
        'cache-control': `public, max-age=${IPX_CACHE_SECONDS}, immutable`,
      },
    },
    // The three text pages fetch nothing of their own, so their HTML only
    // depends on the shell: render it once, then serve it from the Nitro cache
    // and re-render in the background when the entry ages out (#1617).
    // `swr` rather than `prerender` on purpose: a prerendered page would carry
    // the *build* environment's `runtimeConfig.public` — no Umami, no version
    // stamp, the prod canonical URL on preprod — for the whole visit that lands
    // on it, which is exactly what the runtime-config decision below avoids.
    // The TTL matches the champion slug map's own server cache
    // (`server/api/static/champion-slugs.get.ts`), so a cached page is never
    // staler than an SSR one.
    '/about': { swr: STATIC_PAGE_SWR_SECONDS },
    '/privacy': { swr: STATIC_PAGE_SWR_SECONDS },
    '/terms': { swr: STATIC_PAGE_SWR_SECONDS },
  },
  runtimeConfig: {
    apiBaseUrl: process.env.NUXT_API_BASE_URL
      ?? 'http://localhost:5008',
    // Server-only key for the API's `POST /internal/logs` (#1556), from
    // NUXT_LOG_INGEST_KEY. Empty keeps error forwarding off
    // (server/plugins/log-forwarding.ts).
    logIngestKey: '',
    // Which desktop app builds the site offers (#1772), from NUXT_DESKTOP_CHANNEL:
    // `stable` serves only a release promoted by hand, `beta` (preprod) the newest
    // build too. See server/utils/desktop-release.ts.
    desktopChannel: 'stable',
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
