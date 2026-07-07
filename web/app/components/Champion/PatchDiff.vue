<script setup lang="ts">
import type { ChampionPatchDiffResponse, ChampionPatchDiffSide } from '~~/shared/types/champions'
import type { ChampionStaticData, RuneTreeResponse, StaticItemData } from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'

const props = withDefaults(defineProps<{
  diff: ChampionPatchDiffResponse | null
  itemsMap: Record<number, StaticItemData>
  runeTree: RuneTreeResponse | null
  /** Champion spell metadata — powers the skill-order spell icons. */
  championStatic: ChampionStaticData | null
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

const fromSide = computed(() => props.diff?.from ?? null)
const toSide = computed(() => props.diff?.to ?? null)

// The endpoint resolves its own from/to defaults; mirror them into the
// selectors so the pickers reflect the slice actually shown even before the
// user touches them.
const selectedFrom = computed(() => fromSide.value?.patch || props.fromPatch || '')
const selectedTo = computed(() => toSide.value?.patch || props.toPatch || '')

const hasBothSides = computed(() => Boolean(fromSide.value && toSide.value))

const winRateChange = computed(() => props.diff?.delta?.winRateChange ?? 0)
// Signed win-rate swing, e.g. "+2.1%" — shown next to the newer patch's rate.
const winRateChangeLabel = computed(() => {
  const value = winRateChange.value
  const sign = value > 0 ? '+' : ''
  return `${sign}${formatPercentage(value, 1)}`
})
const winRateTone = computed(() => {
  if (Math.abs(winRateChange.value) < 0.0005) return 'text-muted'
  return winRateChange.value > 0 ? 'text-success' : 'text-error'
})

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

function winRateLabel(side: ChampionPatchDiffSide | null): string {
  return side ? formatPercentage(side.winRate, 1) : '—'
}
</script>

<template>
  <SectionCard>
    <template #title>
      <div class="flex flex-col gap-0.5">
        <h2 class="text-sm font-semibold">
          Patch diff
        </h2>
        <p class="text-xs text-muted">
          Compare this champion's core build, runes and skill order across two patches.
        </p>
      </div>
    </template>

    <USkeleton
      v-if="loading"
      class="h-[220px] w-full rounded-lg"
    />

    <p
      v-else-if="patchOptions.length === 0"
      class="rounded-lg px-4 py-8 text-center text-sm text-muted"
    >
      Not enough patch history yet to compare this champion across patches.
    </p>

    <div
      v-else
      class="flex flex-col gap-5"
    >
      <!-- Two sides, each with its own patch selector and win rate. The grid
           keeps every dimension row aligned left ⇄ right. -->
      <div class="grid grid-cols-2 gap-x-4 gap-y-6 sm:gap-x-8">
        <!-- Column headers: patch selector + win rate -->
        <div class="flex flex-col items-center gap-1">
          <USelect
            :model-value="selectedFrom"
            :items="patchOptions"
            placeholder="From patch"
            size="sm"
            class="w-28"
            aria-label="Compare from patch"
            @update:model-value="onFromChange"
          />
          <span class="text-lg font-semibold tabular-nums text-default">
            {{ winRateLabel(fromSide) }}
          </span>
          <span class="text-xs text-dimmed">
            {{ fromSide ? `${fromSide.games} games` : 'No data' }}
          </span>
        </div>
        <div class="flex flex-col items-center gap-1">
          <USelect
            :model-value="selectedTo"
            :items="patchOptions"
            placeholder="To patch"
            size="sm"
            class="w-28"
            aria-label="Compare to patch"
            @update:model-value="onToChange"
          />
          <span class="flex items-baseline gap-1.5">
            <span class="text-lg font-semibold tabular-nums text-default">
              {{ winRateLabel(toSide) }}
            </span>
            <span
              v-if="hasBothSides"
              class="text-xs font-semibold tabular-nums"
              :class="winRateTone"
            >{{ winRateChangeLabel }}</span>
          </span>
          <span class="text-xs text-dimmed">
            {{ toSide ? `${toSide.games} games` : 'No data' }}
          </span>
        </div>

        <!-- Core build — ChampionCoreBuildPath self-titles "Build path". The
             divider row carries the dimension's own changed/unchanged badge,
             restoring the per-dimension signal the old single-block layout had. -->
        <div class="col-span-2 flex items-center justify-between border-t border-default/60 pt-3">
          <span class="text-xs font-medium uppercase tracking-wide text-muted">Core build</span>
          <UBadge
            v-if="diff?.delta?.firstItemChanged"
            color="primary"
            variant="soft"
            size="sm"
          >
            Changed
          </UBadge>
          <span
            v-else-if="hasBothSides"
            class="text-xs text-dimmed"
          >Unchanged</span>
        </div>
        <div class="flex justify-center">
          <ChampionCoreBuildPath
            :path="fromSide?.itemPath ?? null"
            :items-map="itemsMap"
          />
        </div>
        <div class="flex justify-center">
          <ChampionCoreBuildPath
            :path="toSide?.itemPath ?? null"
            :items-map="itemsMap"
          />
        </div>

        <!-- Runes -->
        <div class="col-span-2 flex items-center justify-between border-t border-default/60 pt-3">
          <span class="text-xs font-medium uppercase tracking-wide text-muted">Runes</span>
          <UBadge
            v-if="diff?.delta?.keystoneChanged"
            color="primary"
            variant="soft"
            size="sm"
          >
            Changed
          </UBadge>
          <span
            v-else-if="hasBothSides"
            class="text-xs text-dimmed"
          >Unchanged</span>
        </div>
        <div class="flex justify-center overflow-hidden">
          <ChampionCoreRunes
            v-if="fromSide?.runePage && runeTree"
            :page="fromSide.runePage"
            :tree="runeTree"
            :keystone-size="35"
          />
          <span
            v-else
            class="py-2 text-sm text-muted"
          >No data</span>
        </div>
        <div class="flex justify-center overflow-hidden">
          <ChampionCoreRunes
            v-if="toSide?.runePage && runeTree"
            :page="toSide.runePage"
            :tree="runeTree"
            :keystone-size="35"
          />
          <span
            v-else
            class="py-2 text-sm text-muted"
          >No data</span>
        </div>

        <!-- Skill order — ChampionCoreSkillOrder self-titles "Skill order". -->
        <div class="col-span-2 flex items-center justify-between border-t border-default/60 pt-3">
          <span class="text-xs font-medium uppercase tracking-wide text-muted">Skill order</span>
          <UBadge
            v-if="diff?.delta?.skillOrderChanged"
            color="primary"
            variant="soft"
            size="sm"
          >
            Changed
          </UBadge>
          <span
            v-else-if="hasBothSides"
            class="text-xs text-dimmed"
          >Unchanged</span>
        </div>
        <div class="flex justify-center">
          <ChampionCoreSkillOrder
            v-if="championStatic"
            :skill-order="fromSide?.skillOrder ?? null"
            :champion-static="championStatic"
          />
          <span
            v-else
            class="py-2 text-sm text-muted"
          >No data</span>
        </div>
        <div class="flex justify-center">
          <ChampionCoreSkillOrder
            v-if="championStatic"
            :skill-order="toSide?.skillOrder ?? null"
            :champion-static="championStatic"
          />
          <span
            v-else
            class="py-2 text-sm text-muted"
          >No data</span>
        </div>
      </div>

      <p
        v-if="!hasBothSides"
        class="text-center text-xs text-muted"
      >
        No data for this champion on one of the selected patches — pick another patch to compare.
      </p>
      <p
        v-else-if="noChanges"
        class="text-center text-xs text-muted"
      >
        No notable build, rune or skill changes between these patches.
      </p>
    </div>
  </SectionCard>
</template>
