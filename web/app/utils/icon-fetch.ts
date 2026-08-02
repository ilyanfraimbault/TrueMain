/**
 * The one size every icon is fetched at, whatever size it is displayed at.
 *
 * Data Dragon ships item icons at 64×64; Community Dragon perk icons are
 * larger but downscale cleanly. Funneling every request through this single
 * size is what makes one asset share one cache entry — browser and server —
 * no matter how many places render it: the champion portrait in the search
 * palette (20 px), a matchup row (32 px) and a build path slot (36 px) all
 * resolve to the same `/_ipx/f_webp&s_64x64/…` URL. Display size comes from
 * the caller's CSS, never from the fetch.
 *
 * Getting this wrong is not hypothetical: before #1000 the position glyphs
 * were fetched at 12, 20, 22 *and* 64 px by four different call sites — four
 * downloads and four cache entries for one image.
 */
export const ICON_FETCH_SIZE = 64
