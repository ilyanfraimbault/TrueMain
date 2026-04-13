// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  modules: ['@nuxt/ui'],
  css: ['./app/assets/css/main.css'],
  compatibilityDate: '2025-07-15',
  devtools: { enabled: true },
  ui: {
    fonts: false
  },
  runtimeConfig: {
    public: {
      apiBaseUrl: process.env.NUXT_PUBLIC_API_BASE_URL ?? 'http://localhost:5008'
    }
  }
})
