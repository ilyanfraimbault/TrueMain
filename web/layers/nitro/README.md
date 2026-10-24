# `web/layers/nitro` — the server code the site and the admin share

A Nuxt layer holding the Nitro code the site (`web/`) and the admin portal (`admin/`) used to keep as two
labelled copies (#1226, #1625): the containment guard and abandonment watch of their backend proxies, the ops-log
forwarder and its plugin, the Data Dragon patch resolution and the champion list endpoint. The site picks it up on
its own (Nuxt registers `web/layers/*`); the admin lists it in `extends`. The desktop app does not extend it — it
has no Nitro server at runtime — and the admin does not extend `web/layers/common`, the pages the site and the app
share.

| File | Used by |
| --- | --- |
| `server/utils/proxy-path.ts` | the site's `/api` and the admin's `/api/ops` proxies |
| `server/utils/abandoned-request.ts` | the same two proxies, and the log-forwarding plugin |
| `server/utils/log-forwarder.ts`, `server/plugins/log-forwarding.ts` | every 5xx and proxied 429/5xx of both servers |
| `server/utils/ingest-forwarder.ts` | the log-forwarding plugin, and the admin's edge error relay (#1580) |
| `server/utils/ddragon-patch.ts` | every `/api/static/*` endpoint of both apps |
| `server/api/static/champions.get.ts` | the champion list, both apps |

## Imports

- Relative imports, never `~~/…`: inside a layer `~~` is the *consuming* app's root, so a shared file would silently
  read whatever that app has at the same path. That covers the site's `shared/` too (`../../../../shared/…`).
- The layer defines no alias of its own. The admin's own code reaches the site's `shared/utils/ddragon.ts` and
  `shared/types/static-data.ts` through `#site-shared`, an alias in `admin/nuxt.config.ts`; on the site `#shared`
  already names that directory.

## What each app provides

The shared code calls these by name; each app defines them for its host. A missing or mistyped one fails that
app's typecheck.

| Name | Site (`web/`) | Admin (`admin/`) |
| --- | --- | --- |
| `logForwardingTarget()` | `server/utils/log-forwarding-target.ts` — process `Web`, `apiBaseUrl` | `server/utils/log-forwarding-target.ts` — process `Admin`, `opsApiBaseUrl` |

The champion list is open on the site. The admin puts every `/api/static/*` route behind the operator session in
`server/middleware/static-session.ts`, the same prefix its client reads a 401 from (`app/utils/session-expiry.ts`).

## Building the admin

The admin image builds from `./admin`, where this directory does not exist. Its Dockerfiles copy three things from
a second build context named `web` (`--build-context web=./web`, `additional_contexts` in compose, `build-contexts`
in CI): this layer, the site's `shared/`, and `layers/common/tsconfig.json`, which the tsconfigs of both extend. A plain
`docker build ./admin` without it fails. The bundler gives each file its nearest tsconfig, which is why
`tsconfig.json` here carries only emit options, like `web/layers/common`'s.

Tests live in the site's suite (`web/tests/`); the admin's own handler tests import the layer by path.
