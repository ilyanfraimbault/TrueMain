import { allowsViewTransition } from '~/utils/view-transition'

/**
 * Opts the pages that await data in `setup` out of the page transition (#1621).
 * Which pages, and why, live in `utils/view-transition.ts`.
 *
 * Nuxt's own transition plugin reads `to.meta.viewTransition` in a
 * `beforeResolve` guard, so setting it from `beforeEach` — which always runs
 * first — disables the transition for that navigation. This is the form Nuxt's
 * transitions guide uses for the same job; `definePageMeta` on the page itself
 * would be more direct, but `pages/champions/[slug].vue` is over the file-size
 * guardrail's limit and may only shrink.
 */
export default defineNuxtPlugin(() => {
  useRouter().beforeEach((to) => {
    if (!allowsViewTransition(to)) to.meta.viewTransition = false
  })
})
