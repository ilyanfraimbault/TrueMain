import { focusMainContent, shouldMoveFocus } from '~/utils/route-focus'

/**
 * Moves keyboard focus to the main region once a path-changing client-side
 * navigation has rendered its page (#1616). The rules for *which* navigations
 * qualify live in `utils/route-focus.ts`.
 *
 * The focus waits for `page:finish` rather than running in `afterEach`: the
 * guard fires before the new page is mounted, and focusing then would park the
 * keyboard in front of the outgoing page's content. `preventScroll` leaves the
 * scroll position to the router's own scroll behaviour.
 */
export default defineNuxtPlugin((nuxtApp) => {
  let pending = false

  useRouter().afterEach((to, from, failure) => {
    pending = !failure && shouldMoveFocus(to, from)
  })

  nuxtApp.hook('page:finish', () => {
    if (!pending) return
    pending = false
    focusMainContent({ preventScroll: true })
  })
})
