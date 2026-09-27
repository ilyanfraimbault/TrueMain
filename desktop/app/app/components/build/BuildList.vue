<script setup lang="ts">
import type { BuildOption } from '~/types/build'

/**
 * The builds a view can switch between, one row each: the one computed for
 * this draft first (when there is a draft), then the champion's lane builds,
 * named by their keystone and first completed item — the pair the site groups
 * builds by.
 */
const props = defineProps<{
  options: BuildOption[]
  pending: boolean
  /** What the draft's own build is called: "This draft", "vs Zed". */
  draftLabel?: string
}>()

const selected = defineModel<string | null>({ default: null })

const { item, perk } = useBuildStatics()

function label(option: BuildOption) {
  if (option.key === 'draft') return option.standard ? 'Standard build' : (props.draftLabel ?? 'This draft')
  return item(option.firstItemId)?.name || 'Build'
}
</script>

<template>
  <div class="flex flex-col gap-1">
    <h3 class="px-2 pb-1 stat-label">Builds</h3>

    <button
      v-for="option in options"
      :key="option.key"
      type="button"
      class="relative flex items-center gap-2 rounded-lg px-2 py-2 text-left transition-colors"
      :class="selected === option.key ? 'bg-accented' : 'hover:bg-elevated'"
      @click="selected = option.key"
    >
      <span v-if="selected === option.key" class="absolute inset-y-2 left-0 w-0.5 rounded-full bg-primary" />
      <div class="min-w-0 flex-1 leading-tight">
        <p class="flex items-center gap-1.5 truncate text-[13px] font-semibold" :class="selected === option.key ? 'text-highlighted' : 'text-default'">
          <UIcon v-if="option.key === 'draft'" name="i-lucide-sparkles" class="size-3.5 shrink-0 text-primary" />
          <GameIcon v-else-if="option.keystoneId" :source="perk(option.keystoneId)" size="size-4" round class="shrink-0" />
          <span class="truncate">{{ label(option) }}</span>
        </p>
        <p class="mt-0.5 text-[11px] tabular-nums text-dimmed">{{ option.games.toLocaleString('en-US') }} games</p>
      </div>
      <div class="flex shrink-0 -space-x-1">
        <GameIcon v-for="(id, index) in (option.core.itemPath?.itemIds ?? [option.firstItemId]).slice(0, 3)" :key="index" :source="item(id)" size="size-6" class="ring-1 ring-ink-950" />
      </div>
      <span class="w-10 shrink-0 text-right text-xs font-semibold tabular-nums" :class="winRateTone(option.winRate)">
        {{ option.winRate === null ? '—' : `${Math.round(option.winRate * 100)}%` }}
      </span>
    </button>

    <div v-if="pending && !options.length" class="flex flex-col gap-1.5 px-2">
      <USkeleton v-for="index in 3" :key="index" class="h-10 w-full" />
    </div>
    <p v-else-if="!options.length" class="px-2 text-xs text-dimmed">No build recorded on this lane</p>
  </div>
</template>
