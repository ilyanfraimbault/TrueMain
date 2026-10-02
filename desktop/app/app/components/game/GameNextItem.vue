<script setup lang="ts">
import type { GameState } from '~/types/game'
import { ITEM_CONTEXT_TONE_CLASS } from '#shared/utils/item-context'

/**
 * What to complete next (#1751): the item the champion's mains take from
 * where this build stands, in a game like this one (#1749), and what of it
 * can be bought with the gold in hand. Read in two seconds while dead or on
 * the walk out of base, so it carries the decision and nothing else: the
 * item, why when a situation moved it, the next purchase, the two runners-up,
 * and the boots while they are still open. The overlay draws the same panel
 * narrower (`OverlayNextItem`), from the same `useNextItemPanel`.
 */
const props = defineProps<{ game: GameState }>()

const { answer, pending, statics, build, top, runnersUp, boots, why, remaining, steps, state, name, percent, reason, affordable } = useNextItemPanel(toRef(props, 'game'))
</script>

<template>
  <section class="surface grid grid-cols-[minmax(0,1fr)_auto] gap-6 rounded-xl px-4 py-3">
    <div class="min-w-0">
      <div class="mb-2 flex items-center gap-2">
        <span class="stat-label">Next item</span>
        <span v-if="build && !build.onTree" class="text-xs text-dimmed" title="Your build left the path the mains take; this continues from your latest item they build.">off the mains' path</span>
        <UIcon v-if="pending && answer" name="i-lucide-loader-circle" class="size-3.5 animate-spin text-dimmed" />
      </div>

      <div v-if="state === 'ready' && top" class="flex items-start gap-3">
        <GameTooltipItemIcon :item="statics[top.itemId] ?? null" :width="52" :height="52" class="size-13 shrink-0 rounded-lg ring-1 ring-primary/60" />

        <div class="min-w-0 flex-1 space-y-1.5">
          <div class="flex items-baseline gap-2">
            <span class="truncate text-base font-semibold text-highlighted">{{ name(top.itemId) }}</span>
            <span class="text-xs tabular-nums text-muted" :title="`${top.games} games of the mains on this step`">{{ percent(top.share) }} of mains</span>
          </div>
          <p v-if="why" class="text-sm text-muted">
            <template v-for="(token, index) in why" :key="index">
              <span :class="token.tone && ITEM_CONTEXT_TONE_CLASS[token.tone]">{{ token.text }}</span>
            </template>
          </p>

          <div class="flex flex-wrap items-center gap-x-3 gap-y-1.5 pt-0.5">
            <span v-if="remaining > 0" class="text-xs tabular-nums text-muted">
              <span class="text-stat-gold">{{ remaining }}</span> gold to complete
            </span>
            <span v-else class="text-xs text-stat-gold">Ready to complete</span>
            <span
              v-for="step in steps"
              :key="step.itemId"
              class="flex items-center gap-1.5 rounded-md px-1.5 py-0.5 text-xs tabular-nums ring-1"
              :class="affordable(step.cost) ? 'bg-primary/10 text-highlighted ring-primary/40' : 'text-dimmed ring-default'"
              :title="affordable(step.cost) ? 'You can buy this now' : `${step.cost - game.gold} gold short`"
            >
              <GameTooltipItemIcon :item="statics[step.itemId] ?? null" :width="18" :height="18" class="size-[18px] rounded-sm" />
              {{ step.cost }}
            </span>
          </div>
        </div>

        <div v-if="runnersUp.length" class="flex shrink-0 flex-col items-end gap-1.5 border-l border-default pl-3">
          <span class="stat-label">Or</span>
          <div class="flex gap-1.5">
            <div v-for="candidate in runnersUp" :key="candidate.itemId" class="flex flex-col items-center gap-0.5">
              <GameTooltipItemIcon :item="statics[candidate.itemId] ?? null" :width="30" :height="30" class="size-[30px] rounded" />
              <span class="text-[10px] tabular-nums text-dimmed">{{ percent(candidate.share) }}</span>
            </div>
          </div>
        </div>
      </div>

      <p v-else-if="state === 'unmeasured'" class="text-sm text-muted">No measured builds for this champion on this lane yet.</p>
      <p v-else-if="state === 'complete'" class="text-sm text-muted">The mains' build is complete.</p>
      <p v-else-if="state === 'offline'" class="text-sm text-muted">Cannot reach TrueMain right now.</p>
      <div v-else class="flex items-center gap-3">
        <USkeleton class="size-13 rounded-lg" />
        <div class="space-y-1.5">
          <USkeleton class="h-4 w-40" />
          <USkeleton class="h-3 w-56" />
        </div>
      </div>
    </div>

    <div v-if="boots.length" class="flex flex-col gap-2 border-l border-default pl-4">
      <span class="stat-label">Boots</span>
      <div class="flex gap-2">
        <div v-for="(candidate, index) in boots" :key="candidate.itemId" class="flex flex-col items-center gap-0.5">
          <GameTooltipItemIcon
            :item="statics[candidate.itemId] ?? null"
            :width="index === 0 ? 36 : 28"
            :height="index === 0 ? 36 : 28"
            class="rounded"
            :class="index === 0 ? 'size-9 ring-1 ring-primary/60' : 'size-7 opacity-70'"
          />
          <span class="text-[10px] tabular-nums" :class="index === 0 ? 'text-muted' : 'text-dimmed'">{{ percent(candidate.share) }}</span>
        </div>
      </div>
      <p v-if="reason(boots[0] ?? null)" class="max-w-40 text-xs text-dimmed">
        <template v-for="(token, index) in reason(boots[0] ?? null)" :key="index">
          <span :class="token.tone && ITEM_CONTEXT_TONE_CLASS[token.tone]">{{ token.text }}</span>
        </template>
      </p>
    </div>
  </section>
</template>
