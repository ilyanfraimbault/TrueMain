<script setup lang="ts">
import type { ChampionPatchDiffResponse, ChampionPatchDiffSide } from '~~/shared/types/champions'
import type { RuneTreeResponse, StaticItemData } from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'

const props = withDefaults(defineProps<{
  diff: ChampionPatchDiffResponse | null
  itemsMap: Record<number, StaticItemData>
  runeTree: RuneTreeResponse | null
  /** Patches with data for this champion/lane, newest → oldest; populates both selectors. */
  patchOptions: Array<{ label: string, value: string }>
  fromPatch: string | null
  toPatch: string | null
  loading?: boolean
}>(), {
  loading: false,
})

const emit = defineEmits<{
  'update:fromPatch': [value: string]
  'update:toPatch': [value: string]
}>()

// The endpoint resolves its own from/to defaults; mirror them into the
// selectors so the pickers reflect the slice actually shown even before the
// user touches them.
const selectedFrom = computed(() => props.diff?.from?.patch || props.fromPatch || '')
const selectedTo = computed(() => props.diff?.to?.patch || props.toPatch || '')

const hasBothSides = computed(() => Boolean(props.diff?.from && props.diff?.to))

const winRateChange = computed(() => props.diff?.delta?.winRateChange ?? 0)
// Signed win-rate swing, e.g. "+2.1%" — the headline of the section.
const winRateChangeLabel = computed(() => {
  const value = winRateChange.value
  const sign = value > 0 ? '+' : ''
  return `${sign}${formatPercentage(value, 1)}`
})
const winRateTone = computed(() => {
  if (Math.abs(winRateChange.value) < 0.0005) return 'text-muted'
  return winRateChange.value > 0 ? 'text-success' : 'text-error'
})

function itemById(itemId: number): StaticItemData | null {
  return itemId > 0 ? props.itemsMap[itemId] ?? null : null
}
function keystoneById(perkId: number) {
  return perkId > 0 ? props.runeTree?.perks?.[perkId] ?? null : null
}

function onFromChange(value: unknown) {
  if (typeof value === 'string' && value) emit('update:fromPatch', value)
}
function onToChange(value: unknown) {
  if (typeof value === 'string' && value) emit('update:toPatch', value)
}

// True when both sides are present but nothing notable moved — drives the
// "no notable changes" footnote so the section doesn't read as broken.
const noChanges = computed(() => {
  const delta = props.diff?.delta
  return Boolean(delta) && !delta!.firstItemChanged && !delta!.keystoneChanged && !delta!.skillOrderChanged
})

// One side resolved but the other didn't: the user picked (or defaulted into) a
// patch the champion has no data on. Distinct from "no history at all" so the
// empty copy can be accurate.
const oneSideMissing = computed(() =>
  Boolean(props.diff && (props.diff.from || props.diff.to) && !(props.diff.from && props.diff.to)),
)

function winRateLabel(side: ChampionPatchDiffSide | null | undefined): string {
  return side ? formatPercentage(side.winRate, 1) : '—'
}
</script>

<template>
  <SectionCard>
    <template #title>
      <div class="flex flex-wrap items-center justify-between gap-2">
        <div class="flex flex-col gap-0.5">
          <h2 class="text-sm font-semibold">
            Patch diff
          </h2>
          <p class="text-xs text-muted">
            What changed for this champion between two patches.
          </p>
        </div>
        <div class="flex items-center gap-2">
          <USelect
            :model-value="selectedFrom"
            :items="patchOptions"
            placeholder="From"
            size="sm"
            class="w-24"
            aria-label="Compare from patch"
            @update:model-value="onFromChange"
          />
          <UIcon
            name="i-lucide-arrow-right"
            class="size-4 shrink-0 text-dimmed"
          />
          <USelect
            :model-value="selectedTo"
            :items="patchOptions"
            placeholder="To"
            size="sm"
            class="w-24"
            aria-label="Compare to patch"
            @update:model-value="onToChange"
          />
        </div>
      </div>
    </template>

    <USkeleton
      v-if="loading"
      class="h-[160px] w-full rounded-lg"
    />

    <p
      v-else-if="!hasBothSides"
      class="rounded-lg px-4 py-8 text-center text-sm text-muted"
    >
      {{ oneSideMissing
        ? 'No data for this champion on one of the selected patches — pick another patch to compare.'
        : 'Not enough patch history yet to compare this champion across patches.' }}
    </p>

    <div
      v-else
      class="flex flex-col gap-4"
    >
      <!-- Win-rate swing headline -->
      <div class="flex flex-wrap items-end justify-center gap-6">
        <div class="flex flex-col items-center gap-0.5">
          <span class="text-xs text-muted">Patch {{ diff!.from!.patch }}</span>
          <span class="text-lg font-semibold tabular-nums text-default">
            {{ winRateLabel(diff!.from) }}
          </span>
          <span class="text-xs text-dimmed">{{ diff!.from!.games }} games</span>
        </div>
        <div class="flex flex-col items-center gap-0.5">
          <span class="text-xs text-muted">Win rate change</span>
          <span
            class="text-2xl font-bold tabular-nums"
            :class="winRateTone"
          >{{ winRateChangeLabel }}</span>
        </div>
        <div class="flex flex-col items-center gap-0.5">
          <span class="text-xs text-muted">Patch {{ diff!.to!.patch }}</span>
          <span class="text-lg font-semibold tabular-nums text-default">
            {{ winRateLabel(diff!.to) }}
          </span>
          <span class="text-xs text-dimmed">{{ diff!.to!.games }} games</span>
        </div>
      </div>

      <!-- Build / rune / skill shifts -->
      <div class="grid gap-3 sm:grid-cols-3">
        <!-- First item -->
        <div class="glass flex flex-col items-center gap-2 rounded-lg px-3 py-3">
          <span class="text-xs font-medium text-muted">First item</span>
          <div class="flex items-center gap-2">
            <GameTooltipItemIcon
              :item="itemById(diff!.from!.topFirstItemId)"
              :width="32"
              :height="32"
              class="size-8 rounded"
            />
            <UIcon
              name="i-lucide-arrow-right"
              class="size-3.5 shrink-0 text-dimmed"
            />
            <GameTooltipItemIcon
              :item="itemById(diff!.to!.topFirstItemId)"
              :width="32"
              :height="32"
              class="size-8 rounded"
            />
          </div>
          <UBadge
            v-if="diff!.delta?.firstItemChanged"
            color="primary"
            variant="soft"
            size="sm"
          >
            New popular item
          </UBadge>
          <span
            v-else
            class="text-xs text-dimmed"
          >Unchanged</span>
        </div>

        <!-- Keystone -->
        <div class="glass flex flex-col items-center gap-2 rounded-lg px-3 py-3">
          <span class="text-xs font-medium text-muted">Keystone</span>
          <div class="flex items-center gap-2">
            <GameTooltipPerkIcon
              :perk="keystoneById(diff!.from!.topKeystoneId)"
              :width="32"
              :height="32"
              class="size-8"
            />
            <UIcon
              name="i-lucide-arrow-right"
              class="size-3.5 shrink-0 text-dimmed"
            />
            <GameTooltipPerkIcon
              :perk="keystoneById(diff!.to!.topKeystoneId)"
              :width="32"
              :height="32"
              class="size-8"
            />
          </div>
          <UBadge
            v-if="diff!.delta?.keystoneChanged"
            color="primary"
            variant="soft"
            size="sm"
          >
            Changed
          </UBadge>
          <span
            v-else
            class="text-xs text-dimmed"
          >Unchanged</span>
        </div>

        <!-- Skill order -->
        <div class="glass flex flex-col items-center gap-2 rounded-lg px-3 py-3">
          <span class="text-xs font-medium text-muted">Skill order</span>
          <div class="flex items-center gap-1.5 text-xs font-semibold tabular-nums">
            <span class="text-muted">{{ diff!.from!.topSkillOrder.join(' › ') || '—' }}</span>
            <UIcon
              name="i-lucide-arrow-right"
              class="size-3.5 shrink-0 text-dimmed"
            />
            <span class="text-default">{{ diff!.to!.topSkillOrder.join(' › ') || '—' }}</span>
          </div>
          <UBadge
            v-if="diff!.delta?.skillOrderChanged"
            color="primary"
            variant="soft"
            size="sm"
          >
            Changed
          </UBadge>
          <span
            v-else
            class="text-xs text-dimmed"
          >Unchanged</span>
        </div>
      </div>

      <p
        v-if="noChanges"
        class="text-center text-xs text-muted"
      >
        No notable build, rune or skill changes between these patches.
      </p>
    </div>
  </SectionCard>
</template>
