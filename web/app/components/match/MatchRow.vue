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

const summoner1IconUrl = computed(() => props.summonerSpells[self.value.summoner1Id]?.iconUrl ?? null)
const summoner1Name = computed(() => props.summonerSpells[self.value.summoner1Id]?.name ?? '')
const summoner2IconUrl = computed(() => props.summonerSpells[self.value.summoner2Id]?.iconUrl ?? null)
const summoner2Name = computed(() => props.summonerSpells[self.value.summoner2Id]?.name ?? '')

const keystoneIconUrl = computed<string | null>(() => {
  if (!self.value.keystoneId) return null
  const perk: StaticPerkData | undefined = props.runeTree.perks[self.value.keystoneId]
  return perk?.iconUrl ?? null
})

const subStyleIconUrl = computed<string | null>(() => {
  if (!self.value.subStyleId) return null
  const style: StaticPerkStyleData | undefined = props.runeTree.perkStyles[self.value.subStyleId]
  return style?.iconUrl ?? null
})

const itemIcons = computed(() =>
  self.value.items.map(id => ({
    id,
    iconUrl: id > 0 ? (props.items[id]?.iconUrl ?? null) : null,
    name: id > 0 ? (props.items[id]?.name ?? '') : '',
  })),
)

const trinketIcon = computed(() => {
  const id = self.value.trinketItemId
  return {
    id,
    iconUrl: id > 0 ? (props.items[id]?.iconUrl ?? null) : null,
    name: id > 0 ? (props.items[id]?.name ?? '') : '',
  }
})

function lookupChampionIcon(championId: number) {
  return props.champions.find(c => c.championId === championId)?.iconUrl ?? null
}
function lookupChampionName(championId: number) {
  return props.champions.find(c => c.championId === championId)?.name ?? `Champion ${championId}`
}

// 100 = blue side, 200 = red side.
const blueTeam = computed(() => props.match.participants.filter(p => p.teamId === 100))
const redTeam = computed(() => props.match.participants.filter(p => p.teamId === 200))

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
      Result strip: 4px vertical band on the left edge. Emerald for wins so
      the row leads with the project's accent color; red for losses keeps the
      contrast unambiguous without leaning on indigo/violet (per the
      project's emerald-only palette rule).
    -->
    <div
      class="w-1 shrink-0"
      :class="self.win ? 'bg-emerald-500' : 'bg-red-500'"
      aria-hidden="true"
    />

    <div class="flex flex-1 flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3">
      <!-- Meta column: queue + LP + duration + timestamp -->
      <div class="flex w-24 shrink-0 flex-col text-xs leading-tight">
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

      <!-- Loadout: champion + spells + keystone -->
      <div class="flex shrink-0 items-center gap-2">
        <div class="relative">
          <SkeletonImage
            :src="championIconUrl"
            :alt="championName"
            :title="championName"
            class="size-14 rounded"
          />
          <span
            class="absolute -bottom-1 -right-1 inline-flex size-5 items-center justify-center rounded-full bg-default text-[10px] font-bold leading-none ring-1 ring-default"
          >
            {{ self.championLevel }}
          </span>
        </div>
        <div class="flex flex-col gap-1">
          <SkeletonImage
            :src="summoner1IconUrl"
            :alt="summoner1Name"
            :title="summoner1Name"
            class="size-6 rounded"
          />
          <SkeletonImage
            :src="summoner2IconUrl"
            :alt="summoner2Name"
            :title="summoner2Name"
            class="size-6 rounded"
          />
        </div>
        <div class="flex flex-col gap-1">
          <SkeletonImage
            :src="keystoneIconUrl"
            alt="Keystone"
            title="Keystone"
            class="size-6 rounded-full bg-black/40"
          />
          <SkeletonImage
            :src="subStyleIconUrl"
            alt="Secondary tree"
            title="Secondary tree"
            class="size-6"
          />
        </div>
      </div>

      <!-- KDA centerpiece -->
      <div class="flex min-w-[7rem] flex-col items-center">
        <div class="text-lg font-bold leading-tight">
          {{ self.kills }} <span class="text-muted">/</span>
          <span class="text-red-400">{{ self.deaths }}</span>
          <span class="text-muted">/</span> {{ self.assists }}
        </div>
        <div class="text-xs text-muted">
          {{ kdaRatio }}
        </div>
        <div class="text-xs text-muted">
          {{ csTotal }} CS ({{ csPerMin }}/min)
        </div>
        <div class="text-xs text-muted">
          {{ kpPercent }} KP
        </div>
      </div>

      <!-- Items grid: 6 inventory slots + trinket -->
      <div class="grid shrink-0 grid-cols-4 gap-1">
        <SkeletonImage
          v-for="(item, idx) in itemIcons"
          :key="`item-${idx}`"
          :src="item.iconUrl"
          :alt="item.name"
          :title="item.name"
          class="size-7 rounded"
        />
        <SkeletonImage
          :src="trinketIcon.iconUrl"
          :alt="trinketIcon.name"
          :title="trinketIcon.name"
          class="size-7 rounded"
        />
      </div>

      <!-- Versus thumbnails: blue team column, red team column -->
      <div class="ml-auto grid grid-cols-2 gap-x-2">
        <div class="flex flex-col gap-0.5">
          <div
            v-for="(participant, idx) in blueTeam"
            :key="`blue-${idx}`"
            class="flex items-center gap-1"
          >
            <SkeletonImage
              :src="lookupChampionIcon(participant.championId)"
              :alt="lookupChampionName(participant.championId)"
              :title="lookupChampionName(participant.championId)"
              class="size-4 rounded-sm"
            />
            <span class="max-w-[6rem] truncate text-[10px] text-muted">
              {{ participant.gameName ?? '—' }}
            </span>
          </div>
        </div>
        <div class="flex flex-col gap-0.5">
          <div
            v-for="(participant, idx) in redTeam"
            :key="`red-${idx}`"
            class="flex items-center gap-1"
          >
            <SkeletonImage
              :src="lookupChampionIcon(participant.championId)"
              :alt="lookupChampionName(participant.championId)"
              :title="lookupChampionName(participant.championId)"
              class="size-4 rounded-sm"
            />
            <span class="max-w-[6rem] truncate text-[10px] text-muted">
              {{ participant.gameName ?? '—' }}
            </span>
          </div>
        </div>
      </div>

      <!-- MVP / ACE badge -->
      <div v-if="self.isMvp || self.isAce" class="shrink-0">
        <span
          class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-bold ring-1"
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
