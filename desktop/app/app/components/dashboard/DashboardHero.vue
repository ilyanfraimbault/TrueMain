<script setup lang="ts">
import type { PlayerRecord } from '~/types/record'
import type { MetricReading } from '~/utils/player-form'
import { platformIdToRegion } from '~~/shared/utils/region'

/**
 * The player's banner: the skin they chose as their profile background in the
 * client, under who they are — in the site's profile-header vocabulary, name,
 * region flag and level — and their recent form, one tile per metric.
 */
const props = defineProps<{
  record: PlayerRecord | null
  readings: MetricReading[]
  /** Counted games in the sample the readings come from. */
  sample: number
  pending: boolean
}>()

const { state } = useLcuState()
const { profileIconOf } = useChampionStatics()

const name = computed(() => state.value.riotId?.split('#')[0] ?? null)
const tag = computed(() => state.value.riotId?.split('#')[1] ?? null)
const icon = computed(() => (state.value.profileIconId !== null ? profileIconOf(state.value.profileIconId) : null))
const region = computed(() => platformIdToRegion(props.record?.platformId))

/** Without a chosen background the client shows the most-mastered champion; so does the app. */
const skinId = computed(() => props.record?.backgroundSkinId
  ?? (state.value.championPool[0] ? state.value.championPool[0] * 1000 : null))
</script>

<template>
  <section class="relative overflow-hidden rounded-lg border border-default bg-ink-950">
    <ChampionArt :skin-id="skinId" :alias="skinId ? null : backdropAlias()" fade="none" position="68% 22%" />
    <div class="pointer-events-none absolute inset-0 bg-linear-to-r from-ink-950/85 via-ink-950/30 to-transparent" />
    <div class="pointer-events-none absolute inset-0 bg-linear-to-t from-ink-950 via-ink-950/40 to-transparent" />

    <div class="relative flex flex-col gap-5 p-4 pt-5">
      <div class="flex items-center gap-4">
        <SkeletonImage :src="icon" :alt="`${name ?? 'Player'} profile icon`" class="size-[72px] shrink-0 rounded-lg shadow-lg ring-1 ring-white/10" />
        <div class="min-w-0 [text-shadow:0_1px_12px_rgb(0_0_0/0.6)]">
          <h2 class="truncate text-[28px] font-semibold leading-tight text-highlighted">
            {{ name ?? 'Logging in…' }}<span v-if="tag" class="font-medium text-muted">#{{ tag }}</span>
          </h2>
          <div class="mt-1 flex items-center gap-2 text-sm text-muted">
            <LeaderboardRegionFlag v-if="region" :region="region" :width="18" />
            <span v-if="state.summonerLevel !== null">Level {{ state.summonerLevel }}</span>
          </div>
        </div>
      </div>

      <div v-if="pending && !record" class="grid grid-cols-4 gap-2">
        <USkeleton v-for="index in 8" :key="index" class="h-[86px] rounded-lg bg-ink-950/55" />
      </div>
      <div v-else-if="sample" class="grid grid-cols-4 gap-2">
        <DashboardKpiTile v-for="reading in readings" :key="reading.metric.key" :reading="reading" :sample="sample" />
      </div>
      <UEmpty
        v-else
        icon="i-lucide-chart-spline"
        title="No games to read yet"
        description="Your form builds up from your next games."
        variant="naked"
        class="rounded-lg bg-ink-950/55 py-6 backdrop-blur-md"
      />
    </div>
  </section>
</template>
