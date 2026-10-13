<script setup lang="ts">
import type { MatchDetailItemEvent } from '#shared/types/match-detail'
import type { StaticItemData } from '#shared/types/static-data'
import { isBuildOrderEvent, resolveEventItemId } from '#shared/utils/build'
import { formatGameClock } from '#common/utils/win-probability-swing'

// The selected player's purchases grouped into shopping trips: consecutive
// events in the same game-minute collapse into one cluster under a single time
// label, minute 0 being the starting purchase ("Starter"). `isBuildOrderEvent`
// strips auto-granted transforms (quest stages, empowered-recall boots) and
// their paired ITEM_DESTROYED so they don't clutter it.
const props = defineProps<{
  itemEvents: MatchDetailItemEvent[]
  items: Record<number, StaticItemData>
}>()

interface BuildGroup {
  minute: number
  label: string
  events: MatchDetailItemEvent[]
}
const groups = computed<BuildGroup[]>(() => {
  const result: BuildGroup[] = []
  for (const ev of props.itemEvents) {
    if (!isBuildOrderEvent(ev, props.items)) continue
    const minute = Math.floor(ev.timestampMs / 60000)
    const last = result.at(-1)
    if (last && last.minute === minute) last.events.push(ev)
    else result.push({ minute, label: minute === 0 ? 'Starter' : `${minute} min`, events: [ev] })
  }
  return result
})

function itemOf(ev: MatchDetailItemEvent) {
  const id = resolveEventItemId(ev)
  return id ? props.items[id] ?? null : null
}

// Only a sold item or an undo is a genuine "correction" worth dimming; the
// build order drops ITEM_DESTROYED entirely, so it never reaches the template.
function isCorrection(type: string) {
  return type === 'ITEM_UNDO' || type === 'ITEM_SOLD'
}
</script>

<template>
  <MatchDetailCard title="Build order">
    <div v-if="groups.length" class="flex flex-wrap items-start gap-x-1 gap-y-3">
      <template v-for="(group, gi) in groups" :key="`grp-${gi}`">
        <div class="flex flex-col items-center gap-1">
          <div class="flex items-center gap-0.5">
            <div
              v-for="(ev, ei) in group.events"
              :key="`ev-${gi}-${ei}`"
              class="relative"
              :title="formatGameClock(ev.timestampMs)"
            >
              <GameTooltipItemIcon
                :item="itemOf(ev)"
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
          v-if="gi < groups.length - 1"
          name="i-lucide-chevron-right"
          class="mt-2.5 size-4 shrink-0 text-muted/50"
        />
      </template>
    </div>
    <p v-else class="text-[11px] text-muted">No build data for this game.</p>
  </MatchDetailCard>
</template>
