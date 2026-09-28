<script setup lang="ts">
/**
 * The bar over the content: a champion search (⌘K), and nothing else. No
 * back/forward arrows and no patch label — browser chrome that made the window
 * read as a web page; the sidebar is how the app is navigated. The app never
 * *opens* on the search — it opens on the player — but a champion is always
 * two keystrokes away.
 */
const open = ref(false)

defineShortcuts({
  meta_k: () => (open.value = !open.value),
})
</script>

<template>
  <header class="flex items-center px-4">
    <button
      type="button"
      class="mx-auto flex h-8 w-full max-w-md items-center gap-2 rounded-lg border border-default bg-elevated px-3 text-sm text-dimmed transition-colors hover:border-accented hover:text-muted"
      @click="open = true"
    >
      <UIcon name="i-lucide-search" class="size-4" />
      <span>Search a champion…</span>
      <span class="ml-auto flex items-center gap-0.5">
        <UKbd value="meta" size="sm" />
        <UKbd value="K" size="sm" />
      </span>
    </button>

    <ChampionSearch v-model:open="open" />
  </header>
</template>
