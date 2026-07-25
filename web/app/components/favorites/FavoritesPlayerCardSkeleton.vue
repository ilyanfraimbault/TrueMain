<script setup lang="ts">
// Placeholder for one favorites card. Mirrors FavoritesPlayerCard's header +
// match-row rhythm so revealing the real list after the localStorage read
// doesn't shift the page.
withDefaults(defineProps<{ matchCount?: number }>(), { matchCount: 3 })
</script>

<template>
  <section class="glass overflow-hidden rounded-lg" aria-hidden="true">
    <div class="flex items-center gap-3 border-b border-default/60 px-3 py-2.5">
      <USkeleton class="size-10 shrink-0 rounded" />
      <div class="flex flex-col gap-1.5">
        <USkeleton class="h-4 w-40" />
        <USkeleton class="h-3 w-28" />
      </div>
      <USkeleton class="ml-auto size-7 shrink-0 rounded-md" />
    </div>
    <div class="space-y-1.5 p-2">
      <MatchRowSkeleton v-for="i in matchCount" :key="`fav-card-skel-${i}`" />
    </div>
  </section>
</template>
