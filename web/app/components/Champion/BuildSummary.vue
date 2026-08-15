<script setup lang="ts">
import type { ChampionBuildSummary } from '~~/shared/types/champion-build-summary'
import { championBuildSentences, lanePhrase } from '~~/shared/utils/champion-build-summary'

/**
 * The champion page's build, in words (#1123).
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
 * Renders nothing at all when the summary carries no measurement — an empty
 * card saying "no data" is noise next to the page's own no-data states.
 */
const props = defineProps<{
  summary: ChampionBuildSummary | null
}>()

const sentences = computed(() => (props.summary ? championBuildSentences(props.summary) : []))

const heading = computed(() => {
  const name = props.summary?.championName
  if (!name) return null
  const lane = lanePhrase(props.summary?.position)
  return `How ${name} mains build ${name}${lane ? ` ${lane}` : ''}`
})
</script>

<template>
  <section
    v-if="heading && sentences.length"
    class="surface space-y-3 rounded-xl p-5 sm:p-6"
  >
    <!-- h2, not h3: this is a top-level section of the page, a sibling of the
         build tabs, and the only heading that carries the champion's name next
         to the word "build". -->
    <h2 class="text-base font-semibold text-highlighted">
      {{ heading }}
    </h2>
    <p
      v-for="(sentence, index) in sentences"
      :key="index"
      class="text-sm leading-relaxed text-muted"
    >
      {{ sentence }}
    </p>
  </section>
</template>
