<script setup lang="ts">
import type { BuildOption } from '~/types/build'

/**
 * The builds a view can switch between, one row each, drawn as icons alone:
 * the keystone with the secondary tree as its badge (the site's own pairing,
 * `leaderboard/ChampionBuild`), the first three items, the win rate over the
 * games. An item's name spelled out said less than its icon and took the
 * width the build itself needs. The build computed for this draft comes first,
 * under its own heading ("vs Zed"), then the champion's lane builds.
 */
const props = defineProps<{
  options: BuildOption[]
  pending: boolean
  /** What the draft's own build is called when it faces no one yet: "This draft". */
  draftLabel?: string
  /** The champion the draft's build faces on its lane: its heading is "VS" and that champion's icon. */
  draftOpponent?: number | null
}>()

const selected = defineModel<string | null>({ default: null })

const { items, runeTree } = useStaticData()
const { nameOf, portraitOf } = useChampionStatics()
const { perk, perkStyle, item } = useBuildResolvers(runeTree, items)

const draftOption = computed(() => props.options.find(option => option.key === 'draft') ?? null)
const laneOptions = computed(() => props.options.filter(option => option.key !== 'draft'))

const groups = computed(() => [
  ...(draftOption.value
    ? [{
        label: draftOption.value.standard ? 'Standard build' : (props.draftLabel ?? 'This draft'),
        opponent: draftOption.value.standard ? null : props.draftOpponent ?? null,
        options: [draftOption.value],
      }]
    : []),
  ...(laneOptions.value.length ? [{ label: draftOption.value ? 'On the lane' : 'Builds', opponent: null, options: laneOptions.value }] : []),
])
</script>

<template>
  <div class="flex flex-col gap-3">
    <div v-for="group in groups" :key="group.label" class="flex flex-col gap-0.5">
      <h3 v-if="group.opponent" class="flex items-center gap-1.5 px-2 pb-1 stat-label">
        VS
        <img v-if="portraitOf(group.opponent)" :src="portraitOf(group.opponent)!" :alt="nameOf(group.opponent)" :title="nameOf(group.opponent)" class="size-4 rounded">
      </h3>
      <h3 v-else class="truncate px-2 pb-1 stat-label">{{ group.label }}</h3>

      <button
        v-for="option in group.options"
        :key="option.key"
        type="button"
        class="relative flex items-center gap-2.5 rounded-lg px-2 py-1.5 text-left transition-colors"
        :class="selected === option.key ? 'bg-accented' : 'hover:bg-elevated'"
        @click="selected = option.key"
      >
        <span v-if="selected === option.key" class="absolute inset-y-2 left-0 w-0.5 rounded-full bg-primary" />

        <!-- A gap between the icons, not an overlap: the site's tooltip icons sit in a
             `display: contents` wrapper until first hovered, which a `space-x` margin skips. -->
        <div class="relative size-[26px] shrink-0">
          <GameTooltipPerkIcon
            v-if="option.keystoneId"
            :perk="perk(option.keystoneId)"
            :width="26"
            :height="26"
            class="size-[26px] rounded-full"
          />
          <GameTooltipPerkStyleIcon
            v-if="option.keystoneId && option.core.runePage?.secondaryStyleId"
            :style="perkStyle(option.core.runePage.secondaryStyleId)"
            :width="13"
            :height="13"
            class="absolute -bottom-1 -right-1.5 size-[13px]"
          />
        </div>

        <div class="flex shrink-0 gap-0.5">
          <GameTooltipItemIcon
            v-for="(id, index) in (option.core.itemPath?.itemIds ?? [option.firstItemId]).slice(0, 3)"
            :key="index"
            :item="item(id)"
            :width="24"
            :height="24"
            class="size-6 shrink-0 rounded"
          />
        </div>

        <div class="ml-auto shrink-0 text-right leading-tight">
          <p class="text-xs font-semibold tabular-nums" :class="winRateTone(option.winRate)">
            {{ option.winRate === null ? '—' : `${Math.round(option.winRate * 100)}%` }}
          </p>
          <p class="text-[10px] tabular-nums text-dimmed">{{ option.games.toLocaleString('en-US') }} games</p>
        </div>
      </button>
    </div>

    <div v-if="pending && !options.length" class="flex flex-col gap-1.5 px-2">
      <USkeleton v-for="index in 3" :key="index" class="h-9 w-full" />
    </div>
    <p v-else-if="!options.length" class="px-2 text-xs text-dimmed">No build recorded on this lane</p>
  </div>
</template>
