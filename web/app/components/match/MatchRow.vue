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

// Items 0..5 + trinket grouped together but visually separated by a small
// gap before the trinket — matches the in-game inventory layout where the
// trinket sits in its own slot.
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
  return deaths === 0
    ? 'Perfect'
    : `${((kills + assists) / deaths).toFixed(1)} KDA`
})

const csTotal = computed(() => self.value.cs)
const csPerMin = computed(() => {
  const minutes = props.match.gameDurationSeconds / 60
  if (minutes <= 0) return '0.0'
  return (csTotal.value / minutes).toFixed(1)
})

const kpPercent = computed(() => `${Math.round(self.value.killParticipation * 100)}%`)

const resultLabel = computed(() => (self.value.win ? 'Victory' : 'Defeat'))

const lpDeltaText = computed(() => {
  const delta = self.value.lpDelta
  if (delta === null || delta === undefined) return null
  if (delta > 0) return `+${delta} LP`
  return `${delta} LP`
})
</script>

<template>
  <article
    class="group relative flex overflow-hidden rounded-lg bg-elevated/40 transition-colors hover:bg-elevated/70"
    :aria-label="`${resultLabel} as ${championName}, ${self.kills}/${self.deaths}/${self.assists}`"
  >
    <!--
      Result strip: 4px vertical band on the left edge. Emerald for wins,
      red for losses — the standard scoreboard cue. The body of the row
      keeps neutral elevated/40 backgrounds so the result colour is the
      only chromatic signal beyond the KDA/LP text.
    -->
    <div
      class="w-1 shrink-0"
      :class="self.win ? 'bg-emerald-500' : 'bg-red-500'"
      aria-hidden="true"
    />

    <div class="flex flex-1 items-center gap-3 px-3 py-2.5">
      <!-- Meta column: result, queue, LP delta, duration, timestamp -->
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

      <!-- Champion + level pill -->
      <div class="relative shrink-0">
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

      <!-- Summoner spells (stacked) -->
      <div class="flex shrink-0 flex-col gap-0.5">
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

      <!-- Runes (keystone bigger, secondary smaller) -->
      <div class="flex shrink-0 flex-col items-center gap-0.5">
        <GameTooltipPerkIcon
          :perk="keystone"
          :width="22"
          :height="22"
          class="size-[22px] rounded-full bg-black/50"
        />
        <GameTooltipPerkStyleIcon
          :style="subStyle"
          :width="18"
          :height="18"
          class="size-[18px]"
        />
      </div>

      <!-- KDA centerpiece -->
      <div class="flex min-w-[6rem] flex-col items-center">
        <div class="text-lg font-bold leading-tight tabular-nums">
          {{ self.kills }}
          <span class="text-muted">/</span>
          <span class="text-red-400">{{ self.deaths }}</span>
          <span class="text-muted">/</span>
          {{ self.assists }}
        </div>
        <div class="text-[11px] text-muted tabular-nums">
          {{ kdaRatio }}
        </div>
        <div class="text-[11px] text-muted tabular-nums">
          {{ csTotal }} CS · {{ csPerMin }}/min
        </div>
        <div class="text-[11px] text-muted tabular-nums">
          {{ kpPercent }} KP
        </div>
      </div>

      <!-- Items in one horizontal row of 6 + a small gap + trinket -->
      <div class="flex shrink-0 items-center gap-2">
        <div class="flex gap-1">
          <GameTooltipItemIcon
            v-for="(item, idx) in inventoryItems"
            :key="`item-${idx}`"
            :item="item"
            :width="24"
            :height="24"
            class="size-6 rounded bg-black/30"
          />
        </div>
        <GameTooltipItemIcon
          :item="trinket"
          :width="24"
          :height="24"
          class="size-6 rounded-full bg-black/30"
        />
      </div>

      <!-- MVP / ACE badge (right side, no VS thumbnails) -->
      <div class="ml-auto shrink-0">
        <span
          v-if="self.isMvp || self.isAce"
          class="inline-flex items-center rounded-full px-2 py-0.5 text-[11px] font-bold ring-1"
          :class="self.isMvp
            ? 'bg-emerald-500/20 text-emerald-300 ring-emerald-500/40'
            : 'bg-amber-500/20 text-amber-300 ring-amber-500/40'"
        >
          {{ self.isMvp ? 'MVP' : 'ACE' }}
        </span>
      </div>
    </div>
  </article>
</template>
