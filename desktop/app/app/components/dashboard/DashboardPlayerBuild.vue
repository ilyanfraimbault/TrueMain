<!--
  One player of an opened game, compact: the site's Details tab
  (MatchDetailPanel's player selector + MatchDetailPlayerPanel) — the lane at
  fifteen minutes, the per-minute figures, the build order and the skill order,
  by the site's rules — on the row's surface, in small type and small icons.
  Opens on the player; the ten portraits switch to anyone else.
-->
<script setup lang="ts">
import type { MatchDetailItemEvent, MatchDetailParticipant } from '#shared/types/match-detail'
import type { ChampionStaticListItem, StaticItemData } from '#shared/types/static-data'
import { isBuildOrderEvent, resolveEventItemId } from '#shared/utils/build'
import { getPositionIconUrl } from '#shared/utils/ddragon'

const props = defineProps<{
  participants: MatchDetailParticipant[]
  selfId: number | null
  selfTeamId: number
  champions: ChampionStaticListItem[]
  items: Record<number, StaticItemData>
}>()

const championById = computed(() => new Map(props.champions.map(c => [c.championId, c])))
const sides = computed(() => {
  const other = props.selfTeamId === 100 ? 200 : 100
  return [props.selfTeamId, other].map(team => props.participants.filter(p => p.teamId === team))
})

const selectedId = ref<number | null>(props.selfId)
const player = computed(() => props.participants.find(p => p.participantId === selectedId.value) ?? props.participants[0] ?? null)

const signed = (value: number) => (value > 0 ? `+${value}` : `${value}`)
const tone = (value: number) => (value > 0 ? 'text-data-good' : value < 0 ? 'text-data-bad' : 'text-muted')

const laning = computed(() => {
  const lane = player.value?.laning15
  if (!lane) return []
  return [
    { label: 'CS @15', value: lane.csDiff },
    { label: 'Gold @15', value: lane.goldDiff },
    { label: 'XP @15', value: lane.xpDiff },
  ]
})
const rates = computed(() => {
  const p = player.value
  if (!p) return []
  return [
    { label: 'CS/m', value: p.csPerMin.toFixed(1) },
    { label: 'Gold/m', value: p.goldPerMin.toFixed(0) },
    { label: 'DMG/m', value: p.damagePerMin.toFixed(0) },
    { label: 'Vision/m', value: p.visionPerMin.toFixed(2) },
  ]
})

// Shopping trips, as the site groups them: events of the same minute together,
// minute 0 the starting purchase; auto-granted transforms left out.
const trips = computed(() => {
  const groups: { minute: number, events: MatchDetailItemEvent[] }[] = []
  for (const event of player.value?.itemEvents ?? []) {
    if (!isBuildOrderEvent(event, props.items)) continue
    const minute = Math.floor(event.timestampMs / 60_000)
    const last = groups.at(-1)
    if (last?.minute === minute) last.events.push(event)
    else groups.push({ minute, events: [event] })
  }
  return groups
})

const { data: championStatic, status } = useChampionStatic(() => player.value?.championId ?? 0, () => null)
const KEYS = ['Q', 'W', 'E', 'R'] as const
const skillRows = computed(() => {
  const events = player.value?.skillEvents ?? []
  return KEYS.map((key, index) => ({
    key,
    spell: championStatic.value?.championSpells[key] ?? null,
    levels: events.map(event => event.skillSlot === index + 1),
  }))
})
</script>

<template>
  <div v-if="player" class="flex flex-col gap-3">
    <div class="flex items-center justify-center gap-3">
      <template v-for="(side, index) in sides" :key="index">
        <span v-if="index === 1" class="text-[11px] font-semibold text-dimmed">vs</span>
        <div class="flex gap-1">
          <button
            v-for="p in side"
            :key="p.participantId"
            type="button"
            class="relative rounded transition-opacity"
            :class="p.participantId === player.participantId ? 'ring-2 ring-primary' : 'opacity-50 hover:opacity-100'"
            :title="championById.get(p.championId)?.name"
            @click="selectedId = p.participantId"
          >
            <SkeletonImage :src="championById.get(p.championId)?.iconUrl ?? null" :width="28" :height="28" class="size-7 rounded" />
            <img v-if="p.teamPosition" :src="getPositionIconUrl(p.teamPosition)" alt="" class="absolute -bottom-1 -left-1 size-3.5 rounded-full bg-default p-px">
          </button>
        </div>
      </template>
    </div>

    <div class="flex flex-wrap items-baseline gap-x-5 gap-y-1 border-y border-default/50 py-2">
      <span class="text-xs font-semibold text-highlighted">{{ player.gameName ?? player.summonerName }}</span>
      <span v-for="stat in laning" :key="stat.label" class="text-[11px] text-dimmed">
        <span class="font-semibold tabular-nums" :class="tone(stat.value)">{{ signed(stat.value) }}</span> {{ stat.label }}
      </span>
      <span v-if="player.firstToLevelTwo !== null" class="text-[11px] text-dimmed">
        <span class="font-semibold" :class="player.firstToLevelTwo ? 'text-data-good' : 'text-muted'">{{ player.firstToLevelTwo ? 'First' : 'Second' }}</span> to lvl 2
      </span>
      <span v-for="stat in rates" :key="stat.label" class="text-[11px] text-dimmed">
        <span class="font-semibold tabular-nums text-default">{{ stat.value }}</span> {{ stat.label }}
      </span>
    </div>

    <div>
      <p class="stat-label mb-1.5">Build order</p>
      <div v-if="trips.length" class="flex flex-wrap items-start gap-x-1 gap-y-2">
        <template v-for="(trip, index) in trips" :key="index">
          <div class="flex flex-col items-center gap-0.5">
            <div class="flex gap-px">
              <GameTooltipItemIcon
                v-for="(event, e) in trip.events"
                :key="e"
                :item="items[resolveEventItemId(event)] ?? null"
                :width="22"
                :height="22"
                class="rounded-sm"
                :class="event.eventType === 'ITEM_SOLD' || event.eventType === 'ITEM_UNDO' ? 'opacity-40' : ''"
              />
            </div>
            <span class="text-[9px] tabular-nums text-dimmed">{{ trip.minute === 0 ? 'Start' : `${trip.minute}m` }}</span>
          </div>
          <UIcon v-if="index < trips.length - 1" name="i-lucide-chevron-right" class="mt-1 size-3 text-dimmed" />
        </template>
      </div>
      <p v-else class="text-[11px] text-muted">No build order for this game.</p>
    </div>

    <div>
      <p class="stat-label mb-1.5">Skill order</p>
      <div v-if="player.skillEvents.length" class="overflow-x-auto">
        <div class="mx-auto flex w-fit flex-col gap-0.5">
        <div v-for="row in skillRows" :key="row.key" class="flex items-center gap-0.5">
          <div class="relative mr-1.5 flex size-5 shrink-0">
            <GameTooltipChampionSpellIcon
              :spell="row.spell"
              :fallback-label="row.key"
              :pending="status === 'pending'"
              :width="20"
              :height="20"
              class="size-5 rounded-sm"
            />
            <ItemRankBadge :value="row.key" />
          </div>
          <span
            v-for="(filled, level) in row.levels"
            :key="level"
            class="flex size-4 shrink-0 items-center justify-center rounded-sm text-[9px] font-bold tabular-nums"
            :class="filled ? (row.key === 'R' ? 'bg-gold text-ink-950' : 'bg-primary/80 text-ink-950') : 'bg-white/5 text-transparent'"
          >{{ filled ? level + 1 : '' }}</span>
        </div>
        </div>
      </div>
      <p v-else class="text-[11px] text-muted">No skill order for this game.</p>
    </div>
  </div>
</template>
