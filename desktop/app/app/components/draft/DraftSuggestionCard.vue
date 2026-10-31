<script setup lang="ts">
import { formatCount } from '#shared/utils/counts'
import { formatPercentage } from '#shared/utils/ddragon'
import type { DraftReason } from '~/types/draft'
import type { TierEntry } from '~/composables/useTierList'
import type { DamageNote } from '~/utils/draft-damage'

/**
 * One suggested pick or ban, poster-sized: the champion's loading art, its
 * leading reason's figure over the card and the reason itself in one line
 * under the name, every reason on hover (#1906). The figures are the draft
 * endpoint's — a measured delta in win-rate points or a rate, never the
 * ranking score, which is not a number to read. On the card in focus, its lane
 * numbers from the tier list, on every card.
 *
 * The default slot covers the card (the click that previews its build); the
 * `action` slot is the bar under the art, above that cover (the hover or ban
 * control). Focus only rings the card: nothing on it grows or appears, so the
 * podium holds still under the cursor.
 * A pick that answers a one-sided team's damage mix carries a note (#1907), and
 * the player's own recent games on it are said on hover (#1906) — both said,
 * never scored.
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
  /** What the pick does to our team's damage mix, when it answers it. */
  note?: DamageNote | null
  /** The player's own recent games on the champion, from their client — said, never scored. */
  comfort?: string | null
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
  const note = props.note?.text ?? null
  const comfort = props.comfort ?? null
  if (!texts.value.length) {
    const lane = props.entry ? `Win rate on this lane: ${formatPercentage(props.entry.winRate)} over ${formatCount(props.entry.games)} games` : null
    return [lane, note, comfort].filter(Boolean).join('\n') || undefined
  }
  return [...texts.value.map(text => text.full), props.thin ? 'Few games behind these figures' : null, note, comfort].filter(Boolean).join('\n')
})
</script>

<template>
  <div class="relative flex h-full w-[112px] shrink-0 flex-col justify-end gap-1.5" :title="why">
    <p class="text-center text-[22px] font-bold tabular-nums leading-none tracking-tight" :class="tone">
      <template v-if="lead">{{ lead.headline }}<span class="text-xs"> {{ lead.unit }}</span></template>
      <template v-else-if="entry">{{ formatPercentage(entry.winRate) }}<span class="text-xs"> WR</span></template>
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
        <div v-if="entry" class="grid grid-cols-2 gap-1">
          <div class="flex flex-col">
            <span class="stat-label text-[9px]!">Win rate</span>
            <span class="stat-value text-xs">{{ formatPercentage(entry.winRate) }}</span>
          </div>
          <div class="flex flex-col">
            <span class="stat-label text-[9px]!">Pick rate</span>
            <span class="stat-value text-xs">{{ formatPercentage(entry.pickRate) }}</span>
          </div>
        </div>
        <UBadge v-if="lead && thin" color="neutral" variant="soft" size="sm" class="self-start">Few games</UBadge>
        <span
          v-if="note"
          class="flex w-fit items-center gap-1 self-start whitespace-nowrap rounded bg-ink-950/70 px-1.5 py-0.5 text-[10px] font-medium leading-none ring-1 ring-white/10"
          :class="note.adds === 'magic' ? 'text-stat-mr' : 'text-stat-ad'"
        >
          <UIcon name="i-lucide-plus" class="size-2.5" />{{ note.adds === 'magic' ? 'Magic damage' : 'Physical damage' }}
        </span>
      </div>
      <TierBadge v-if="entry" stacked :tier="entry.tier" class="absolute left-2 top-2 scale-75 origin-top-left" />
    </div>
    <slot />
    <div v-if="$slots.action" class="relative z-10">
      <slot name="action" />
    </div>
  </div>
</template>
