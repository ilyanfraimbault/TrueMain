<script setup lang="ts">
/**
 * The gap between two item values, under a chevron toward the side ahead — in
 * that side's colour, ours blue on the left, theirs red on the right. Even
 * reads as a grey dash.
 */
const props = defineProps<{ ours: number, theirs: number, large?: boolean }>()

const gap = computed(() => props.ours - props.theirs)
</script>

<template>
  <div class="flex min-w-14 flex-col items-center leading-none">
    <UIcon
      v-if="gap !== 0"
      :name="gap > 0 ? 'i-lucide-chevron-left' : 'i-lucide-chevron-right'"
      :class="[gap > 0 ? 'text-ally' : 'text-enemy', large ? 'size-3.5' : 'size-3']"
    />
    <UIcon v-else name="i-lucide-minus" class="text-dimmed" :class="large ? 'size-3.5' : 'size-3'" />
    <span
      class="mt-0.5 tabular-nums font-semibold"
      :class="[large ? 'text-xs' : 'text-[10px]', gap > 0 ? 'text-ally' : gap < 0 ? 'text-enemy' : 'text-dimmed']"
    >{{ Math.abs(gap).toLocaleString('en-US') }}</span>
  </div>
</template>
