<script setup lang="ts">
// Mirrors the row layout in pages/champions/index.vue so placeholder rows keep
// the same column widths, gaps and heights as the real rows and the data
// swaps in without any layout shift. Same modelling as LeaderboardRowSkeleton:
// the real row is `dense`, i.e. it has no vertical padding left and is sized by
// its 36px champion icon, so the placeholder takes that height explicitly.
</script>

<template>
  <div
    class="surface flex h-9 items-center gap-4 rounded-lg px-1.5"
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

    <!-- Rates: WR + PR + BR -->
    <div class="flex shrink-0 items-center gap-5">
      <div v-for="i in 3" :key="`rate-${i}`" class="flex min-w-[3rem] flex-col items-center gap-1">
        <USkeleton class="h-4 w-8" />
        <USkeleton class="h-3 w-6" />
      </div>
    </div>
  </div>
</template>
