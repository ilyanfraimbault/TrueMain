import { isAnalyticsOptedOut } from '~/utils/analytics-opt-out'
import { clearVisitorId, getOrCreateVisitorId } from '~/utils/analytics-visitor-id'

/**
 * Umami analytics tracker (#728) — self-hosted, cookieless audience
 * measurement. Loaded only when the instance host and website id are
 * configured (see `runtimeConfig.public.umami` in nuxt.config.ts), so
 * unconfigured environments never load any tracker.
 *
 * Kept inside the CNIL's consent exemption for audience measurement (#2089),
 * which is why there is no consent banner:
 * - A visitor is recognised across months by a random id stored on the device
 *   for 13 months at most, never extended (#2149, `utils/analytics-visitor-id`).
 *   Without storage, Umami falls back to its hash of site + IP + user agent + a
 *   monthly salt.
 * - No session replay or heatmaps: Umami's `recorder.js` used to load here
 *   (#1011) and was removed, since following one visitor's navigation is
 *   exactly what the exemption excludes.
 * - A visitor who opted out on /privacy never gets the tracker loaded nor an
 *   id; the tracker itself also checks the same flag before every send.
 *
 * The id rides on every send through the tracker's `before-send` hook rather
 * than `umami.identify()`: both set the payload `id` Umami derives its session
 * from, but `identify` called once the tracker is up lets the automatic first
 * page view go out without it — a second, IP-hashed session on every page load
 * — and sends an extra `identify` event.
 *
 * Client-only on purpose: the tracker observes browser navigation and has no
 * SSR role, and loading it client-side keeps it out of the server-rendered
 * HTML of environments that disable it.
 *
 * Loaded through Nuxt Scripts (#1622) with the registry entry
 * (`useScriptUmamiAnalytics`), pointed at our instance: its `proxy` queues a
 * `umami.track(…)` made before the script has loaded instead of dropping it,
 * which is what makes custom events possible at all.
 *
 * `useScript` tags the script `crossorigin="anonymous"`, so the browser runs it
 * only if the instance answers with CORS headers. Umami serves `script.js` with
 * `Access-Control-Allow-Origin: *`; an instance behind a proxy that strips it
 * would silently stop tracking.
 *
 * Trigger: `onNuxtReady` — after hydration, when the browser is idle. The
 * tracker renders nothing and reads the current URL when it starts, then
 * follows client-side navigation. The cost is a page view lost when a visitor
 * leaves before the page ever goes idle.
 */
export default defineNuxtPlugin(() => {
  const { host, websiteId } = useRuntimeConfig().public.umami
  if (!host || !websiteId) {
    return
  }
  if (isAnalyticsOptedOut()) {
    clearVisitorId()
    return
  }

  const origin = host.replace(/\/+$/, '')
  const visitorId = getOrCreateVisitorId()

  useScriptUmamiAnalytics({
    websiteId,
    scriptInput: { src: `${origin}/script.js` },
    ...(visitorId && {
      beforeSend: (_type: string, payload: Record<string, unknown>) => ({ ...payload, id: visitorId }),
    }),
    // `bundle: false` is load-bearing. The registry bundles its entry by default:
    // the build downloads *cloud.umami.is*'s tracker (it cannot read our `src`,
    // which is only known at runtime) and serves that copy from `/_scripts/`.
    // The tracker posts to the origin it was loaded from, which would then be
    // this site instead of our instance — and the build would need the network.
    scriptOptions: { trigger: 'onNuxtReady', bundle: false },
  })
})
