// The server code the site and the admin portal share (#1625): the `/api`
// proxies' path guard and abandonment watch, the ops-log forwarder and its
// plugin, the Data Dragon patch resolution and the champion list endpoint. The
// site picks this layer up on its own (`web/layers/*`); the admin extends it by
// path. The desktop app does not — it has no Nitro server at runtime.
//
// Code in here never reaches for `~~/…`: inside a layer that alias is the
// *consuming* app's root, so a shared file would silently import whatever that
// app has at the same path. It imports the site's `shared/` by relative path
// instead, which resolves the same from either app; on the site `#shared`
// already names that directory, and the admin's alias for it
// (`#site-shared`) lives in `admin/nuxt.config.ts`.
//
// What each app must provide itself is listed in `README.md` beside this file.
export default defineNuxtConfig({})
