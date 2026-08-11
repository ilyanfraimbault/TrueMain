<script setup lang="ts">
import type { RouteLocationRaw } from 'vue-router'
import { POSITION_BY_VALUE } from '~/utils/positions'
import { presenceTone, winRateTone } from '~/utils/rate-tone'
import { formatPercentage, formatPercentageOrDash } from '~~/shared/utils/ddragon'

// A champion in the tier list: the portrait, its lane badged into the corner,
// and nothing else. The name and the three rates moved into the hover tooltip
// (#1079) so a tier group reads as a wall of faces — the unit a player actually
// scans — instead of a column of stat lines that made a five-tier page scroll.
const props = defineProps<{
  to: RouteLocationRaw
  name: string
  iconUrl: string
  /** Raw position value from the API (`TOP`, `UTILITY`, …). */
  position: string
  winRate: number
  pickRate: number
  /** Null on patches predating ban ingestion (#920) — rendered as a dash. */
  banRate: number | null
}>()

const positionOption = computed(() => POSITION_BY_VALUE.get(props.position))

// One line per rate, label and value in their own column, so the three numbers
// read as a small table — same shape as RateBadge's tooltip.
const stats = computed(() => [
  { label: 'Win rate', value: formatPercentage(props.winRate), tone: winRateTone(props.winRate) },
  { label: 'Pick rate', value: formatPercentage(props.pickRate), tone: presenceTone(props.pickRate) },
  { label: 'Ban rate', value: formatPercentageOrDash(props.banRate), tone: presenceTone(props.banRate) },
])

// The tooltip is hover-only, so the link itself has to carry the whole content
// for a screen reader (and for touch, where no tooltip ever opens). A missing
// ban rate is dropped entirely rather than announced as "dash BR": an absent
// stat is better left unsaid than read out as punctuation.
const ariaLabel = computed(() => {
  const parts = [
    `${formatPercentage(props.winRate, 0)} WR`,
    `${formatPercentage(props.pickRate, 0)} PR`,
    ...(props.banRate === null ? [] : [`${formatPercentage(props.banRate, 0)} BR`]),
  ]
  const lane = positionOption.value ? `, ${positionOption.value.label}` : ''
  return `View ${props.name}${lane} (${parts.join(', ')})`
})
</script>

<template>
  <UTooltip
    :delay-duration="150"
    :ui="{ content: 'h-auto items-start p-2' }"
  >
    <template #content>
      <div class="space-y-1.5">
        <p class="text-sm font-medium text-highlighted">
          {{ name }}
        </p>
        <div class="grid grid-cols-[auto_auto] gap-x-4 gap-y-0.5 text-xs">
          <template
            v-for="stat in stats"
            :key="stat.label"
          >
            <span class="text-muted">{{ stat.label }}</span>
            <span
              class="stat-value text-right text-xs"
              :class="stat.tone"
            >
              {{ stat.value }}
            </span>
          </template>
        </div>
      </div>
    </template>

    <NuxtLink
      :to="to"
      :aria-label="ariaLabel"
      class="relative block rounded-lg ring-primary/60 transition hover:ring-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
    >
      <SkeletonImage
        :src="iconUrl"
        :alt="name"
        :width="48"
        :height="48"
        loading="lazy"
        class="size-12 rounded-lg"
      />

      <!-- Lane badge in the bottom-right corner of the portrait, the same
           anchoring the champion directory uses for the secondary rune tree.
           The opaque disc + ring keeps the glyph readable over whatever splash
           colour sits underneath it. -->
      <SkeletonImage
        v-if="positionOption?.iconUrl"
        :src="positionOption.iconUrl"
        :alt="positionOption.label"
        :width="18"
        :height="18"
        loading="lazy"
        class="absolute -bottom-1 -right-1 size-[18px] rounded-full bg-default p-px ring-1 ring-default"
      />
    </NuxtLink>
  </UTooltip>
</template>
