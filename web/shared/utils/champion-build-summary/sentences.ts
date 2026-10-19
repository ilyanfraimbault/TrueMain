import type {
  BuildSummaryEntityToken,
  BuildSummarySentence,
  BuildSummarySource,
  BuildSummaryToken,
  BuildSummaryTone,
  ChampionBuildSummary,
  SummaryEntity,
} from '../../types/champion-build-summary'
import { formatCount } from '../counts'
import { formatPercentage } from '../ddragon'

/**
 * How each lane is said in a sentence. Deliberately not `POSITION_OPTIONS`'s
 * labels (`Middle`, `Support`): those are picker labels, and "wins 50.6% of
 * their games in the Support" is not English. Riot's `UTILITY` never surfaces.
 */
const LANE_PHRASE: Readonly<Record<string, string>> = {
  TOP: 'in the top lane',
  JUNGLE: 'in the jungle',
  MIDDLE: 'in the mid lane',
  BOTTOM: 'in the bot lane',
  UTILITY: 'as support',
}

/**
 * The lane as it reads mid-sentence, or `''` for an unknown/absent one so the
 * caller can drop the clause. Shared with the heading, which has to agree with
 * the paragraph under it.
 */
export function lanePhrase(position: string | null | undefined): string {
  return position ? LANE_PHRASE[position] ?? '' : ''
}

/**
 * How each elo filter is said. `ALL` adds nothing to a sentence — the absence of
 * a rank qualifier already means "every rank" — so it maps to the empty string
 * and the caller drops the clause entirely.
 */
function bracketPhrase(eloBracket: string): string {
  if (!eloBracket || eloBracket === 'ALL') return ''
  const [tier, plus] = eloBracket.split('_')
  if (!tier) return ''
  const named = tier.charAt(0) + tier.slice(1).toLowerCase()
  return plus === 'PLUS' ? `${named} and above` : named
}

/**
 * Small counts as words, so a sentence never opens with a numeral and a
 * repeated item reads as a quantity. Only ever called with 2 or more (one of
 * anything is worded without a count), hence the sparse start. Past single
 * digits it falls back to the figure rather than growing into a
 * number-to-words implementation.
 */
const NUMBER_WORDS: Readonly<Record<number, string>> = {
  2: 'two',
  3: 'three',
  4: 'four',
  5: 'five',
  6: 'six',
  7: 'seven',
  8: 'eight',
  9: 'nine',
}

function spellSmallNumber(value: number): string {
  return NUMBER_WORDS[value] ?? String(value)
}

export function capitalise(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1)
}

/**
 * ─── The sentences, as tokens ───────────────────────────────────────────────
 *
 * The summary is the actual indexable text of the champion page, and since
 * #1143 it is also *typeset*: each named entity carries its icon and a tone, so
 * the paragraph is scannable the way the icon grid above it is.
 *
 * Tokens rather than a string the component parses back apart. A regex over
 * finished prose would have to find "Doran's Ring" inside a sentence that also
 * contains "Doran's Blade", and would break on the first item named after a
 * rune — the builder already knows exactly what each fragment *is*, so it says
 * so instead of hiding it in punctuation and making the view guess.
 *
 * `championBuildSentences` is the concatenation of these, which is what keeps
 * the sentence a crawler reads identical to the one a reader reads: there is
 * one builder, not a plain version and a decorated version that can drift.
 *
 * One rule survives from #1123 and still governs everything: a sentence is
 * emitted only when every figure in it is real. Nothing is padded to reach a
 * word count, because each sentence is a claim about a measurement, and a
 * generic filler sentence next to real numbers makes the real numbers read as
 * filler too.
 */

/** Connective prose. Every space in a sentence belongs to one of these. */
export function plain(text: string): BuildSummaryToken {
  return { kind: 'text', text }
}

/** A measurement — a count, a percentage, a patch, a rank scope. */
export function figure(text: string): BuildSummaryToken {
  return { kind: 'value', text }
}

function mark(
  entity: SummaryEntity,
  tone: BuildSummaryTone,
  source: BuildSummarySource,
  text: string = entity.name,
): BuildSummaryEntityToken {
  return { kind: 'entity', text, iconUrl: entity.iconUrl, tone, source, id: entity.id }
}

/**
 * `a`, `a and b`, `a, b and c` — as tokens, separators included.
 *
 * `lastSeparator` is the whole difference between a list of *names* (` and `)
 * and a list of *clauses* (`, and `): the clauses are long enough that the
 * unpunctuated version misreads, which is why they keep the Oxford comma.
 */
export function joinTokens(parts: BuildSummaryToken[][], lastSeparator: string): BuildSummaryToken[] {
  const tokens: BuildSummaryToken[] = []
  parts.forEach((part, index) => {
    if (index > 0) tokens.push(plain(index === parts.length - 1 ? lastSeparator : ', '))
    tokens.push(...part)
  })
  return tokens
}

/**
 * A list of entities, with runs of the same one collapsed into a count: a
 * starter set genuinely holds two potions as two entries, and "Health Potion and
 * Health Potion" is accurate but not English. The icon grid above is right to
 * repeat them; prose is not — and the collapsed token still carries the one
 * icon, so "two Health Potions" reads as a quantity rather than as two marks.
 *
 * Consecutive runs only, so the collapse can never reorder a list whose order is
 * the claim — the core path in particular, which never repeats an item anyway.
 */
function marks(
  entities: SummaryEntity[],
  tone: BuildSummaryTone,
  source: BuildSummarySource,
): BuildSummaryToken[] {
  const parts: BuildSummaryToken[][] = []
  for (let i = 0; i < entities.length;) {
    const entity = entities[i]!
    let count = 1
    while (entities[i + count]?.id === entity.id) count++
    parts.push([mark(entity, tone, source, count > 1 ? `${spellSmallNumber(count)} ${entity.name}s` : entity.name)])
    i += count
  }
  return joinTokens(parts, ' and ')
}

export function championBuildSentenceTokens(summary: ChampionBuildSummary): BuildSummarySentence[] {
  const name = summary.championName
  if (!name || summary.games === 0) return []

  const sentences: BuildSummarySentence[] = []
  const lane = lanePhrase(summary.position)
  const bracket = bracketPhrase(summary.eloBracket)

  const scope: BuildSummaryToken[] = [
    plain('Across '),
    figure(formatCount(summary.games)),
    plain(' ranked games'),
  ]
  if (summary.patch) scope.push(plain(' on patch '), figure(summary.patch))
  if (bracket) scope.push(plain(' in '), figure(bracket))
  scope.push(
    plain(`, ${name} mains win `),
    figure(formatPercentage(summary.winRate)),
    plain(` of their games${lane ? ` ${lane}` : ''}`),
  )
  if (summary.opponentName) {
    scope.push(plain(' against '), {
      kind: 'entity',
      text: summary.opponentName,
      iconUrl: summary.opponentIconUrl,
      tone: 'champion',
      source: 'champion',
      id: 'opponent',
    })
  }
  scope.push(plain('.'))
  sentences.push(scope)

  // Second, so it qualifies everything after it. The page flags a thin slice
  // with a warning icon; without this the *indexable* version of the page would
  // be the one that states the figures unqualified.
  if (!summary.minSampleMet) {
    sentences.push([plain(
      'That is below the sample TrueMain requires before it treats a build as settled, so read the figures here as indicative.',
    )])
  }

  const build = summary.build
  if (!build) return sentences

  // The build's own share and record, stated before its contents: the contents
  // are only worth reading once you know how much of the sample chose them.
  // Its own sentence rather than an aside inside the next one, which is what
  // the em dashes used to buy — they read as a stumble in a narrow column, and
  // splitting also means the share survives a build with no item data at all.
  sentences.push([
    plain('Their most common build appears in '),
    figure(formatPercentage(build.pickRate)),
    plain(' of those games and wins '),
    figure(formatPercentage(build.winRate)),
    plain(' of its '),
    figure(formatCount(build.games)),
    plain(build.games === 1 ? ' game.' : ' games.'),
  ])

  const itemClauses: BuildSummaryToken[][] = []
  if (build.starterItems.length) {
    itemClauses.push([plain('starts '), ...marks(build.starterItems, 'item', 'item')])
  }
  if (build.coreItems.length) {
    itemClauses.push([plain('completes '), ...marks(build.coreItems, 'item', 'item'), plain(' in that order')])
  }
  if (build.boots) itemClauses.push([plain('takes '), mark(build.boots, 'item', 'item')])
  if (itemClauses.length) {
    sentences.push([plain('It '), ...joinTokens(itemClauses, ', and '), plain('.')])
  }

  // Runes, skill order and summoner spells used to get a sentence each. They are
  // gone (#1466): every one of them named, in words, a row of icons sitting a few
  // hundred pixels away — the reader's own complaint, and the reason the whole
  // paragraph now opens on demand rather than leading the sidebar. What is left
  // is what the icon grid does not say: the scope of the sample, how much of it
  // this build is, and the item progression as an ordered claim.
  //
  // This is a deliberate trade against #1123: the paragraph is still the only
  // build content in the server HTML, so shortening it costs indexable text. It
  // keeps the substantive claims — champion, patch, bracket, lane, win rate,
  // build share, items — which is what the page's own title promises.

  const others = summary.buildCount - 1
  if (others > 0) {
    sentences.push([plain(
      others === 1
        ? `One other build is played often enough ${lane || 'on this lane'} to be measured on its own.`
        : `${capitalise(spellSmallNumber(others))} other builds are played often enough ${lane || 'on this lane'} to be measured on their own.`,
    )])
  }

  return sentences
}

/** One sentence's tokens, concatenated — what a crawler and a screen reader get. */
export function buildSummarySentenceText(sentence: BuildSummarySentence): string {
  return sentence.map(token => token.text).join('')
}

/**
 * The summary as plain sentences. Derived from the tokens rather than built
 * alongside them, so the decorated paragraph and its text can never disagree.
 */
export function championBuildSentences(summary: ChampionBuildSummary): string[] {
  return championBuildSentenceTokens(summary).map(buildSummarySentenceText)
}
