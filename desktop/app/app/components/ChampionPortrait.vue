<script setup lang="ts">
const props = withDefaults(defineProps<{
  /** `null` is a slot nobody has filled yet — drawn as unknown, never as an id. */
  championId?: number | null
  size?: 'sm' | 'md' | 'lg'
  dimmed?: boolean
  /** The "?" in an empty slot. Off where an empty slot is not a question — a ban not cast. */
  emptyIcon?: boolean
}>(), { championId: null, size: 'md', dimmed: false, emptyIcon: true })

const { nameOf, portraitOf } = useChampionStatics()

const source = computed(() => (props.championId ? portraitOf(props.championId) : null))
const label = computed(() => (props.championId ? nameOf(props.championId) : 'Not picked yet'))

const box = computed(() => ({ sm: 'size-8', md: 'size-12', lg: 'size-16' }[props.size]))
</script>

<template>
  <div
    :class="[box, dimmed && 'opacity-40']"
    class="relative shrink-0 overflow-hidden rounded-lg bg-ink-900 ring-1 ring-default"
    :title="label"
  >
    <img v-if="source" :src="source" :alt="label" class="size-full object-cover" loading="lazy">
    <div v-else class="flex size-full items-center justify-center rounded-lg border border-dashed border-accented text-dimmed" aria-label="Not picked yet">
      <UIcon v-if="emptyIcon" name="i-lucide-help-circle" class="size-1/2 opacity-60" />
    </div>
  </div>
</template>
