<script setup lang="ts">
import type { MatchDetailItemEvent, MatchDetailParticipant } from '~~/shared/types/match-detail'
import type { ChampionStaticListItem, StaticItemData } from '~~/shared/types/static-data'
import { isBuildOrderEvent, resolveEventItemId } from '~~/shared/utils/build'

/**
 * Full-width detail view for a single selected participant (the Details tab).
 * Lays out laning @15 + global per-minute stats on top, then the full build
 * order and skill order — mirroring the OP.GG-style single-player breakdown.
 * Ward counts and control wards are intentionally absent: the detail payload
 * doesn't carry them yet (tracked separately).
 */
const props = defineProps<{
  participant: MatchDetailParticipant
  champions: ChampionStaticListItem[]
  items: Record<number, StaticItemData>
}>()

const champ = computed(() =>
  props.champions.find(c => c.championId === props.participant.championId) ?? null,
)
const champName = computed(() => champ.value?.name ?? `Champion ${props.participant.championId}`)

const laning = computed(() => props.participant.laning15)

function diffClass(value: number) {
  if (value > 0) return 'text-sky-400'
  if (value < 0) return 'text-red-400'
  return 'text-muted'
}
function fmtDiff(value: number) {
  return value > 0 ? `+${value}` : `${value}`
}

function fmtTime(ms: number) {
  const total = Math.round(ms / 1000)
  const m = Math.floor(total / 60)
  const s = total % 60
  return `${m}:${s.toString().padStart(2, '0')}`
}

// Only a sold item or an undo is a genuine "correction" worth dimming; the
// build order drops ITEM_DESTROYED entirely (see isBuildOrderEvent), so it
// never reaches the template.
function isCorrection(type: string) {
  return type === 'ITEM_UNDO' || type === 'ITEM_SOLD'
}

// Build order grouped into shopping trips: consecutive events sharing the same
// game-minute collapse into one cluster with a single time label, the way the
// inspiration lays it out. Minute 0 is the starting purchase ("Starter").
// isBuildOrderEvent strips auto-granted transforms (quest stages, empowered-
// recall boots) and their paired ITEM_DESTROYED so they don't clutter it.
interface BuildGroup {
  minute: number
  label: string
  events: MatchDetailItemEvent[]
}
const buildGroups = computed<BuildGroup[]>(() => {
  const groups: BuildGroup[] = []
  for (const ev of props.participant.itemEvents) {
    if (!isBuildOrderEvent(ev, props.items)) continue
    const minute = Math.floor(ev.timestampMs / 60000)
    const last = groups[groups.length - 1]
    if (last && last.minute === minute) {
      last.events.push(ev)
    }
    else {
      groups.push({
        minute,
        label: minute === 0 ? 'Starter' : `${minute} min`,
        events: [ev],
      })
    }
  }
  return groups
})

// Skill order grid — one column per level (chronological), one row per slot.
// Palette-safe per-slot colours (sky / amber / emerald / rose); no violet.
const SKILL_META: Record<number, { letter: string, fill: string }> = {
  1: { letter: 'Q', fill: 'bg-sky-500 text-white' },
  2: { letter: 'W', fill: 'bg-amber-500 text-black' },
  3: { letter: 'E', fill: 'bg-emerald-500 text-black' },
  4: { letter: 'R', fill: 'bg-rose-500 text-white' },
}
const skillRows = computed(() => {
  const events = props.participant.skillEvents
  const maxLevel = events.length
  return [1, 2, 3, 4].map(slot => ({
    slot,
    letter: SKILL_META[slot]!.letter,
    fill: SKILL_META[slot]!.fill,
    levels: Array.from({ length: maxLevel }, (_, i) => events[i]?.skillSlot === slot),
  }))
})
const hasSkills = computed(() => props.participant.skillEvents.length > 0)
</script>

<template>
  <div class="flex flex-col gap-3">
    <!-- Laning @15 + global per-minute stats -->
    <div class="grid gap-3 sm:grid-cols-2">
      <div class="glass rounded-md border border-default/60 bg-elevated/60 p-3">
        <p class="mb-2 flex items-center gap-1.5 text-[10px] font-semibold uppercase tracking-wide text-muted">
          <UIcon name="i-lucide-swords" class="size-3.5 text-primary" />
          Laning phase (at 15)
        </p>
        <div v-if="laning" class="grid grid-cols-4 gap-2 text-center">
          <div>
            <p class="text-sm font-bold tabular-nums" :class="diffClass(laning.csDiff)">{{ fmtDiff(laning.csDiff) }}</p>
            <p class="text-[10px] text-muted">cs diff</p>
          </div>
          <div>
            <p class="text-sm font-bold tabular-nums" :class="diffClass(laning.goldDiff)">{{ fmtDiff(laning.goldDiff) }}</p>
            <p class="text-[10px] text-muted">gold diff</p>
          </div>
          <div>
            <p class="text-sm font-bold tabular-nums" :class="diffClass(laning.xpDiff)">{{ fmtDiff(laning.xpDiff) }}</p>
            <p class="text-[10px] text-muted">xp diff</p>
          </div>
          <div>
            <p
              class="text-sm font-bold"
              :class="participant.firstToLevelTwo ? 'text-sky-400' : 'text-muted'"
            >
              {{ participant.firstToLevelTwo === null ? '—' : participant.firstToLevelTwo ? 'Yes' : 'No' }}
            </p>
            <p class="text-[10px] text-muted">first lvl 2</p>
          </div>
        </div>
        <p v-else class="text-[11px] text-muted">No laning data for this game.</p>
      </div>

      <div class="glass rounded-md border border-default/60 bg-elevated/60 p-3">
        <p class="mb-2 flex items-center gap-1.5 text-[10px] font-semibold uppercase tracking-wide text-muted">
          <UIcon name="i-lucide-activity" class="size-3.5 text-primary" />
          Global stats
        </p>
        <div class="grid grid-cols-4 gap-2 text-center">
          <div>
            <p class="text-sm font-bold tabular-nums text-default">{{ participant.csPerMin.toFixed(1) }}</p>
            <p class="text-[10px] text-muted">CS/m</p>
          </div>
          <div>
            <p class="text-sm font-bold tabular-nums text-default">{{ participant.visionPerMin.toFixed(1) }}</p>
            <p class="text-[10px] text-muted">VS/m</p>
          </div>
          <div>
            <p class="text-sm font-bold tabular-nums text-default">{{ participant.damagePerMin.toFixed(0) }}</p>
            <p class="text-[10px] text-muted">DMG/m</p>
          </div>
          <div>
            <p class="text-sm font-bold tabular-nums text-default">{{ participant.goldPerMin.toFixed(0) }}</p>
            <p class="text-[10px] text-muted">Gold/m</p>
          </div>
        </div>
      </div>
    </div>

    <!-- Build order -->
    <div class="glass rounded-md border border-default/60 bg-elevated/60 p-3">
      <p class="mb-2 text-[10px] font-semibold uppercase tracking-wide text-muted">Build order</p>
      <div v-if="buildGroups.length" class="flex flex-wrap items-start gap-x-1 gap-y-3">
        <template v-for="(group, gi) in buildGroups" :key="`grp-${gi}`">
          <div class="flex flex-col items-center gap-1">
            <div class="flex items-center gap-0.5">
              <div
                v-for="(ev, ei) in group.events"
                :key="`ev-${gi}-${ei}`"
                class="relative"
                :title="`${fmtTime(ev.timestampMs)}`"
              >
                <GameTooltipItemIcon
                  :item="resolveEventItemId(ev) ? items[resolveEventItemId(ev)] ?? null : null"
                  :width="28"
                  :height="28"
                  class="size-7 rounded"
                  :class="isCorrection(ev.eventType) ? 'opacity-50 ring-1 ring-red-500/50' : ''"
                />
                <UIcon
                  v-if="ev.eventType === 'ITEM_UNDO'"
                  name="i-lucide-x"
                  class="absolute -right-1 -top-1 size-3 rounded-full bg-red-500 text-white"
                />
              </div>
            </div>
            <span class="text-[9px] tabular-nums text-muted">{{ group.label }}</span>
          </div>
          <UIcon
            v-if="gi < buildGroups.length - 1"
            name="i-lucide-chevron-right"
            class="mt-2.5 size-4 shrink-0 text-muted/50"
          />
        </template>
      </div>
      <p v-else class="text-[11px] text-muted">No build data for this game.</p>
    </div>

    <!-- Skill order -->
    <div class="glass rounded-md border border-default/60 bg-elevated/60 p-3">
      <p class="mb-2 text-[10px] font-semibold uppercase tracking-wide text-muted">Skill order</p>
      <div v-if="hasSkills" class="space-y-1 overflow-x-auto">
        <div
          v-for="row in skillRows"
          :key="`skill-${row.slot}`"
          class="flex items-center gap-0.5"
        >
          <span
            class="flex size-5 shrink-0 items-center justify-center rounded bg-default/50 text-[10px] font-bold text-default"
          >
            {{ row.letter }}
          </span>
          <span
            v-for="(filled, lvl) in row.levels"
            :key="`lvl-${lvl}`"
            class="flex size-5 shrink-0 items-center justify-center rounded text-[10px] font-semibold tabular-nums"
            :class="filled ? row.fill : 'bg-default/20 text-transparent'"
          >
            {{ filled ? lvl + 1 : '' }}
          </span>
        </div>
      </div>
      <p v-else class="text-[11px] text-muted">No skill data for this game.</p>
    </div>
  </div>
</template>
