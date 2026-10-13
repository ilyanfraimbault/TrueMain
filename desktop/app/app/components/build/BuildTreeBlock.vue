<script setup lang="ts">
import type { StaticItemData } from '#shared/types/static-data'
import type { BuildOption } from '~/types/build'
import type { ItemSetSubject } from '~/composables/useItemSetImport'

/**
 * The site's tree, drawn smaller and tighter to fit the pane. It is the build path too,
 * item by item; a build with no branch to draw (a true main's thin sample) states the path instead.
 * The item set import button sits in its corner (#1908).
 */
defineProps<{
  build: BuildOption
  items: Record<number, StaticItemData>
  /** What the item set button imports; no button without one. */
  itemSetSubject: ItemSetSubject | null
}>()
</script>

<template>
  <div class="relative">
    <BuildItemSetImport v-if="itemSetSubject" :subject="itemSetSubject" class="absolute right-0 top-0 z-10" />
    <ChampionBuildPanelBuildTree
      v-if="build.buildTree.length > 0"
      :tree="build.buildTree"
      :first-item-id="build.firstItemId"
      :item-path="build.core.itemPath?.itemIds ?? []"
      :items-map="items"
      :item-size="28"
      :h-gap="10"
      :v-gap="20"
    />
    <div v-else-if="build.core.itemPath?.itemIds.length" class="flex justify-center">
      <ChampionCoreBuildPath :path="build.core.itemPath" :items-map="items" />
    </div>
  </div>
</template>
