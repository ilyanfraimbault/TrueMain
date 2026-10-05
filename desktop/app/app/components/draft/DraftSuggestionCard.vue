<script setup lang="ts">
import type { DraftReason } from '~/types/draft'
import type { TierEntry } from '~/composables/useTierList'

/**
 * One suggested pick or ban, poster-sized: the champion's loading art, its
 * leading reason's figure over the card and the reason itself in one line
 * under the name, every reason on hover (#1906). The figures are the draft
 * endpoint's — a measured delta in win-rate points or a rate, never the
 * ranking score, which is not a number to read. On the card in focus, its lane
 * numbers from the tier list.
 *
 * The default slot covers the card (the click that previews its build); the
 * `action` slot sits over the art's corner, above it (the hover or ban control).
 */
const props = defineProps<{
  championId: number
  /** The facts behind the suggestion, strongest first. */
  reasons: DraftReason[]
  /** Few games behind every reason: drawn as unproven. */
  thin: boolean
  /** Our lane, for the reasons' wording. */
  position: string
  entry: TierEntry | null
  focused: boolean
  /** Height of the card in the podium, 0..1 of the row. */
  height: number
}>()

const { nameOf } = useChampionStatics()

const texts = computed(() => props.reasons.map(reason => describeReason(reason, nameOf, props.position)))
const lead = computed(() => texts.value[0] ?? null)

const tone = computed(() => {
  if (!lead.value) return winRateTone(props.entry?.winRate ?? null)
  if (props.thin) return 'text-dimmed'
  return { good: 'text-data-good', bad: 'text-data-bad', neutral: 'text-highlighted' }[lead.value.tone]
})

/** Every reason, one per line — the hover says what the line under the name only starts. */
const why = computed(() => {
  if (!texts.value.length) {
    return props.entry ? `Win rate on this lane: ${percent(props.entry.winRate)} over ${props.entry.games.toLocaleString('en-US')} games` : undefined
  }
  return [...texts.value.map(text => text.full), props.thin ? 'Few games behind these figures' : null].filter(Boolean).join('\n')
})

const percent = (value: number) => `${(value * 100).toFixed(1)}%`
</script>

<template>
  <div class="relative flex h-full w-[112px] shrink-0 flex-col justify-end gap-1.5" :title="why">
    <p class="text-center font-bold tabular-nums leading-none tracking-tight" :class="[tone, focused ? 'text-[26px]' : 'text-[22px]']">
      <template v-if="lead">{{ lead.headline }}<span class="text-xs"> {{ lead.unit }}</span></template>
      <template v-else-if="entry">{{ (entry.winRate * 100).toFixed(1) }}<span class="text-xs">% WR</span></template>
    </p>

    <div
      class="relative w-full overflow-hidden rounded-lg bg-elevated transition-[box-shadow]"
      :class="focused ? 'ring-2 ring-primary shadow-[0_0_24px_-8px_var(--color-rosegold-400)]' : 'ring-1 ring-white/5'"
      :style="{ height: `${Math.round(height * 100)}%` }"
    >
      <ChampionArt :champion-id="championId" kind="loading" fade="none" position="50% 12%" :class="thin && 'opacity-70'" />
      <div class="absolute inset-0 bg-gradient-to-t from-ink-950 via-ink-950/40 via-30% to-transparent to-60%" />

      <div class="absolute inset-x-0 bottom-0 flex flex-col gap-1 p-2">
        <p class="truncate text-[13px] font-semibold text-highlighted">{{ nameOf(championId) }}</p>
        <p v-if="lead" class="truncate text-[11px] leading-tight text-muted">{{ lead.short }}</p>
        <div v-if="focused && entry" class="grid grid-cols-2 gap-1">
          <div class="flex flex-col">
            <span class="stat-label text-[9px]!">Win rate</span>
            <span class="stat-value text-xs">{{ percent(entry.winRate) }}</span>
          </div>
          <div class="flex flex-col">
            <span class="stat-label text-[9px]!">Pick rate</span>
            <span class="stat-value text-xs">{{ percent(entry.pickRate) }}</span>
          </div>
        </div>
        <UBadge v-if="lead && thin" color="neutral" variant="soft" size="sm" class="self-start">Few games</UBadge>
      </div>
      <TierMark v-if="entry" :tier="entry.tier" class="absolute left-2 top-2 scale-75 origin-top-left" />
      <div v-if="$slots.action" class="absolute right-1.5 top-1.5 z-10">
        <slot name="action" />
      </div>
    </div>
    <slot />
  </div>
</template>
