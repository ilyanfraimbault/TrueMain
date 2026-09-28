<script setup lang="ts">
import type { TruemainDedication } from '~~/shared/types/dedication'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { TRUEMAIN_SCORE_LABEL, dedicationParts, formatDedicationLastPlayed, formatDedicationScore } from '~/utils/dedication'

// TrueMain's signature metric — the Truemain score (#1701) — on the player's
// signature champion. Every figure on this card — the score, the verdict, the
// parts and the facts behind them — comes from
// `GET /truemains/{nameTag}/profile`; nothing is derived or estimated here, so
// the card can't disagree with the leaderboard column.
const props = defineProps<{
  dedication: TruemainDedication
  champions: ChampionStaticListItem[]
  /** Profile slug ({gameName}-{tagLine}); drives the player-scoped champion link. */
  nameTag: string
}>()

const champion = computed(() =>
  props.champions.find(c => c.championId === props.dedication.championId) ?? null)

const championName = computed(() =>
  champion.value?.name ?? `Champion ${props.dedication.championId}`)

const { truemainPathFor } = useChampionSlugs()

const championHref = computed(() =>
  truemainPathFor(props.nameTag, props.dedication.championId))

const parts = computed(() => dedicationParts(props.dedication))

const lastPlayed = computed(() => formatDedicationLastPlayed(props.dedication.daysSinceLastPlayed))

// Whole number, like the leaderboard: the parts below are whole points and
// must add up to it.
const scoreLabel = computed(() => formatDedicationScore(props.dedication.score))
</script>

<template>
  <section class="flex flex-col gap-2">
    <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
      {{ TRUEMAIN_SCORE_LABEL }}
    </h2>

    <div class="surface flex flex-col gap-3 rounded-lg p-3">
      <!-- Score + the champion it is about. The champion cell links to this
           player's own build page for it, which is the natural next click. -->
      <div class="flex items-center gap-3">
        <div class="flex flex-col">
          <div class="flex items-center gap-2">
            <span class="text-3xl font-bold leading-none tabular-nums text-default">
              {{ scoreLabel }}
            </span>
            <DedicationVerdict :dedication="dedication" />
          </div>
          <span class="mt-1 text-[10px] uppercase tracking-wide text-muted">
            out of 100
          </span>
        </div>

        <NuxtLink
          :to="championHref"
          class="surface-hover ml-auto flex min-w-0 items-center gap-2 rounded-md px-2 py-1 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        >
          <SkeletonImage
            :src="champion?.iconUrl ?? null"
            :alt="championName"
            class="size-8 shrink-0 rounded"
          />
          <span class="truncate text-sm font-medium">{{ championName }}</span>
        </NuxtLink>
      </div>

      <!-- The parts, so the score is readable rather than asserted: each line
           is the raw fact and the points it added. Shared with the leaderboard
           row's tooltip. -->
      <DedicationBreakdown :parts="parts" />

      <p v-if="lastPlayed" class="text-[10px] leading-snug text-muted">
        {{ lastPlayed }}
      </p>
    </div>
  </section>
</template>
