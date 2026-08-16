<script setup lang="ts">
import type { TruemainDedication } from '~~/shared/types/dedication'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { dedicationComponents, dedicationTier, dedicationTierColor } from '~/utils/dedication'

// TrueMain's signature metric, on the player's signature champion. Every figure
// on this card — the score, the four components, the raw counts — comes from
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

const components = computed(() => dedicationComponents(props.dedication))

const tierLabel = computed(() => dedicationTier(props.dedication.score))

// Colours the score by its own tier so the card reads at a glance, no hover
// needed — the same rose-gold→iron scale `TierBadge` uses for S..D.
const tierColorClass = computed(() => dedicationTierColor(props.dedication.score))

// One decimal, matching what the backend ranks on — the leaderboard cell
// rounds harder because it has a fifth of the room.
const scoreLabel = computed(() => props.dedication.score.toFixed(1))
</script>

<template>
  <section class="flex flex-col gap-2">
    <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
      Dedication
    </h2>

    <div class="surface flex flex-col gap-3 rounded-lg p-3">
      <!-- Score + the champion it is about. The champion cell links to this
           player's own build page for it, which is the natural next click. -->
      <div class="flex items-center gap-3">
        <div class="flex flex-col">
          <div class="flex items-baseline gap-1.5">
            <span
              class="text-3xl font-bold leading-none tabular-nums"
              :class="tierColorClass"
            >
              {{ scoreLabel }}
            </span>
            <span
              class="text-xs font-semibold uppercase tracking-wide"
              :class="tierColorClass"
            >
              {{ tierLabel }}
            </span>
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

      <!-- The four components, so the score is readable rather than asserted.
           Each bar is the normalised component; the caption is the raw figure
           behind it. Shared with the leaderboard row's tooltip. -->
      <DedicationBreakdown :components="components" />

      <p class="text-[10px] leading-snug text-muted">
        Weighted from the share of games on the champion, the patches played,
        the tracked volume and how recently it was played.
      </p>
    </div>
  </section>
</template>
