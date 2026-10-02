/**
 * Counts the pages the player opens (#1805) — a view per page reached, not per
 * filter change on it. The shell keeps the counts and sends them, and does
 * nothing with them when the player turned "Share Anonymous Usage Data" off.
 * An overlay panel's page has no key (`utils/telemetry.ts`), and a browser
 * running `npm run dev` has no shell to count in.
 */
export default defineNuxtPlugin(() => {
  if (!insideTauri()) return
  const router = useRouter()
  router.afterEach((to, from, failure) => {
    if (failure) return
    // The first navigation comes from nowhere: it is the page the app opened on.
    if (from.matched.length > 0 && to.path === from.path) return
    const page = telemetryPage(to.name)
    if (!page) return
    void import('@tauri-apps/api/core')
      .then(({ invoke }) => invoke('telemetry_page', { page }))
      .catch(() => {})
  })
})
