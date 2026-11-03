/**
 * Empty the navigation cache (#2306, `utils/ops-cache.ts`) the moment the session ends.
 *
 * Both ways out clear the client session — the user menu's logout and the session-expiry
 * interceptor's 401 sign-out (`session-expiry.client.ts`) — so watching `loggedIn` covers
 * them without touching either. `clearNuxtData` drops Nuxt's own copies too, so nothing
 * fetched under the session that ended can be drawn under the next one.
 */
export default defineNuxtPlugin(() => {
  const { loggedIn } = useUserSession()
  watch(loggedIn, (isLoggedIn) => {
    if (!isLoggedIn) {
      clearOpsCache()
      clearNuxtData()
    }
  })
})
