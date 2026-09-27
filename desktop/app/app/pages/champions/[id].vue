<script setup lang="ts">
import type { Lane } from '~/types/draft'
import { LANES, LANE_LABELS } from '~/types/draft'

/**
 * One champion: its splash, its numbers on a lane, and its builds on that lane
 * in the same view the draft opens after a pick. The lanes offered are the
 * ones the site tiers it on, most played first.
 */
const route = useRoute()
const router = useRouter()

const championId = computed(() => Number(route.params.id))
const { nameOf, aliasOf } = useChampionStatics()
const { entries, entryOf } = useTierList()

/** The lanes the champion is tiered on, most played first. */
const lanes = computed<Lane[]>(() => entries.value
  .filter(entry => entry.championId === championId.value)
  .sort((a, b) => b.games - a.games)
  .map(entry => entry.position as Lane)
  .filter(lane => LANES.includes(lane)))

const lane = computed<Lane | null>({
  get: () => {
    const asked = String(route.query.lane ?? '').toUpperCase() as Lane
    return LANES.includes(asked) ? asked : lanes.value[0] ?? null
  },
  set: value => void router.replace({ query: { ...route.query, lane: value ?? undefined } }),
})

const entry = computed(() => entryOf(championId.value, lane.value))
const percent = (value: number) => `${(value * 100).toFixed(1)}%`
</script>

<template>
  <div class="flex h-full flex-col">
    <section class="relative shrink-0 overflow-hidden border-b border-default">
      <ChampionArt :champion-id="championId" fade="x" position="70% 22%" />
      <div class="relative flex items-end gap-5 px-6 pb-4 pt-8">
        <ChampionPortrait :champion-id="championId" size="lg" class="size-16! rounded-xl! ring-2! ring-primary/50!" />
        <div class="min-w-0">
          <h1 class="truncate text-3xl font-semibold tracking-tight text-highlighted">{{ nameOf(championId) }}</h1>
          <div class="mt-2 flex items-center gap-3">
            <LaneTabs v-if="lanes.length" v-model="lane" :lanes="lanes" labels />
            <span v-else class="text-sm text-dimmed">Not tiered on any lane this patch</span>
          </div>
        </div>

        <div v-if="entry" class="ml-auto flex items-end gap-6 rounded-xl border border-default bg-ink-950/75 px-4 py-2.5 backdrop-blur">
          <TierMark :tier="entry.tier" />
          <div class="flex flex-col gap-1">
            <span class="stat-label">Win rate</span>
            <span class="text-lg font-semibold tabular-nums" :class="winRateTone(entry.winRate)">{{ percent(entry.winRate) }}</span>
          </div>
          <div class="flex flex-col gap-1">
            <span class="stat-label">Pick rate</span>
            <span class="stat-value text-lg">{{ percent(entry.pickRate) }}</span>
          </div>
          <div class="flex flex-col gap-1">
            <span class="stat-label">Ban rate</span>
            <span class="stat-value text-lg">{{ percent(entry.banRate) }}</span>
          </div>
          <div class="flex flex-col gap-1">
            <span class="stat-label">Games</span>
            <span class="stat-value text-lg">{{ entry.games.toLocaleString('en-US') }}</span>
          </div>
        </div>
      </div>
    </section>

    <div class="min-h-0 flex-1 p-4">
      <BuildView v-if="lane" :champion-id="championId" :position="lane">
        <template #header>
          <div class="flex items-center justify-between gap-2">
            <span class="text-sm font-semibold text-highlighted">{{ LANE_LABELS[lane] }}</span>
            <div class="flex items-center gap-0.5">
              <UButton
                :to="`/matchup?champion=${championId}&lane=${lane}`"
                icon="i-lucide-wand-sparkles"
                color="neutral"
                variant="ghost"
                size="xs"
                aria-label="Matchup"
                title="Against a champion"
              />
              <UButton
                v-if="aliasOf(championId)"
                icon="i-lucide-external-link"
                color="neutral"
                variant="ghost"
                size="xs"
                aria-label="Open on truemain.lol"
                title="Open on truemain.lol"
                @click="openOnSite(`/champions/${aliasOf(championId)!.toLowerCase()}`)"
              />
            </div>
          </div>
        </template>
      </BuildView>
    </div>
  </div>
</template>
