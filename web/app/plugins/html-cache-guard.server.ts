// A fetch that failed during this render shows on the page as an inline error or a gap, under a
// 200. The HTML cache must not hold on to that moment (`server/utils/html-cache.ts`, #1557).
export default defineNuxtPlugin((nuxtApp) => {
  nuxtApp.hook('app:rendered', () => {
    const failed = Boolean(nuxtApp.payload.error)
      || Object.values(nuxtApp.payload._errors ?? {}).some(Boolean)
    if (failed && nuxtApp.ssrContext) nuxtApp.ssrContext.event.context.renderFailed = true
  })
})
