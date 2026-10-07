# `web/layers/common` — what the site and the desktop app share

A Nuxt layer holding the pages the site (`web/`) and the desktop app (`desktop/app/`) render identically, and
everything those pages are built from: components, composables, utils, the design system (`app/assets/css/theme.css`)
and the Nuxt UI theme (`app/app.config.ts`). The site picks it up on its own (Nuxt registers `web/layers/*`); the app
lists it in `extends`. One file, one rendering: a change made here shows in both (#1732).

The admin portal does not extend it: its image builds from `./admin`, where this directory is invisible, so
web↔admin sharing stays on labelled twin files (`.claude/docs/decisions/web-frontend-rules.md`, #1623, #1684).

A shared page is a component under `app/components/page/` (`<PageTierList>`). Each app's route file wraps it with
what is the app's own — the site's head tags and structured data, the app's `SharedPage` frame (scroll container,
and a `<Suspense>` so a tab changes before the page's first read is in, #1788) — and nothing else.

## Imports

- `#common/…` — this layer's `app/` directory. Never `~/…` from in here: inside a layer `~` is the *consuming*
  app's source dir, so a shared file would silently read whatever that app has at the same path.
- `#shared/…` — the site's `shared/` types and helpers. The layer pins the alias to `web/shared`, so the app reads
  the site's copy rather than keeping its own.

## Building from either app

The app installs only its own dependencies and never prepares the site, so nothing shared may lean on `web/` being
set up:

- `tsconfig.json` here and in `web/shared` — the bundler gives each file its nearest tsconfig, and for a shared file
  that would otherwise be the site's, whose references point into a `web/.nuxt` that does not exist in the app's
  builds. Both carry only emit options; type checking still runs through each app's `nuxt typecheck`.
- The app maps unresolved bare imports to its own `node_modules` (`desktop/app/nuxt.config.ts`), since TypeScript
  would look for them under `web/`.
- The typefaces — Inter and Geist Mono, every subset — are files in `public/fonts/`, resolved for both apps' @nuxt/fonts
  by the provider in `fonts.ts`, which imports nothing for the same reason; no build downloads a font (#1106).
- `desktop.yml` builds the app in exactly those conditions whenever this layer or `web/shared` changes.

## What each app provides

A shared page that calls a new endpoint needs it allow-listed in the app's shell, and the endpoint on production
before the next app release (`docs/ci.md`, "Desktop releases").

The shared code calls these by name; each app defines them for its host. A signature drift fails that app's
typecheck.

| Name | Site (`web/app`) | App (`desktop/app/app`) |
| --- | --- | --- |
| `useApiFetch()` | `composables/useApi.ts` — the Nitro `/api` proxy, visitor forwarded during SSR | `composables/useApi.ts` — TrueMain reads through the shell (`api_get`, and `api_post` for the composition build), each path allow-listed in `desktop/src-tauri/src/lib.rs`; `/static/*` answered from Data Dragon (`utils/static-endpoints.ts`) |
| `useChampionSlugs()` | `composables/useChampionSlugs.ts` — slugged `/champions/{slug}` | `composables/useSiteShims.ts` — the app's `/champions/{id}` |
| `useCanonicalIcon()` | `composables/useCanonicalIcon.ts` — IPX | `composables/useSiteShims.ts` — the CDN URL as given |

Shared pages link to the site's routes as they are (`truemainProfilePath`, …). A route the app does not have — a
player's profile — opens on truemain.lol in the browser (`desktop/app/app/plugins/site-routes.ts`), so the shared
code never asks which app it runs in.

Two components are host-provided the same way, because neither can draw the site's way in the app: `SkeletonImage`
(the site's goes through IPX and fades in on `load`, which WKWebView never reports for late images) and `RankIcon`
(IPX). The site's live in `web/app/components`, the app's in `desktop/app/app/components`; the shared components
use them by name. Everything else draws from this layer, so it draws the same.
