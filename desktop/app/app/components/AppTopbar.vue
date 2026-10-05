<script setup lang="ts">
/**
 * The bar over the content: a champion search (⌘K), and nothing else. No
 * back/forward arrows and no patch label — browser chrome that made the window
 * read as a web page; the sidebar is how the app is navigated. The app never
 * *opens* on the search — it opens on the player — but a champion is always
 * two keystrokes away. In a bar too narrow for it (a compact window, #1914)
 * the field folds into its icon; the shortcut stays.
 */
const open = ref(false)

defineShortcuts({
  meta_k: () => (open.value = !open.value),
})
</script>

<template>
  <header class="@container flex items-center px-4">
    <button
      type="button"
      aria-label="Search a champion"
      class="mx-auto flex h-8 w-full max-w-md items-center gap-2 rounded-lg border border-default bg-elevated px-3 text-sm text-dimmed transition-colors hover:border-accented hover:text-muted @max-sm:ml-auto @max-sm:mr-0 @max-sm:w-8 @max-sm:justify-center @max-sm:px-0"
      @click="open = true"
    >
      <UIcon name="i-lucide-search" class="size-4 shrink-0" />
      <span class="@max-sm:hidden">Search a champion…</span>
      <span class="ml-auto flex items-center gap-0.5 @max-sm:hidden">
        <UKbd value="meta" size="sm" />
        <UKbd value="K" size="sm" />
      </span>
    </button>

    <ChampionSearch v-model:open="open" />
  </header>
</template>
