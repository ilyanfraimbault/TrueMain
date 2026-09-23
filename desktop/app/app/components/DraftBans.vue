<script setup lang="ts">
/**
 * One side's bans, at the top of its team. Padded to five so the row keeps its
 * shape while the ban phase runs; a ban is the portrait in grey, the way the
 * client draws it.
 */
const props = defineProps<{ bans: number[], mirrored?: boolean }>()

const { nameOf, portraitOf } = useChampionStatics()

const slots = computed(() => Array.from({ length: 5 }, (_, index) => props.bans[index] ?? null))
</script>

<template>
  <div class="flex items-center gap-1" :class="mirrored && 'flex-row-reverse'">
    <template v-for="(champion, index) in slots" :key="index">
      <img
        v-if="champion !== null && portraitOf(champion)"
        :src="portraitOf(champion)!"
        :alt="nameOf(champion)"
        :title="nameOf(champion)"
        class="size-6 rounded-md object-cover opacity-55 grayscale"
      >
      <span v-else class="size-6 rounded-md bg-white/[0.04] ring-1 ring-inset ring-white/5" />
    </template>
  </div>
</template>
