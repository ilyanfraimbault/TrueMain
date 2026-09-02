/**
 * The classes the placeholder behind an icon wears, per state.
 *
 * Loading and failed used to differ only by `animate-pulse`, which made a page
 * whose icons had all failed indistinguishable from one still loading — exactly
 * how the 1.20.0 `/_ipx` outage looked: every icon on the site dead, and the page
 * merely appearing slow. They are now different *shapes*: loading is solid and
 * moving, failed is hollow and still.
 *
 * Extracted from the component so the rule is pinned by a test rather than by
 * reading a template, and so the loading fill can be asserted to match
 * `ui.skeleton.base` in `app.config.ts` — this is the most numerous skeleton on
 * the site, so a drift there is what "the page is loading" looks like everywhere.
 */

/** The fill Nuxt UI's own skeletons use, set on `ui.skeleton.base`. */
export const SKELETON_FILL = 'bg-ink-700'

export function iconPlaceholderClass(failed: boolean): string {
  return failed
    // Hollow and still: a 404 is a final state, and it should read as an empty
    // slot rather than as a slot that is still filling.
    ? 'bg-ink-800 ring-1 ring-inset ring-ink-700'
    : `${SKELETON_FILL} animate-pulse`
}
