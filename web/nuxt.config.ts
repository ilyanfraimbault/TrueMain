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
    // Game icons are tiny (≤10 KB) and rendered at many CSS sizes across the
    // page. Disable the 2x DPR srcset variant globally — without it every
    // <NuxtImg> requests at least two IPX URLs per asset (e.g. s_36x36 +
    // s_72x72), even when CSS will scale them anyway. Saves ~half the
    // image traffic on builds-heavy pages.
    densities: '1x',
  },
  runtimeConfig: {
    apiBaseUrl: process.env.NUXT_API_BASE_URL
      ?? 'http://localhost:5008',
    public: {},
  },
})
