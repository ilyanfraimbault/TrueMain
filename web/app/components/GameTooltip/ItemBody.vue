<script setup lang="ts">
import { computed } from 'vue'
import type { StaticItemData } from '~~/shared/types/static-data'
import { parseItemDescription } from '~~/shared/utils/tooltip-parser'
import { formatCount } from '~~/shared/utils/counts'
import { formatPercentageAdaptive } from '~~/shared/utils/ddragon'
import type { ItemContextCard } from '~~/shared/utils/item-context'
import { ITEM_CONTEXT_TONE_CLASS, itemContextAxisPhrase, wordableAxes } from '~~/shared/utils/item-context'

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
        rate: formatPercentageAdaptive(axis.rateIn),
        phrase: itemContextAxisPhrase(axis)!,
      }))
    : [],
)

/**
 * The class as a short label — and only where it says something. A `Preference` renders
 * nothing at all: "no situation moves this" is true but it is not worth a line on a card
 * a reader opens to learn something, and printing it on three quarters of all items made
 * the section read as noise rather than as an answer.
 */
const contextClassLabel = computed(() => {
  switch (props.context?.class) {
    case 'Core': return 'Core item'
    case 'Situational': return 'Situational'
    default: return null
  }
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

    <!-- Why this item (#1451): the situations that measurably move this pick. One line
         each, the rate and the situation and nothing else — the contrast rate, the sample
         and the scope footnote were all cut, because the class above already says the pick
         is situational and the card is read in a hover, not studied. The key term carries
         the item tooltip's own colour vocabulary (see `ITEM_CONTEXT_TONE_CLASS`), so magic
         damage is the same cyan here as in the description right above it. -->
    <div
      v-if="contextClassLabel"
      class="mt-2 border-t border-default/40 pt-2"
    >
      <p class="text-xs font-semibold uppercase tracking-wide text-muted">
        {{ contextClassLabel }}
      </p>

      <ul
        v-if="contextLines.length"
        class="mt-1 space-y-1 text-sm"
      >
        <li
          v-for="line in contextLines"
          :key="line.key"
          class="text-toned"
        >
          <span class="font-semibold tabular-nums text-default">{{ line.rate }}</span>{{ ' ' }}<template
            v-for="(token, index) in line.phrase"
            :key="`${line.key}-${index}`"
          ><span :class="token.tone ? ITEM_CONTEXT_TONE_CLASS[token.tone] : undefined">{{ token.text }}</span></template>
        </li>
      </ul>
    </div>
  </div>
</template>
