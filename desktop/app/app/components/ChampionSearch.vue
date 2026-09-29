<script setup lang="ts">
import type { CommandPaletteGroup, CommandPaletteItem } from '@nuxt/ui'

/**
 * The champion search behind ⌘K: every champion Data Dragon lists, opening its
 * builds. Local — the list is already in memory for the draft.
 */
const open = defineModel<boolean>('open', { default: false })

const { champions, portraitOf } = useChampionStatics()

const groups = computed<CommandPaletteGroup<CommandPaletteItem>[]>(() => [{
  id: 'champions',
  label: 'Champions',
  items: [...champions.value.values()]
    .sort((a, b) => a.name.localeCompare(b.name))
    .map(champion => ({
      id: champion.id,
      label: champion.name,
      avatar: { src: portraitOf(champion.id) ?? undefined, alt: champion.name },
      to: `/champions/${champion.id}`,
      onSelect: () => (open.value = false),
    })),
}])
</script>

<template>
  <UModal v-model:open="open" :ui="{ content: 'max-w-lg' }">
    <template #content>
      <UCommandPalette
        :groups="groups"
        placeholder="Search a champion…"
        class="h-96"
        close
        @update:open="open = $event"
      />
    </template>
  </UModal>
</template>
