<script setup lang="ts">
defineProps<{
  /** Mirrors ProfileHeader's rail layout so the swap doesn't reflow the rail. */
  stacked?: boolean
}>()
</script>

<template>
  <section
    class="flex items-center gap-4"
    :class="stacked ? 'xl:flex-col xl:items-start xl:gap-3' : undefined"
    aria-hidden="true"
  >
    <USkeleton class="size-20 shrink-0 rounded-lg" />
    <div class="flex flex-col gap-1">
      <!-- Title row, follow star included, so resolving the profile doesn't
           shove the rest of the rail down. -->
      <div class="flex items-center gap-2">
        <USkeleton class="h-7 w-40" />
        <USkeleton class="size-7 rounded-md" />
      </div>
      <div class="flex gap-2">
        <USkeleton class="h-4 w-14 rounded-full" />
        <USkeleton class="h-4 w-20" />
      </div>
    </div>
  </section>
</template>
