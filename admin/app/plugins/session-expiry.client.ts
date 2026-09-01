/**
 * Sign the operator out the moment the backend says the session is gone (#1225).
 *
 * `middleware/auth.global.ts` only runs on navigation, and this app is `ssr: false`:
 * an operator parked on /health or /logs for hours never navigates, so an expired
 * sealed cookie used to surface as every panel turning red with "Failed to load" —
 * a logout that reads as a backend outage.
 *
 * Intercepting `$fetch` globally rather than fixing `useOps` is what makes this
 * complete: it also covers the imperative one-shots (`getAccountExplorer`,
 * `getCandidateDetail`, `getSeedRequest`, …) and the seed poller, which swallows
 * its errors by design and would otherwise loop silently until its 30 s deadline
 * with nothing on screen to say why. `useFetch` resolves `globalThis.$fetch` at
 * call time, so replacing it here covers reactive and imperative callers alike.
 */
export default defineNuxtPlugin((nuxtApp) => {
  const { clear, loggedIn } = useUserSession()

  // A dashboard page fires a dozen requests in parallel, so an expiry arrives as a
  // burst of 401s. Sharing one in-flight promise turns that burst into exactly one
  // session clear and one navigation.
  let pending: Promise<void> | null = null

  async function signOut(): Promise<void> {
    await nuxtApp.runWithContext(async () => {
      if (loggedIn.value) {
        // Drop the client session first: `loggedIn` has to be false before we
        // navigate, or the global middleware bounces /login straight back to /.
        await clear()
      }
      const target = loginRedirectTarget(useRouter().currentRoute.value.path)
      if (target) {
        await navigateTo(target)
      }
    })
  }

  globalThis.$fetch = $fetch.create({
    onResponseError({ request, response }) {
      const url = typeof request === 'string' ? request : request.url
      if (!isSessionExpiry(url, response.status)) {
        return
      }
      pending ??= signOut().finally(() => {
        pending = null
      })
      // Returned so ofetch awaits the redirect before rejecting the call; the
      // caller still sees its error, it just no longer renders as an outage.
      return pending
    },
  })
})
