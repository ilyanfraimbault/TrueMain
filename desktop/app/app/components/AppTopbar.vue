<script setup lang="ts">
/**
 * The bar over the content: history, a champion search (⌘K), and the patch the
 * numbers are from. The app never *opens* on the search — it opens on the
 * player — but a champion is always two keystrokes away.
 */
const router = useRouter()
const { patch } = useTierList()
const open = ref(false)

defineShortcuts({
  meta_k: () => (open.value = !open.value),
})
</script>

<template>
  <header class="flex items-center gap-2 pl-3 pr-4">
    <div class="flex items-center gap-0.5">
      <UButton icon="i-lucide-chevron-left" color="neutral" variant="ghost" size="sm" aria-label="Back" @click="router.back()" />
      <UButton icon="i-lucide-chevron-right" color="neutral" variant="ghost" size="sm" aria-label="Forward" @click="router.forward()" />
    </div>

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

    <span v-if="patch" class="stat-label shrink-0">Patch {{ patch }}</span>

    <ChampionSearch v-model:open="open" />
  </header>
</template>
