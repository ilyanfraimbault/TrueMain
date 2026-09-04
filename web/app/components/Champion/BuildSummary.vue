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
 * Collapsed by default since #1466, and moved to the foot of the sidebar. The
 * feedback was that it restates data the page already shows — true for a reader
 * who has the icon grid right there, and the reason it now opens on demand
 * rather than occupying the top of the column. `championBuildSentenceTokens`
 * dropped the three sentences that were pure icon repetition (runes, skill
 * order, summoners) in the same change; what is left are the claims the grid
 * does not make in words.
 *
 * A native `<details>`, deliberately, and not `UAccordion`: Reka's accordion
 * unmounts closed content, which would take the paragraph out of the server
 * HTML and undo the whole point of #1123. `<details>` keeps it in the DOM —
 * collapsed content is still indexed, unlike `sr-only`, which is cloaking.
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

// Names the <section> region through the heading that lives inside the summary.
const headingId = useId()

const heading = computed(() => {
  const name = props.summary?.championName
  if (!name) return null
  const lane = lanePhrase(props.summary?.position)
  return `How ${name} mains build ${name}${lane ? ` ${lane}` : ''}`
})

</script>

<template>
  <!-- UCard directly rather than SectionCard: the heading has to live inside the
       <summary> to be the thing you click, and SectionCard puts its title in a
       header row above the body. Dropping the header slot also drops the divider
       that would otherwise cut the card in two above a collapsed body. -->
  <UCard
    v-if="heading && sentences.length"
    as="section"
    :aria-labelledby="headingId"
    :ui="{ body: 'p-0 sm:p-0' }"
  >
    <details class="group">
      <summary
        class="flex cursor-pointer list-none items-center justify-between gap-3 rounded-xl
               p-3 sm:px-4 sm:py-3.5 hover:text-highlighted"
      >
        <!-- h2, not h3: this is a top-level section of the page, and the only
             heading that carries the champion's name next to the word "build".
             Inside the summary, so collapsing changes nothing in the outline. -->
        <h2
          :id="headingId"
          class="text-sm font-medium text-default"
        >
          {{ heading }}
        </h2>
        <UIcon
          name="i-lucide-chevron-down"
          class="size-4 shrink-0 text-dimmed transition-transform group-open:rotate-180"
        />
      </summary>

      <div class="space-y-2.5 px-3 pb-3 sm:px-4 sm:pb-4">
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
    </details>
  </UCard>
</template>
