// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  modules: ['@nuxt/ui', '@nuxt/image'],
  css: ['./app/assets/css/main.css'],
  compatibilityDate: '2026-05-15',
  devtools: { enabled: true },
  colorMode: {
    preference: 'dark',
    fallback: 'dark',
  },
  image: {
    domains: ['ddragon.leagueoflegends.com', 'raw.communitydragon.org'],
  },
  runtimeConfig: {
    apiBaseUrl: process.env.NUXT_API_BASE_URL
      ?? 'http://localhost:5008',
    public: {},
  },
})
