<script setup lang="ts">
import type { MatchSummaryResponse } from '~~/shared/types/matches'
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticPerkData,
  StaticPerkStyleData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import { getQueueLabel } from '~/utils/queues'
import { formatDuration, formatRelativeTime } from '~/utils/relativeTime'

const props = defineProps<{
  match: MatchSummaryResponse
  champions: ChampionStaticListItem[]
  items: Record<number, StaticItemData>
  summonerSpells: Record<number, StaticSummonerSpellData>
  runeTree: RuneTreeResponse
}>()

const self = computed(() => props.match.self)

const championIconUrl = computed(() => {
  const champ = props.champions.find(c => c.championId === self.value.championId)
  return champ?.iconUrl ?? null
})

const championName = computed(() => {
  const champ = props.champions.find(c => c.championId === self.value.championId)
  return champ?.name ?? `Champion ${self.value.championId}`
})

const summoner1: ComputedRef<StaticSummonerSpellData | null> = computed(
  () => props.summonerSpells[self.value.summoner1Id] ?? null,
)
const summoner2: ComputedRef<StaticSummonerSpellData | null> = computed(
  () => props.summonerSpells[self.value.summoner2Id] ?? null,
)

const keystone: ComputedRef<StaticPerkData | null> = computed(() => {
  if (!self.value.keystoneId) return null
  return props.runeTree.perks[self.value.keystoneId] ?? null
})

const subStyle: ComputedRef<StaticPerkStyleData | null> = computed(() => {
  if (!self.value.subStyleId) return null
  return props.runeTree.perkStyles[self.value.subStyleId] ?? null
})

const inventoryItems = computed(() =>
  self.value.items.map(id => (id > 0 ? props.items[id] ?? null : null)),
)
const trinket = computed<StaticItemData | null>(() => {
  const id = self.value.trinketItemId
  return id > 0 ? props.items[id] ?? null : null
})

const queueLabel = computed(() => getQueueLabel(props.match.queueId))
const durationLabel = computed(() => formatDuration(props.match.gameDurationSeconds))
const relativeLabel = computed(() => formatRelativeTime(props.match.gameStartTimeUtc))

const kdaRatio = computed(() => {
  const { kills, deaths, assists } = self.value
  if (deaths === 0) return 'Perfect'
  return `${((kills + assists) / deaths).toFixed(2)} KDA`
})

const csPerMin = computed(() => {
  const minutes = props.match.gameDurationSeconds / 60
  if (minutes <= 0) return '0.0'
  return (self.value.cs / minutes).toFixed(1)
})

const kpPercent = computed(() => `${Math.round(self.value.killParticipation * 100)}%`)

const resultLabel = computed(() => (self.value.win ? 'Victory' : 'Defeat'))

const lpDeltaText = computed(() => {
  const delta = self.value.lpDelta
  if (delta === null || delta === undefined) return null
  return delta > 0 ? `+${delta} LP` : `${delta} LP`
})

// Row-level tint: subtle emerald for wins, subtle red for losses. The
// whole row picks up the colour rather than just the left strip, so the
// win/loss pattern is readable at a glance when scanning a feed of 20+
// matches. Numbers tuned low (8% / 7% alpha) so the row body still reads
// as "card" — anything higher and the colour starts to fight the content.
const rowTint = computed(() =>
  self.value.win
    ? 'bg-emerald-500/8 hover:bg-emerald-500/12'
    : 'bg-red-500/8 hover:bg-red-500/12',
)
</script>

<template>
  <article
    class="group relative flex overflow-hidden rounded-md transition-colors"
    :class="rowTint"
    :aria-label="`${resultLabel} as ${championName}, ${self.kills}/${self.deaths}/${self.assists}`"
  >
    <!-- Result strip: 4px vertical band on the left edge, slightly more
         saturated than the row tint so the eye still locks on the side
         even when scanning quickly. -->
    <div
      class="w-1 shrink-0"
      :class="self.win ? 'bg-emerald-500' : 'bg-red-500'"
      aria-hidden="true"
    />

    <div class="flex flex-1 items-center gap-3 px-3 py-2.5">
      <!-- Meta column: result + queue + LP + duration + timestamp -->
      <div class="flex w-[5.5rem] shrink-0 flex-col text-xs leading-tight">
        <div class="font-semibold" :class="self.win ? 'text-emerald-400' : 'text-red-400'">
          {{ resultLabel }}
        </div>
        <div class="text-muted">
          {{ queueLabel }}
        </div>
        <div
          v-if="lpDeltaText"
          class="font-semibold"
          :class="(self.lpDelta ?? 0) >= 0 ? 'text-emerald-400' : 'text-red-400'"
        >
          {{ lpDeltaText }}
        </div>
        <div class="text-muted">
          {{ durationLabel }}
        </div>
        <div class="text-muted">
          {{ relativeLabel }}
        </div>
      </div>

      <!-- Loadout cluster: champion + level pill, summoner spells, runes -->
      <div class="flex shrink-0 items-center gap-1.5">
        <div class="relative">
          <SkeletonImage
            :src="championIconUrl"
            :alt="championName"
            :title="championName"
            class="size-12 rounded"
          />
          <span
            class="absolute -bottom-1 -right-1 inline-flex size-4 items-center justify-center rounded-full bg-default text-[10px] font-bold leading-none ring-1 ring-default"
          >
            {{ self.championLevel }}
          </span>
        </div>
        <div class="flex flex-col gap-0.5">
          <GameTooltipSummonerSpellIcon
            :spell="summoner1"
            :width="22"
            :height="22"
            class="size-[22px] rounded"
          />
          <GameTooltipSummonerSpellIcon
            :spell="summoner2"
            :width="22"
            :height="22"
            class="size-[22px] rounded"
          />
        </div>
        <div class="flex flex-col items-center gap-0.5">
          <GameTooltipPerkIcon
            :perk="keystone"
            :width="22"
            :height="22"
            class="size-[22px] rounded-full bg-black/40"
          />
          <GameTooltipPerkStyleIcon
            :style="subStyle"
            :width="18"
            :height="18"
            class="size-[18px]"
          />
        </div>
      </div>

      <!-- KDA + stats: two-column block. KDA on top with the ratio under
           it; CS/m and KP stacked to the right so they share the vertical
           rhythm of the KDA cluster. Tabular-nums everywhere so columns
           visually align row-to-row. -->
      <div class="flex shrink-0 items-start gap-3">
        <div class="flex min-w-[5.5rem] flex-col items-center">
          <div class="text-lg font-bold leading-tight tabular-nums">
            {{ self.kills }}
            <span class="text-muted/70">/</span>
            <span class="text-red-400">{{ self.deaths }}</span>
            <span class="text-muted/70">/</span>
            {{ self.assists }}
          </div>
          <div class="text-[11px] font-semibold text-muted tabular-nums">
            {{ kdaRatio }}
          </div>
        </div>
        <div class="flex flex-col gap-0.5 text-[11px] text-muted tabular-nums">
          <span>{{ csPerMin }} CS/m</span>
          <span>{{ kpPercent }} KP</span>
        </div>
      </div>

      <!-- Items: single row of 6 inventory slots + a small gap + trinket.
           No background on the icons themselves — they sit on the row tint
           directly, the way scoreboards usually render inventory. -->
      <div class="flex shrink-0 items-center gap-1.5">
        <div class="flex gap-1">
          <GameTooltipItemIcon
            v-for="(item, idx) in inventoryItems"
            :key="`item-${idx}`"
            :item="item"
            :width="24"
            :height="24"
            class="size-6 rounded"
          />
        </div>
        <GameTooltipItemIcon
          :item="trinket"
          :width="24"
          :height="24"
          class="size-6 rounded-full"
        />
      </div>

      <!-- MVP / ACE badge anchored to the right -->
      <div class="ml-auto shrink-0">
        <span
          v-if="self.isMvp || self.isAce"
          class="inline-flex items-center rounded-full px-2 py-0.5 text-[11px] font-bold ring-1"
          :class="self.isMvp
            ? 'bg-emerald-500/25 text-emerald-200 ring-emerald-500/50'
            : 'bg-amber-500/25 text-amber-200 ring-amber-500/50'"
        >
          {{ self.isMvp ? 'MVP' : 'ACE' }}
        </span>
      </div>
    </div>
  </article>
</template>
