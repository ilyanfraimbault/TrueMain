<script setup lang="ts">
import type { BuildSummaryTone, ChampionBuildSummary } from '~~/shared/types/champion-build-summary'
import { championBuildSentenceTokens, lanePhrase } from '~~/shared/utils/champion-build-summary'

/**
 * The champion page's build, in words (#1123), typeset (#1143).
 *
 * This is the **only** build content in the server-rendered HTML: every panel
 * around it fetches `server: false` (the #149 hydration fix), so before JS runs
 * the page was ~1.5 kB of chrome under a title promising "Ahri Build". A title
 * that promises a build over a page that delivers a shell is thin content, and
 * it is why the champion pages could not rank for their own subject.
 *
 * Visible, never sr-only. Hiding indexable text behind `sr-only` is cloaking,
 * and it would also waste the honest half of this: a reader landing cold on a
 * champion wants the one-paragraph version before the icon grid.
 *
 * #1143 gave that paragraph relief. Shipped flat and monochrome, it was a wall
 * of grey naming twenty entities a player normally reads as *pictures* — so
 * every named entity now carries its icon inline and a tone, and every
 * measurement is set as a measurement. The words are unchanged: the tokens
 * concatenate to exactly the sentences `championBuildSentences` returns, which
 * is what keeps the decorated paragraph and the indexable one the same
 * paragraph.
 *
 * Renders nothing at all when the summary carries no measurement — an empty
 * card saying "no data" is noise next to the page's own no-data states.
 */
const props = defineProps<{
  summary: ChampionBuildSummary | null
}>()

const sentences = computed(() =>
  props.summary ? championBuildSentenceTokens(props.summary) : [],
)

const heading = computed(() => {
  const name = props.summary?.championName
  if (!name) return null
  const lane = lanePhrase(props.summary?.position)
  return `How ${name} mains build ${name}${lane ? ` ${lane}` : ''}`
})

/**
 * Tone → utility. Written out rather than built as `text-rune-${tone}` so
 * Tailwind can see every class it has to generate, and so the mapping from a
 * *semantic* tone to the design system's vocabulary lives in the view instead
 * of in the shared model — see `main.css` for why the five rune tones exist.
 *
 * Summoner spells, abilities and the pinned opponent share `text-highlighted`
 * on purpose: they have no colour of their own in Riot's vocabulary, and the
 * icon beside them already says which kind of thing they are. Inventing three
 * more hues to fill the table would be exactly the rainbow the palette avoids.
 */
const TONE_CLASS: Readonly<Record<BuildSummaryTone, string>> = {
  item: 'text-stat-gold',
  spell: 'text-highlighted',
  ability: 'text-highlighted',
  champion: 'text-highlighted',
  precision: 'text-rune-precision',
  domination: 'text-rune-domination',
  sorcery: 'text-rune-sorcery',
  inspiration: 'text-rune-inspiration',
  resolve: 'text-rune-resolve',
  rune: 'text-highlighted',
}

/** Runes and portraits are round artwork; items, spells and abilities are square. */
const ROUND_TONES: ReadonlySet<BuildSummaryTone> = new Set<BuildSummaryTone>([
  'precision',
  'domination',
  'sorcery',
  'inspiration',
  'resolve',
  'rune',
  'champion',
])

// Plain <img> through the canonical IPX URL rather than `SkeletonImage`: these
// icons sit *inside* sentences, where a pulsing placeholder box mid-line reads
// as a broken word, and there are a dozen of them per paragraph. Same 64×64
// WebP fetch as every other icon on the site, so they share its cache entries.
const canonicalIcon = useCanonicalIcon()
</script>

<template>
  <!-- h2, not h3: this is a top-level section of the page, and the only heading
       that carries the champion's name next to the word "build". -->
  <SectionCard
    v-if="heading && sentences.length"
    :level="2"
    :title="heading"
    :ui="{ header: 'p-2 sm:px-2.5 sm:py-3', body: 'p-2 sm:p-2.5' }"
  >
    <div class="space-y-2.5">
      <p
        v-for="(sentence, index) in sentences"
        :key="index"
        class="text-sm leading-relaxed text-muted"
      >
        <template
          v-for="(token, tokenIndex) in sentence"
          :key="tokenIndex"
        >
          <strong
            v-if="token.kind === 'value'"
            class="font-semibold tabular-nums text-highlighted"
          >{{ token.text }}</strong>
          <!-- The name and its icon never separate across a line break: an
               orphaned icon at the end of a line reads as a bullet. -->
          <span
            v-else-if="token.kind === 'entity'"
            class="whitespace-nowrap font-medium"
            :class="TONE_CLASS[token.tone]"
          ><img
            v-if="token.iconUrl"
            :src="canonicalIcon(token.iconUrl)"
            alt=""
            aria-hidden="true"
            :width="16"
            :height="16"
            loading="lazy"
            class="mr-1 inline-block size-4 align-[-0.2em]"
            :class="ROUND_TONES.has(token.tone) ? 'rounded-full' : 'rounded-xs'"
          >{{ token.text }}</span>
          <template v-else>{{ token.text }}</template>
        </template>
      </p>
    </div>
  </SectionCard>
</template>
