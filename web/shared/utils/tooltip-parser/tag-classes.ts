/**
 * Canonical tag → Tailwind class mapping. The lookup is the single source of
 * truth for how each DDragon / CDragon description tag (and a few synthetic
 * ones emitted by the parsers) is styled. Tags are matched case-insensitively
 * by the parsers — keys are lowercase.
 *
 * Unknown tags fall through to the `default` entry (no extra emphasis class);
 * the renderer logs once per session in dev so we can grow this list as new
 * tags surface in real patch data.
 */
export const TAG_CLASS: Record<string, string> = {
  // Structural wrappers (no class — children carry the styling; line breaks
  // come from <br> tags between stat rows, not from a block label).
  maintext: '',
  stats: '',

  // Item / spell semantic tags
  // <attention> = numeric callout. The item parser retroactively retags each
  // `attention` segment into `attention<stat>` based on the following stat
  // label, so each value matches its stat color in-game. Bare `attention`
  // (no recognised label nearby) stays AD-orange like the in-game default.
  attention: 'text-stat-ad font-semibold',
  attentionad: 'text-stat-ad font-semibold',
  attentionap: 'text-stat-ap font-semibold',
  attentionhealth: 'text-stat-health font-semibold',
  attentionarmor: 'text-stat-armor font-semibold',
  attentionmr: 'text-stat-mr font-semibold',
  attentionmana: 'text-stat-mana font-semibold',
  attentionspeed: 'text-stat-speed font-semibold',
  attentioncrit: 'text-stat-crit font-semibold',
  attentionshield: 'text-stat-shield font-semibold',
  passive: 'text-stat-passive font-semibold uppercase tracking-wide text-xs',
  active: 'text-stat-active font-semibold uppercase tracking-wide text-xs',
  rules: 'text-muted text-xs italic block mt-1',
  flavortext: 'text-muted italic block mt-1',
  raritymythic: 'text-stat-active font-semibold',
  raritylegendary: 'text-stat-active font-semibold',

  // Damage / scaling
  physicaldamage: 'text-stat-ad',
  magicdamage: 'text-stat-ap',
  truedamage: 'text-stat-true',
  scalead: 'text-stat-ad',
  scaleap: 'text-stat-ap',
  scalearmor: 'text-stat-armor',
  scalemr: 'text-stat-mr',
  scalehealth: 'text-stat-health',
  scalemana: 'text-stat-mana',
  healing: 'text-stat-health',
  shield: 'text-stat-shield',
  lifesteal: 'text-stat-health',
  speed: 'text-stat-speed',
  status: 'text-stat-status',
  keywordstealth: 'text-stat-status',
  keywordmajor: 'text-default font-semibold',
  onhit: 'text-stat-speed',

  // Champion-ability slot key (synthetic — not emitted by DDragon)
  spellkey: 'text-stat-default font-bold',

  // Rune synthetic tags (emitted by rune parser only)
  runekeyword: 'text-stat-adaptive font-semibold',
  runeadaptive: 'text-stat-adaptive',
  runead: 'text-stat-ad',
  runeap: 'text-stat-ap',
  runehealth: 'text-stat-health',
  runearmor: 'text-stat-armor',
  runespeed: 'text-stat-speed',
  runemr: 'text-stat-mr',

  // Fallthrough
  default: 'text-default',
}

/**
 * Look up the class string for a tag name (case-insensitive). Returns the
 * default-class entry when the tag isn't in the table — parsers should still
 * emit the segment so the text reaches the user.
 */
export function classForTag(tag: string): string {
  const normalized = tag.toLowerCase()
  return TAG_CLASS[normalized] ?? TAG_CLASS.default ?? ''
}

/**
 * Whether the parser has a known mapping for this tag. Used by the renderer
 * to surface unknown tags in dev console.
 */
export function isKnownTag(tag: string): boolean {
  return Object.prototype.hasOwnProperty.call(TAG_CLASS, tag.toLowerCase())
}
