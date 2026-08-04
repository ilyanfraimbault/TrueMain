import { ICON_FETCH_SIZE } from '~/utils/icon-fetch'

/**
 * Builds the canonical `/_ipx/…` URL for an icon: fixed {@link ICON_FETCH_SIZE}
 * square, WebP.
 *
 * `SkeletonImage` is the usual way to render an icon and calls this itself.
 * Reach for the composable directly only where a component deliberately renders
 * a plain `<img>` instead — fixed-size glyphs that appear dozens of times per
 * page, where one component instance per icon measurably costs more than it
 * gives (see the profiling note in `SkeletonImage.vue`). Both paths must
 * produce the *same* URL, which is the whole point of routing them through one
 * function rather than hand-writing `ipx(...)` at each call site.
 *
 * Not for SVG sources: IPX serves them through untouched as `image/svg+xml`,
 * and asking for WebP would rasterise a vector that stays crisp at any DPR.
 * `RankIcon.vue` is the one component in that situation and builds its own URL.
 */
export function useCanonicalIcon() {
  const ipx = useImage()

  return (src: string | null | undefined): string | undefined =>
    src
      ? ipx(src, { width: ICON_FETCH_SIZE, height: ICON_FETCH_SIZE, format: 'webp' })
      : undefined
}
