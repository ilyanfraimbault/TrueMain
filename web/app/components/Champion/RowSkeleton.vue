<script setup lang="ts">
// Mirrors the row layout in pages/champions/index.vue so placeholder rows keep
// the same column widths, gaps and heights as the real rows and the data
// swaps in without any layout shift. Same modelling as LeaderboardRowSkeleton.
</script>

<template>
  <div
    class="flex items-center gap-4 rounded-lg border border-default/40 bg-elevated/20 px-3 py-2.5"
    aria-hidden="true"
  >
    <!-- Champion -->
    <div class="flex min-w-[10rem] items-center gap-2">
      <USkeleton class="size-9 rounded" />
      <USkeleton class="h-4 w-24" />
    </div>

    <!-- Position -->
    <USkeleton class="size-[22px] shrink-0 rounded" />

    <!-- Runes: keystone circle -->
    <USkeleton class="size-7 shrink-0 rounded-full" />

    <!-- Build path -->
    <div class="flex shrink-0 items-center gap-1">
      <USkeleton v-for="i in 5" :key="`bp-${i}`" class="size-7 rounded" />
    </div>

    <!-- Tier -->
    <div class="ml-auto flex min-w-[3rem] shrink-0 items-center justify-center">
      <USkeleton class="h-5 w-8 rounded" />
    </div>

    <!-- Rates: WR + PR -->
    <div class="flex shrink-0 items-center gap-5">
      <div class="flex min-w-[3rem] flex-col items-center gap-1">
        <USkeleton class="h-5 w-8" />
        <USkeleton class="h-3 w-6" />
      </div>
      <div class="flex min-w-[3rem] flex-col items-center gap-1">
        <USkeleton class="h-5 w-8" />
        <USkeleton class="h-3 w-6" />
      </div>
    </div>
  </div>
</template>
