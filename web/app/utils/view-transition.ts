/**
 * Which navigations may run a view transition (#1621).
 *
 * The browser freezes the outgoing frame from the start of a transition until
 * Nuxt resolves the new page, and drops the animation after 4 s. A page that
 * awaits data in its setup therefore turns the transition into a frozen screen:
 * observed cold on the champion page, which awaits its build summary — 4 s of
 * a motionless directory, then no animation at all. Without a transition the
 * old page also stays on screen during that wait, but it stays *live* (hover
 * states, pulsing skeletons), which is the better failure.
 *
 * So the rule is about the *destination*: leaving such a page still animates.
 */

/** Route paths whose page awaits data in `setup` and must not be animated into. */
const NO_TRANSITION_PATHS = [
  /^\/champions\/(?!tierlist$)[^/]+$/,
]

interface RouteLike {
  path: string
}

/** Whether arriving on `to` may run a view transition. */
export function allowsViewTransition(to: RouteLike): boolean {
  return !NO_TRANSITION_PATHS.some(pattern => pattern.test(to.path))
}
