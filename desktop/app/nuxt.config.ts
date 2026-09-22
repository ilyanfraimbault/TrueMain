export default defineNuxtConfig({
  compatibilityDate: '2025-07-15',
  // The app is a static bundle inside a webview: there is no Node server at
  // runtime, so no SSR and no Nitro server routes. Anything the site does
  // through `server/api` has to be done against the API directly here.
  ssr: false,
  modules: ['@nuxt/ui'],
  css: ['~/assets/css/main.css'],
  devServer: { port: 3003 },
  // Tauri serves the bundle from a custom protocol; hashed asset names at the
  // root keep every reference relative to it.
  app: { baseURL: './' },
  nitro: { preset: 'static' },
  devtools: { enabled: false },
})
