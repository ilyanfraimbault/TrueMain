<script setup lang="ts">
import { computed } from 'vue'
import type { StaticItemData } from '~~/shared/types/static-data'
import { parseItemDescription } from '~~/shared/utils/tooltip-parser'
import { formatCount } from '~~/shared/utils/counts'
import { formatPercentageAdaptive } from '~~/shared/utils/ddragon'
import type { ItemContextCard } from '~~/shared/utils/item-context'
import { itemContextAxisPhrase, wordableAxes } from '~~/shared/utils/item-context'

const props = defineProps<{
  item: StaticItemData
  /** Optional pickrate (0..1) — only passed from the build-tree call site to surface slot popularity. */
  pickRate?: number
  /**
   * The situational verdict for this item (#1451): why it is built, measured. Absent on
   * every call site that has no population slice behind it, and the card then renders
   * exactly as it did before.
   */
  context?: ItemContextCard
}>()

/**
 * The "why this item" block (#1451).
 *
 * Every line is a measurement the ingestor decided; nothing here computes a rate or a
 * threshold. A finding this build has no wording for is dropped rather than printed as a
 * raw identifier, so a front end older than the backend loses a line instead of leaking a
 * `EnemyArmorPenetration` at a player.
 */
const contextLines = computed(() =>
  props.context
    ? wordableAxes(props.context).map(axis => ({
        key: `${axis.axis}-${axis.bucket}`,
        phrase: itemContextAxisPhrase(axis)!,
        rateIn: formatPercentageAdaptive(axis.rateIn),
        rateOut: formatPercentageAdaptive(axis.rateOut),
        games: formatCount(axis.totalIn + axis.totalOut),
        inGame: !axis.draftTime,
      }))
    : [],
)

/** The class as a short label. `Core` and `Preference` are answers in themselves. */
const contextClassLabel = computed(() => {
  switch (props.context?.class) {
    case 'Core': return 'Core item'
    case 'Situational': return 'Situational'
    case 'Preference': return 'Preference'
    default: return null
  }
})

/**
 * The one sentence a `Preference` gets. Saying "no situation moves this" is a result, not
 * an empty state: it tells a reader the choice is theirs rather than leaving them to
 * wonder what the card failed to load.
 */
const showsPreferenceNote = computed(() =>
  props.context?.class === 'Preference' && contextLines.value.length === 0)

/**
 * The scope the verdict was measured on. Always worth saying: these numbers carry no rank
 * dimension while the panels around them do, and a patch window wider than one is a
 * different claim from "this patch".
 */
const contextFootnote = computed(() => {
  if (!props.context) return null
  const parts = ['across all ranks']
  if (props.context.scopeNote) parts.push(props.context.scopeNote)
  if (props.context.patchWindow > 1) parts.push(`last ${props.context.patchWindow} patches`)
  return parts.join(' · ')
})

const parsed = computed(() => props.item.description ? parseItemDescription(props.item.description) : [])
const hasDescription = computed(() => parsed.value.length > 0)
const goldLabel = computed(() => props.item.totalGold > 0 ? `${formatCount(props.item.totalGold)}g` : null)
// Adaptive precision: sub-1% picks would otherwise round to "0%" and look broken.
const pickRateLabel = computed(() =>
  props.pickRate === undefined || props.pickRate === null
    ? null
    : formatPercentageAdaptive(props.pickRate))
</script>

<template>
  <div>
    <header class="mb-2 flex items-center gap-3">
      <SkeletonImage
        :src="item.iconUrl"
        :alt="item.name"
        :width="36"
        :height="36"
        class="size-9 shrink-0 rounded"
      />
      <div class="min-w-0 flex-1">
        <div class="truncate font-semibold text-default">
          {{ item.name }}
        </div>
        <div
          v-if="goldLabel"
          class="text-xs text-stat-active"
        >
          {{ goldLabel }}
        </div>
      </div>
      <div
        v-if="pickRateLabel"
        class="shrink-0 self-start text-xs font-semibold text-muted"
      >
        {{ pickRateLabel }} pick
      </div>
    </header>
    <div class="border-t border-default/40 pt-2 text-sm">
      <GameTooltipRichText
        v-if="hasDescription"
        :segments="parsed"
      />
      <p
        v-else-if="item.plaintext"
        class="text-muted"
      >
        {{ item.plaintext }}
      </p>
    </div>

    <!-- Why this item (#1451): the situations that measurably move this pick, each with
         its two rates and its sample. Rendered only when the fold had something to say —
         a card with nothing measured looks exactly as it did before this section
         existed. -->
    <div
      v-if="contextClassLabel"
      class="mt-2 border-t border-default/40 pt-2"
    >
      <div class="mb-1 flex items-center gap-2">
        <span class="text-xs font-semibold uppercase tracking-wide text-muted">
          {{ contextClassLabel }}
        </span>
      </div>

      <ul
        v-if="contextLines.length"
        class="space-y-1 text-sm"
      >
        <li
          v-for="line in contextLines"
          :key="line.key"
          class="text-toned"
        >
          <span class="font-semibold tabular-nums text-default">{{ line.rateIn }}</span>
          {{ line.phrase }}
          <span class="text-muted">·</span>
          <span class="font-semibold tabular-nums text-default">{{ line.rateOut }}</span>
          <span class="text-muted">otherwise</span>
          <span class="text-muted">· {{ line.games }} games</span>
          <span
            v-if="line.inGame"
            class="text-muted italic"
          >· in game</span>
        </li>
      </ul>

      <p
        v-else-if="showsPreferenceNote"
        class="text-sm text-muted"
      >
        No draft situation moves this pick.
      </p>

      <p
        v-if="contextFootnote"
        class="mt-1 text-xs text-dimmed"
      >
        {{ contextFootnote }}
      </p>
    </div>
  </div>
</template>
