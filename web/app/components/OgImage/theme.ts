/**
 * Shared visual language for the Satori share cards (#926).
 *
 * Satori resolves neither CSS custom properties nor Tailwind utilities, so the
 * two things the cards cannot borrow from the app are the `@theme` colours in
 * `app/assets/css/main.css` and anything expressed as a class. Those are
 * restated below as literal hex; when a brand colour moves in `main.css`, it
 * moves here too.
 *
 * Everything that is *already* a plain value or a pure function — `TIER_HEX`,
 * `formatTier`, `eloBracketLabel`, the position labels — is imported from where
 * the pages get it, not copied, so a rank can never read one colour on the page
 * and another on the card.
 *
 * The templates style exclusively through inline `style` objects. That is
 * deliberate: Satori supports a subset of CSS, and going through Tailwind would
 * make the card's output depend on how the module transpiles utilities rather
 * than on what we wrote.
 */

export { formatTier as formatRank, TIER_HEX as RANK_COLOR } from '~/utils/tiers'
export { eloBracketLabel as formatEloBracket } from '~/utils/elo-brackets'
export { formatCount } from '~~/shared/utils/counts'

import { POSITION_BY_VALUE } from '~/utils/positions'

/** `--color-ink-950`, the app's darkest surface — the card ground. */
export const INK = '#0b0b0d'
/** The `--ui-bg-elevated` step: panel fills inside the card. */
export const PANEL = '#1b1b20'
/** `--color-ink-800` — the quiet border around non-accented panels. */
export const PANEL_BORDER = '#26262c'
/** `--color-ink-200` — primary text on the dark ground. */
export const TEXT = '#d9d9dd'
/** `--color-ink-400` — labels and secondary text. */
export const TEXT_MUTED = '#8b8b95'
/** `--color-ink-500` — the dimmest legible step (footer, units). */
export const TEXT_DIM = '#6a6a74'
/** `--color-rosegold-400`, the brand accent as it reads on dark. */
export const ROSEGOLD = '#e58f83'
/** `--color-rosegold-300`, the lighter end of the wordmark ramp. */
export const ROSEGOLD_LIGHT = '#eeaea3'
/**
 * `--color-gold` at its on-dark hairline opacity. Spelled `rgba()` rather than
 * an 8-digit hex because Satori's colour parsing is only reliable on the former.
 */
export const GOLD_HAIRLINE = 'rgba(217, 182, 118, 0.22)'

/**
 * The `--color-tier-*` performance ladder — the medal scale, rose gold at S
 * down to iron at D. This one has no hex counterpart in `app/utils` —
 * `TierBadge.vue` reaches for it through `bg-tier-*` classes — so it is
 * restated here rather than imported, and **has to be updated by hand whenever
 * main.css moves**: Satori cannot resolve a CSS variable, which is why these
 * are literals in the first place. An unknown or missing tier is deliberately
 * absent from the map: the card drops the badge, mirroring the badge's own
 * dash-instead-of-a-guess behaviour.
 */
export const TIER_COLOR: Record<string, string> = {
  S: '#e58f83',
  A: '#e2b95c',
  B: '#b9c2cd',
  C: '#b3763f',
  D: '#837a7c',
}

/**
 * The rose-gold "eclipse" the site paints behind the home hero
 * (`AppBackdrop.vue` draws it in WebGL). Two offset radial washes are as close
 * as Satori's gradient support gets, and they carry the same warm off-centre
 * glow. The card keeps it whatever page it was shared from: a share card is an
 * ad for the site, so it wears the brand's signature rather than the sober
 * treatment the data pages get.
 */
export const BACKDROP = [
  'radial-gradient(900px 620px at 78% -12%, rgba(229,143,131,0.30), rgba(229,143,131,0) 70%)',
  'radial-gradient(700px 520px at 8% 108%, rgba(217,182,118,0.14), rgba(217,182,118,0) 70%)',
].join(', ')

/** Riot position value → the label the app's role pickers show. */
export function formatPosition(position: string): string {
  return POSITION_BY_VALUE.get(position.toUpperCase())?.label ?? position
}
