<script setup lang="ts">
// Search as a tile: the field is the real trigger (its own `AppSearch`, the
// palette the header's ⌘K opens too), and a click anywhere else on the tile is
// handed to it. The ⌘ K keycaps press down as the cursor arrives.
defineProps<{ index: number }>()

const tile = ref<{ $el: HTMLElement } | null>(null)

function openFromTile(event: MouseEvent) {
  const root = tile.value?.$el
  const trigger = root?.querySelector<HTMLButtonElement>('.g-search button')
  if (!trigger || trigger.contains(event.target as Node)) return
  trigger.click()
}
</script>

<template>
  <DesignGTile
    ref="tile"
    area="search"
    :index="index"
    label="Search"
    icon="i-lucide-search"
    class="g-searchtile"
    @click="openFromTile"
  >
    <span class="g-searchtile__hint">Any champion, any truemain.</span>
    <AppSearch
      variant="field"
      size="lg"
      placeholder="Search a champion or player…"
      class="g-search"
    />
  </DesignGTile>
</template>
