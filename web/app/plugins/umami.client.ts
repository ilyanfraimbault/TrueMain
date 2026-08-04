/**
 * Umami analytics tracker (#728) — self-hosted, cookieless visitor/session
 * tracking. Also loads Umami's session replay/heatmap recorder, whose
 * sampling rate, masking, and enablement are configured per-website in
 * Umami's own settings, not here. Both scripts are injected only when the
 * instance host and website id are configured (see `runtimeConfig.public.umami`
 * in nuxt.config.ts), so unconfigured environments never load any tracker.
 *
 * Client-only on purpose: the tracker observes browser navigation and has no
 * SSR role, and injecting it client-side keeps it out of the server-rendered
 * HTML of environments that disable it.
 */
export default defineNuxtPlugin(() => {
  const { host, websiteId } = useRuntimeConfig().public.umami
  if (!host || !websiteId) {
    return
  }

  useHead({
    script: [
      {
        src: `${host.replace(/\/+$/, '')}/script.js`,
        defer: true,
        'data-website-id': websiteId,
      },
      {
        src: `${host.replace(/\/+$/, '')}/recorder.js`,
        defer: true,
        'data-website-id': websiteId,
      },
    ],
  })
})
