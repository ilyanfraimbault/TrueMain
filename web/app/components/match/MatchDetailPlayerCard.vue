<script setup lang="ts">
import type { MatchDetailParticipant } from '~~/shared/types/match-detail'
import type { ChampionStaticListItem, StaticItemData } from '~~/shared/types/static-data'

const props = defineProps<{
  participant: MatchDetailParticipant
  champions: ChampionStaticListItem[]
  items: Record<number, StaticItemData>
}>()

const champ = computed(() =>
  props.champions.find(c => c.championId === props.participant.championId) ?? null,
)
const champName = computed(() => champ.value?.name ?? `Champion ${props.participant.championId}`)

function fmtTime(ms: number) {
  const total = Math.round(ms / 1000)
  const m = Math.floor(total / 60)
  const s = total % 60
  return `${m}:${s.toString().padStart(2, '0')}`
}

// Build order: render every item event in order. Purchases are the spine;
// sells / undos / destroys are dimmed so the timeline still reads as "what was
// bought" without hiding the corrections.
const SKILL_LETTERS: Record<number, string> = { 1: 'Q', 2: 'W', 3: 'E', 4: 'R' }

function skillLetter(slot: number) {
  return SKILL_LETTERS[slot] ?? '?'
}

function eventLabel(type: string) {
  switch (type) {
    case 'ITEM_PURCHASED': return 'Bought'
    case 'ITEM_SOLD': return 'Sold'
    case 'ITEM_DESTROYED': return 'Used'
    case 'ITEM_UNDO': return 'Undo'
    default: return type
  }
}

function eventTint(type: string) {
  return type === 'ITEM_PURCHASED' ? 'opacity-100' : 'opacity-50'
}

// Riot sets itemId = 0 on an ITEM_UNDO and carries the affected item in
// beforeId / afterId. Fall back to those so undo steps render the real item
// instead of a blank square.
function eventItemId(ev: { itemId: number, beforeId: number | null, afterId: number | null }) {
  return ev.itemId || ev.beforeId || ev.afterId || 0
}

// Skill order grid: one column per level (max 18), one row per slot Q/W/E/R.
// A cell is filled at the level the slot was leveled. SkillEvents are already
// in chronological (level) order from the API.
const skillRows = computed(() => {
  const events = props.participant.skillEvents
  const maxLevel = events.length
  const slots = [1, 2, 3, 4]
  return slots.map(slot => ({
    slot,
    letter: skillLetter(slot),
    levels: Array.from({ length: maxLevel }, (_, i) => events[i]?.skillSlot === slot),
  }))
})

const skillLevelCount = computed(() => props.participant.skillEvents.length)

const laning = computed(() => props.participant.laning15)

function diffClass(value: number) {
  if (value > 0) return 'text-sky-400'
  if (value < 0) return 'text-red-400'
  return 'text-muted'
}

function fmtDiff(value: number) {
  return value > 0 ? `+${value}` : `${value}`
}
</script>

<template>
  <section class="glass rounded-md border border-default/60 bg-elevated/40 p-3">
    <!-- Header: champion + identity + per-minute stats -->
    <header class="mb-3 flex items-center gap-2">
      <ChampionLink
        :champion-id="participant.championId"
        :name="champName"
        :icon-url="champ?.iconUrl"
        class="block size-9 overflow-hidden rounded"
      />
      <div class="min-w-0">
        <p class="truncate text-xs font-semibold text-default">
          {{ participant.gameName ?? participant.summonerName }}
        </p>
        <p class="text-[10px] text-muted">
          {{ participant.teamPosition || '—' }} · {{ champName }}
        </p>
      </div>
      <span
        class="ml-auto rounded px-1.5 py-0.5 text-[10px] font-semibold"
        :class="participant.win ? 'bg-sky-500/15 text-sky-400' : 'bg-red-500/15 text-red-400'"
      >
        {{ participant.win ? 'Win' : 'Loss' }}
      </span>
    </header>

    <!-- Global per-minute stats -->
    <div class="mb-3 grid grid-cols-4 gap-2 text-center">
      <div>
        <p class="text-xs font-semibold tabular-nums text-default">{{ participant.csPerMin.toFixed(1) }}</p>
        <p class="text-[10px] text-muted">CS/min</p>
      </div>
      <div>
        <p class="text-xs font-semibold tabular-nums text-default">{{ participant.goldPerMin.toFixed(0) }}</p>
        <p class="text-[10px] text-muted">Gold/min</p>
      </div>
      <div>
        <p class="text-xs font-semibold tabular-nums text-default">{{ participant.damagePerMin.toFixed(0) }}</p>
        <p class="text-[10px] text-muted">DMG/min</p>
      </div>
      <div>
        <p class="text-xs font-semibold tabular-nums text-default">{{ participant.visionPerMin.toFixed(2) }}</p>
        <p class="text-[10px] text-muted">VS/min</p>
      </div>
    </div>

    <!-- Laning @15 + first to level 2 -->
    <div v-if="laning" class="mb-3 rounded bg-default/30 px-2 py-1.5">
      <p class="mb-1 text-[10px] font-semibold uppercase tracking-wide text-muted">
        Laning @15
        <span
          v-if="participant.firstToLevelTwo !== null"
          class="ml-1 font-normal normal-case"
          :class="participant.firstToLevelTwo ? 'text-sky-400' : 'text-muted'"
        >
          · {{ participant.firstToLevelTwo ? 'First to level 2' : 'Lost level 2' }}
        </span>
      </p>
      <div class="flex gap-4 text-[11px] tabular-nums">
        <span>CS <span :class="diffClass(laning.csDiff)">{{ fmtDiff(laning.csDiff) }}</span></span>
        <span>Gold <span :class="diffClass(laning.goldDiff)">{{ fmtDiff(laning.goldDiff) }}</span></span>
        <span>XP <span :class="diffClass(laning.xpDiff)">{{ fmtDiff(laning.xpDiff) }}</span></span>
      </div>
    </div>

    <!-- Build order -->
    <div class="mb-3">
      <p class="mb-1 text-[10px] font-semibold uppercase tracking-wide text-muted">Build order</p>
      <div v-if="participant.itemEvents.length" class="flex flex-wrap items-end gap-1.5">
        <div
          v-for="(ev, idx) in participant.itemEvents"
          :key="`ev-${idx}`"
          class="flex flex-col items-center gap-0.5"
          :class="eventTint(ev.eventType)"
          :title="`${eventLabel(ev.eventType)} @ ${fmtTime(ev.timestampMs)}`"
        >
          <GameTooltipItemIcon
            :item="eventItemId(ev) ? items[eventItemId(ev)] ?? null : null"
            :width="24"
            :height="24"
            class="size-6 rounded"
            :class="ev.eventType === 'ITEM_UNDO' || ev.eventType === 'ITEM_SOLD' ? 'ring-1 ring-red-500/40' : ''"
          />
          <span class="text-[9px] tabular-nums text-muted">{{ fmtTime(ev.timestampMs) }}</span>
        </div>
      </div>
      <p v-else class="text-[11px] text-muted">No build data</p>
    </div>

    <!-- Skill order -->
    <div>
      <p class="mb-1 text-[10px] font-semibold uppercase tracking-wide text-muted">Skill order</p>
      <div v-if="skillLevelCount" class="space-y-0.5">
        <div
          v-for="row in skillRows"
          :key="`skill-${row.slot}`"
          class="flex items-center gap-0.5"
        >
          <span
            class="flex size-4 shrink-0 items-center justify-center rounded bg-default/50 text-[9px] font-bold text-default"
          >
            {{ row.letter }}
          </span>
          <span
            v-for="(filled, lvl) in row.levels"
            :key="`lvl-${lvl}`"
            class="flex size-4 items-center justify-center rounded text-[9px] font-semibold tabular-nums"
            :class="filled
              ? (row.slot === 4 ? 'bg-amber-500/80 text-black' : 'bg-primary/70 text-white')
              : 'bg-default/20 text-transparent'"
          >
            {{ filled ? lvl + 1 : '' }}
          </span>
        </div>
      </div>
      <p v-else class="text-[11px] text-muted">No skill data</p>
    </div>
  </section>
</template>
