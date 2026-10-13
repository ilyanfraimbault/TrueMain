<script setup lang="ts" generic="T extends { games: number, pickRate: number, winRate: number }">
// One category of the build variations: a row per option, its icons on the left
// (the default slot) and its rate badge on the right.
defineProps<{
  title: string
  options: T[]
  /** The row key, which each category builds from its own option shape. */
  optionKey: (option: T, index: number) => string
  pending?: boolean
}>()

defineSlots<{ default: (props: { option: T, index: number }) => unknown }>()
</script>

<template>
  <SectionCard
    :level="2"
    :title="title"
    class="grow shrink basis-64 min-w-0"
  >
    <ul class="space-y-2">
      <li
        v-for="(option, index) in options"
        :key="optionKey(option, index)"
        class="flex items-center justify-between gap-3"
      >
        <slot
          :option="option"
          :index="index"
        />
        <RateBadge
          :games="option.games"
          :pick-rate="option.pickRate"
          :win-rate="option.winRate"
          :pending="pending"
        />
      </li>
    </ul>
  </SectionCard>
</template>
