/**
 * A route change counts as loading (#1788) from its first guard until it is
 * confirmed, refused (`site-routes` hands a site-only route to the browser) or
 * fails: that span covers a page's chunk, and the shared pages' first read is
 * counted by their own `<Suspense>` (`SharedPage`).
 */
export default defineNuxtPlugin(() => {
  const router = useRouter()
  const navigation = loadSpan()
  router.beforeEach(() => navigation.begin())
  router.afterEach(() => navigation.end())
  router.onError(() => navigation.end())
})
