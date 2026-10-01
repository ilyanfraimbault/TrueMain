# `web/layers/common` — what the site and the desktop app share

A Nuxt layer holding the pages the site (`web/`) and the desktop app (`desktop/app/`) render identically, and
everything those pages are built from: components, composables, utils, the design system (`app/assets/css/theme.css`)
and the Nuxt UI theme (`app/app.config.ts`). The site picks it up on its own (Nuxt registers `web/layers/*`); the app
lists it in `extends`. One file, one rendering: a change made here shows in both (#1732).

A shared page is a component under `app/components/page/` (`<PageTierList>`). Each app's route file wraps it with
what is the app's own — the site's head tags and structured data, the app's scroll container — and nothing else.

## Imports

- `#common/…` — this layer's `app/` directory. Never `~/…` from in here: inside a layer `~` is the *consuming*
  app's source dir, so a shared file would silently read whatever that app has at the same path.
- `#shared/…` — the site's `shared/` types and helpers. The layer pins the alias to `web/shared`, so the app reads
  the site's copy rather than keeping its own.

## What each app provides

The shared code calls these by name; each app defines them for its host. A signature drift fails that app's
typecheck.

| Name | Site (`web/app`) | App (`desktop/app/app`) |
| --- | --- | --- |
| `useApiFetch()` | `composables/useApi.ts` — the Nitro `/api` proxy, visitor forwarded during SSR | `composables/useApi.ts` — TrueMain reads through the shell (`api_get`), `/static/*` answered from Data Dragon (`utils/static-endpoints.ts`) |
| `useChampionSlugs()` | `composables/useChampionSlugs.ts` — slugged `/champions/{slug}` | `composables/useSiteShims.ts` — the app's `/champions/{id}` |
| `useCanonicalIcon()` | `composables/useCanonicalIcon.ts` — IPX | `composables/useSiteShims.ts` — the CDN URL as given |

Two components are host-provided the same way, because neither can draw the site's way in the app: `SkeletonImage`
(the site's goes through IPX and fades in on `load`, which WKWebView never reports for late images) and `RankIcon`
(IPX). The site's live in `web/app/components`, the app's in `desktop/app/app/components`; the shared components
use them by name. Everything else draws from this layer, so it draws the same.
