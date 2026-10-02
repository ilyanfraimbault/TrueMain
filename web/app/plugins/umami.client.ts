/**
 * Umami analytics tracker (#728) — self-hosted, cookieless visitor/session
 * tracking. Also loads Umami's session replay/heatmap recorder, whose
 * sampling rate, masking, and enablement are configured per-website in
 * Umami's own settings, not here. Both scripts are loaded only when the
 * instance host and website id are configured (see `runtimeConfig.public.umami`
 * in nuxt.config.ts), so unconfigured environments never load any tracker.
 *
 * Client-only on purpose: the tracker observes browser navigation and has no
 * SSR role, and loading it client-side keeps it out of the server-rendered
 * HTML of environments that disable it.
 *
 * Loaded through Nuxt Scripts (#1622). The tracker uses the registry entry
 * (`useScriptUmamiAnalytics`), pointed at our instance: its `proxy` queues a
 * `umami.track(…)` made before the script has loaded instead of dropping it,
 * which is what makes custom events possible at all. The recorder is not in the
 * registry, so it is a plain `useScript` tag with the same attributes as before.
 *
 * `useScript` tags both scripts `crossorigin="anonymous"`, so the browser runs
 * them only if the instance answers with CORS headers. Umami serves `script.js`
 * and `recorder.js` with `Access-Control-Allow-Origin: *`; an instance behind a
 * proxy that strips it would silently stop tracking.
 *
 * Trigger: `onNuxtReady` — after hydration, when the browser is idle. Both
 * scripts used to be injected while the app was still hydrating, competing with
 * its own chunks for nothing: neither renders anything, and the tracker reads
 * the current URL when it starts, then follows client-side navigation. The cost
 * is a page view lost when a visitor leaves before the page ever goes idle.
 */
export default defineNuxtPlugin(() => {
  const { host, websiteId } = useRuntimeConfig().public.umami
  if (!host || !websiteId) {
    return
  }

  const origin = host.replace(/\/+$/, '')
  const trigger = 'onNuxtReady'

  useScriptUmamiAnalytics({
    websiteId,
    scriptInput: { src: `${origin}/script.js` },
    // `bundle: false` is load-bearing. The registry bundles its entry by default:
    // the build downloads *cloud.umami.is*'s tracker (it cannot read our `src`,
    // which is only known at runtime) and serves that copy from `/_scripts/`.
    // The tracker posts to the origin it was loaded from, which would then be
    // this site instead of our instance — and the build would need the network.
    scriptOptions: { trigger, bundle: false },
  })

  useScript({
    'src': `${origin}/recorder.js`,
    'data-website-id': websiteId,
  }, { trigger })
})
