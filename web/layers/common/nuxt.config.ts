import { fileURLToPath } from 'node:url'

// The pages and components the site and the desktop app share (#1732). The site
// picks this layer up on its own (`web/layers/*`); the app extends it by path.
//
// Code in here never reaches for `~/…` or `~~/…`: inside a layer those aliases
// are the *consuming* app's directories, so they would silently pick up whatever
// that app has at the same path. Shared modules import each other through
// `#common`, and the site's `shared/` types and helpers through `#shared`, which
// this layer pins to the site's directory: both apps read that one copy.
//
// What each app must provide itself — the API fetcher, the icon URL transform,
// champion links — is listed in `README.md` beside this file.
export default defineNuxtConfig({
  alias: {
    '#common': fileURLToPath(new URL('./app', import.meta.url)),
    '#shared': fileURLToPath(new URL('../../shared', import.meta.url)),
  },
  // The design system is dark-only (`theme.css` defines no light surfaces), so
  // neither app may follow a light system preference.
  colorMode: {
    preference: 'dark',
    fallback: 'dark',
  },
})
