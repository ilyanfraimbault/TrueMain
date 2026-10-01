import { existsSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

// The site the app belongs to (`src-tauri/src/site.rs`): production unless the
// build names another — the release workflow does for the preprod build, and a
// developer does in `desktop/.env.local`, read here for `npm run dev` and by
// `npm run tauri` for the shell.
const localEnv = fileURLToPath(new URL('../.env.local', import.meta.url))
if (existsSync(localEnv)) process.loadEnvFile(localEnv)
const siteUrl = (process.env.TRUEMAIN_SITE_URL?.trim() || 'https://truemain.lol').replace(/\/+$/, '')

export default defineNuxtConfig({
  compatibilityDate: '2025-07-15',
  // The pages and components shared with the site (#1732, its README lists
  // what this app provides in return).
  extends: ['../../web/layers/common'],
  // The shared files live under `web/`, so TypeScript would resolve their bare
  // imports from `web/node_modules` — absent wherever only this app is
  // installed (its CI and release builds), which silently types every shared
  // component as `any`. Anything not mapped otherwise resolves from here.
  typescript: {
    tsConfig: {
      compilerOptions: {
        paths: { '*': [fileURLToPath(new URL('./node_modules/*', import.meta.url))] },
      },
    },
  },
  // The app is a static bundle inside a webview: there is no Node server at
  // runtime, so no SSR and no Nitro server routes. Anything the site does
  // through `server/api` has to be done against the API directly here. The two
  // server routes answer only in `npm run dev`: `server/routes/__sim/lcu.ts`,
  // the draft simulator's relay, and `server/routes/__dev/recording-file.ts`,
  // which serves the recordings fixtures' video.
  ssr: false,
  modules: ['@nuxt/ui'],
  css: ['~/assets/css/main.css'],
  devServer: { port: 3003 },
  // Tauri serves the bundle from a custom protocol; hashed asset names at the
  // root keep every reference relative to it.
  app: { baseURL: './' },
  // The route lives in the hash: the custom protocol serves files, not an SPA
  // fallback, so a history-mode path would 404 on a reload.
  router: { options: { hashMode: true } },
  // No server to resolve an icon at runtime and a CSP that only reaches Data
  // Dragon: every icon the source names is bundled at build time instead.
  icon: {
    provider: 'none',
    clientBundle: { scan: true },
  },
  nitro: {
    preset: 'static',
    // `npm run dev` in a browser has no Rust to proxy API calls through, so the
    // dev server does it instead, against the same site's entry point. Dev
    // only: a static build has no server, and the packaged app asks Rust.
    devProxy: { '/api': { target: `${siteUrl}/api`, changeOrigin: true } },
  },
  // Where `openOnSite` sends a browser-only `npm run dev`; inside the shell the
  // URL is Rust's to build.
  vite: { define: { 'import.meta.env.TRUEMAIN_SITE_URL': JSON.stringify(siteUrl) } },
  devtools: { enabled: false },
  hooks: {
    // The `/dev/*` pages (the draft simulator) are development tools: a build
    // never carries them.
    'pages:extend'(pages) {
      if (process.env.NODE_ENV !== 'production') return
      for (let index = pages.length - 1; index >= 0; index--) {
        const path = pages[index]!.path
        if (path === '/dev' || path.startsWith('/dev/')) pages.splice(index, 1)
      }
    },
  },
})
