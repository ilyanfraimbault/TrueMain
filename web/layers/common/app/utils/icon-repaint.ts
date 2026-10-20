import type { InjectionKey, Ref } from 'vue'

/**
 * Repainting an icon that `GameTooltip/LazyTooltip.vue` re-creates on its first hover.
 *
 * Arming the tooltip moves the icon under `UTooltip`'s trigger, and Vue cannot move a
 * subtree between two parents: the icon's `<img>` is destroyed and a new one created.
 * Its picture is already in the browser's memory cache, but a new `<img>` still starts
 * from nothing — the loading skeleton and the fade-in on the component side, and an
 * asynchronous decode on the engine side (Gecko and WebKit paint it a frame or more
 * late). Together they made the icon under the pointer blink out and back the first
 * time it was hovered.
 *
 * So an icon that knows it is being re-created by that hover, and whose source this
 * document has already painted, comes back drawn: loaded from its first render, no
 * fade, decoded synchronously. Both conditions are needed. The tooltip's signal alone
 * would draw an icon hovered while still loading as an empty box instead of a
 * skeleton; the painted set alone would also apply during hydration, where the first
 * render must match the server's, which never shows a picture as loaded.
 */

/** Provided by `LazyTooltip`: true once the tooltip is armed, i.e. its slot is being re-created. */
export const ICON_REMOUNT_KEY: InjectionKey<Readonly<Ref<boolean>>> = Symbol('icon-remount')

// Sources an `<img>` of this document has drawn. Only ever filled client-side (from
// `load` or `onMounted`), so a server render never sees an entry. Bounded by the
// game's icon set — a few hundred items, runes, spells and champions.
const paintedSources = new Set<string>()

export function markIconPainted(src: string | null | undefined) {
  if (src) paintedSources.add(src)
}

/** Whether an icon being created now can be drawn at once instead of loading again. */
export function isIconRepaint(remounting: boolean, src: string | null | undefined): boolean {
  return remounting && Boolean(src) && paintedSources.has(src!)
}

/** Test seam: the set is module state, shared by every test of a run. */
export function resetPaintedIcons() {
  paintedSources.clear()
}
