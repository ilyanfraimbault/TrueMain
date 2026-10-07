import { fileURLToPath } from 'node:url'
import { VENDORED_FONT_FAMILIES, vendoredFontProvider } from './fonts'

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
  // Both apps run @nuxt/fonts (the site lists it, Nuxt UI installs it in the
  // desktop app), and both set the two families from `theme.css`: they resolve
  // from the files this layer ships, never from a font CDN (`fonts.ts`, #1106).
  // Every remote provider is switched off, so nothing in a build can reach for
  // the network — a family nobody vendored falls through to the system stack
  // instead of being fetched.
  //
  // Deliberately **no `weights`**: the files are variable, one face covers
  // 100–900, so `font-light` (the footer links) and `font-extrabold`
  // (TierBadge's tier letters) get their real weight rather than a faked one.
  fonts: {
    providers: {
      vendored: vendoredFontProvider,
      google: false,
      googleicons: false,
      bunny: false,
      fontshare: false,
      fontsource: false,
      adobe: false,
      npm: false,
    },
    priority: ['vendored'],
    families: VENDORED_FONT_FAMILIES.map(name => ({ name, provider: 'vendored' })),
  },
})
