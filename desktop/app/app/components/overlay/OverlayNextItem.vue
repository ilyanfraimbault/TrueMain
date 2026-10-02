<script setup lang="ts">
import type { GameState } from '~/types/game'
import { ITEM_CONTEXT_TONE_CLASS } from '#shared/utils/item-context'

/**
 * The game page's next-item panel (`GameNextItem`), narrowed for the overlay:
 * one column a glance over the game can take in — the item, why, the next
 * purchase, then the runners-up and the boots on one line. Same data, from
 * `useNextItemPanel`. Either half can be switched off in the overlay settings.
 */
const props = defineProps<{
  game: GameState
  nextItem: boolean
  boots: boolean
}>()

const { pending, answer, statics, build, top, runnersUp, boots: bootCandidates, why, remaining, steps, state, name, percent, affordable } = useNextItemPanel(toRef(props, 'game'))

const showFooter = computed(() => state.value === 'ready' && ((props.nextItem && runnersUp.value.length > 0) || (props.boots && bootCandidates.value.length > 0)))
</script>

<template>
  <section class="flex flex-col gap-2.5">
    <header class="flex items-center gap-2">
      <AppMark class="size-3.5 text-primary" />
      <span class="stat-label">{{ nextItem ? 'Next item' : 'Boots' }}</span>
      <span v-if="nextItem && build && !build.onTree" class="text-[11px] text-dimmed">off the mains' path</span>
      <UIcon v-if="pending && answer" name="i-lucide-loader-circle" class="ml-auto size-3.5 animate-spin text-dimmed" />
    </header>

    <template v-if="state === 'ready' && top">
      <div v-if="nextItem" class="flex items-start gap-3">
        <GameTooltipItemIcon :item="statics[top.itemId] ?? null" :width="44" :height="44" class="size-11 shrink-0 rounded-lg ring-1 ring-primary/60" />
        <div class="min-w-0 flex-1 space-y-0.5">
          <p class="truncate text-sm font-semibold text-highlighted">{{ name(top.itemId) }}</p>
          <p class="text-xs tabular-nums text-muted">{{ percent(top.share) }} of mains</p>
          <p v-if="why" class="text-xs leading-snug text-muted">
            <template v-for="(token, index) in why" :key="index">
              <span :class="token.tone && ITEM_CONTEXT_TONE_CLASS[token.tone]">{{ token.text }}</span>
            </template>
          </p>
        </div>
      </div>

      <div v-if="nextItem" class="flex flex-wrap items-center gap-1.5">
        <span
          v-for="step in steps"
          :key="step.itemId"
          class="flex items-center gap-1 rounded-md px-1.5 py-0.5 text-[11px] tabular-nums ring-1"
          :class="affordable(step.cost) ? 'bg-primary/10 text-highlighted ring-primary/40' : 'text-dimmed ring-default'"
        >
          <GameTooltipItemIcon :item="statics[step.itemId] ?? null" :width="16" :height="16" class="size-4 rounded-sm" />
          {{ step.cost }}
        </span>
        <span v-if="remaining > 0" class="text-[11px] tabular-nums text-muted">
          <span class="text-stat-gold">{{ remaining }}</span> to complete
        </span>
        <span v-else class="text-[11px] text-stat-gold">Ready to complete</span>
      </div>

      <div v-if="showFooter" class="flex items-end justify-between gap-3 border-t border-default pt-2">
        <div v-if="nextItem && runnersUp.length" class="flex items-center gap-1.5">
          <span class="stat-label mr-0.5">Or</span>
          <div v-for="candidate in runnersUp" :key="candidate.itemId" class="flex flex-col items-center">
            <GameTooltipItemIcon :item="statics[candidate.itemId] ?? null" :width="26" :height="26" class="size-[26px] rounded" />
            <span class="text-[10px] tabular-nums text-dimmed">{{ percent(candidate.share) }}</span>
          </div>
        </div>
        <div v-if="boots && bootCandidates.length" class="ml-auto flex items-center gap-1.5">
          <span v-if="nextItem" class="stat-label mr-0.5">Boots</span>
          <div v-for="(candidate, index) in bootCandidates" :key="candidate.itemId" class="flex flex-col items-center">
            <GameTooltipItemIcon
              :item="statics[candidate.itemId] ?? null"
              :width="index === 0 ? 26 : 22"
              :height="index === 0 ? 26 : 22"
              class="rounded"
              :class="index === 0 ? 'size-[26px] ring-1 ring-primary/60' : 'size-[22px] opacity-70'"
            />
            <span class="text-[10px] tabular-nums" :class="index === 0 ? 'text-muted' : 'text-dimmed'">{{ percent(candidate.share) }}</span>
          </div>
        </div>
      </div>
    </template>

    <p v-else-if="state === 'unmeasured'" class="text-xs text-muted">No measured builds for this champion on this lane yet.</p>
    <p v-else-if="state === 'complete'" class="text-xs text-muted">The mains' build is complete.</p>
    <p v-else-if="state === 'offline'" class="text-xs text-muted">Cannot reach TrueMain right now.</p>
    <div v-else class="flex items-center gap-3">
      <USkeleton class="size-11 rounded-lg" />
      <div class="space-y-1.5">
        <USkeleton class="h-3.5 w-36" />
        <USkeleton class="h-3 w-24" />
      </div>
    </div>
  </section>
</template>
