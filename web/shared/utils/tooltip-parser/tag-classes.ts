/**
 * Canonical tag → Tailwind class mapping. The lookup is the single source of
 * truth for how each DDragon / CDragon description tag (and a few synthetic
 * ones emitted by the parsers) is styled. Tags are matched case-insensitively
 * by the parsers — keys are lowercase.
 *
 * Unknown tags fall through to the `default` entry (plain `text-default`,
 * no emphasis). The renderer never throws on an unrecognised tag — text
 * always reaches the user, only the styling is missing. Add a new entry to
 * this table when a new tag surfaces in real patch data.
 */
export const TAG_CLASS: Record<string, string> = {
  // Structural wrappers (no class — children carry the styling; line breaks
  // come from <br> tags between stat rows, not from a block label).
  maintext: '',
  stats: '',

  // <attention> = numeric callout. The item parser retroactively retags each
  // `attention` segment into `attention<stat>` based on the following stat
  // label, so each value matches its stat color in-game. Bare `attention`
  // (no recognised label nearby) stays AD-orange like the in-game default.
  attention: 'text-stat-ad font-semibold',

  // Per-stat numeric callouts (emitted by the lookahead pass in
  // item-description.ts based on the stat label following the value).
  attentionad: 'text-stat-ad font-semibold',
  attentionlethality: 'text-stat-lethality font-semibold',
  attentionap: 'text-stat-ap font-semibold',
  attentionmagicpen: 'text-stat-magicpen font-semibold',
  attentionhealth: 'text-stat-health font-semibold',
  attentionhsp: 'text-stat-hsp font-semibold',
  attentionhealreduction: 'text-stat-heal-reduction font-semibold',
  attentionarmor: 'text-stat-armor font-semibold',
  attentionmr: 'text-stat-mr font-semibold',
  attentiontenacity: 'text-stat-tenacity font-semibold',
  attentionmana: 'text-stat-mana font-semibold',
  attentionhaste: 'text-stat-haste font-semibold',
  attentionspeed: 'text-stat-speed font-semibold',
  attentionas: 'text-stat-as font-semibold',
  attentioncrit: 'text-stat-crit font-semibold',
  attentionvamp: 'text-stat-vamp font-semibold',
  attentionshield: 'text-stat-shield font-semibold',
  attentiongold: 'text-stat-gold font-semibold',
  attentiontrue: 'text-stat-true font-semibold',

  // Item structural labels
  passive: 'text-stat-passive font-semibold uppercase tracking-wide text-xs',
  active: 'text-stat-active font-semibold uppercase tracking-wide text-xs',
  // Inline back-references to an earlier passive/active label (retagged by
  // the item parser). Subdued styling — just the color, no header
  // uppercase/tracking/text-xs — so the reference reads as prose, not a
  // new section header.
  passiveref: 'text-stat-passive font-semibold',
  activeref: 'text-stat-active font-semibold',
  rules: 'text-muted text-xs italic block mt-1',
  flavortext: 'text-muted italic block mt-1',
  raritymythic: 'text-stat-active font-semibold',
  raritylegendary: 'text-stat-active font-semibold',

  // Damage type keywords (used inline inside passive prose).
  // Magic damage tracks the MR cyan, NOT the AP violet — Riot colors damage
  // by the resist that blocks it (AD→armor-orange, magic→MR-cyan).
  physicaldamage: 'text-stat-ad',
  magicdamage: 'text-stat-mr',
  truedamage: 'text-stat-true',
  adaptivedamage: 'text-stat-adaptive',

  // Stat-scaling keywords (e.g. "scales with <scaleAP>0.4 AP</scaleAP>")
  scalead: 'text-stat-ad',
  scaleap: 'text-stat-ap',
  scalearmor: 'text-stat-armor',
  scalemr: 'text-stat-mr',
  scalehealth: 'text-stat-health',
  scalemana: 'text-stat-mana',

  // Sustain / healing keywords
  healing: 'text-stat-hsp',
  shield: 'text-stat-hsp',
  lifesteal: 'text-stat-vamp',

  // Misc inline emphasis
  speed: 'text-stat-speed',
  status: 'text-stat-status',
  // Inline crowd-control / game-term highlights (Slowing, Immobilizing, ...)
  keyword: 'text-stat-status',
  // Stealth-family keywords retagged by the item parser (Invisible, Camouflage, ...)
  keywordstealth: 'text-stat-stealth font-semibold',
  keywordmajor: 'text-default font-semibold',
  onhit: 'text-stat-speed',

  // Champion-ability slot key (synthetic — not emitted by DDragon)
  spellkey: 'text-stat-active font-bold',

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
