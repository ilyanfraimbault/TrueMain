<script setup lang="ts">
import type { ChampionBuildSummary } from '~~/shared/types/champion-build-summary'
import type {
  ChampionStaticData,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
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
 * paragraph. #1147 then gave each mark the same hover card the icon grid shows,
 * resolved client-side by `BuildSummaryMark` from the maps the page already has.
 *
 * Renders nothing at all when the summary carries no measurement — an empty
 * card saying "no data" is noise next to the page's own no-data states.
 */
const props = defineProps<{
  summary: ChampionBuildSummary | null
  /**
   * The page's static maps, for the hover cards (#1147). Optional and expected
   * to be absent on the server: the summary's own payload carries names and
   * icons only, and a card needs the whole record — so the marks render from
   * SSR and grow their tooltips when these land. `BuildSummaryMark` does the
   * lookups; passing the maps down whole avoids building a parallel array of
   * resolved entities per sentence.
   */
  itemsMap?: Record<number, StaticItemData> | null
  runeTree?: RuneTreeResponse | null
  summonersMap?: Record<number, StaticSummonerSpellData> | null
  championStatic?: ChampionStaticData | null
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
          <ChampionBuildSummaryMark
            v-else-if="token.kind === 'entity'"
            :token="token"
            :items-map="itemsMap"
            :perks="runeTree?.perks"
            :summoners-map="summonersMap"
            :champion-spells="championStatic?.championSpells"
          />
          <template v-else>{{ token.text }}</template>
        </template>
      </p>
    </div>
  </SectionCard>
</template>
