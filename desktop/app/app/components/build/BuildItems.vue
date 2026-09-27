<script setup lang="ts">
import type { BuildOption, BuildTreeNode } from '~/types/build'

/**
 * The right of a build view: summoner spells, then the items — starter, boots,
 * the core order, and what is built second and third with how often, read off
 * the build's own item tree (the share of games at that step that went on to
 * each item).
 */
const props = defineProps<{ build: BuildOption }>()

const { spell, item } = useBuildStatics()

const core = computed(() => props.build.core)
const path = computed(() => core.value.itemPath?.itemIds ?? [])

/** The most-built children of a node, in the backend's prune order (games, then id). */
const options = (nodes: BuildTreeNode[]) => nodes
  .slice()
  .sort((a, b) => b.games - a.games || a.itemId - b.itemId)
  .slice(0, 5)

const second = computed(() => options(props.build.buildTree))
/** Third items follow the core's second item — the branch the build path takes. */
const third = computed(() => {
  const next = props.build.buildTree.find(node => node.itemId === path.value[1])
  return next ? options(next.children) : []
})

const percent = (value: number) => `${Math.round(value * 100)}%`
</script>

<template>
  <div class="flex flex-col gap-5 p-4">
    <BuildSection title="Summoners" :win-rate="core.summonerSpells?.winRate ?? null">
      <div class="flex gap-1.5">
        <template v-if="core.summonerSpells">
          <GameIcon :source="spell(core.summonerSpells.spell1Id)" size="size-9" />
          <GameIcon :source="spell(core.summonerSpells.spell2Id)" size="size-9" />
        </template>
      </div>
    </BuildSection>

    <div class="flex flex-col gap-4">
      <h3 class="text-sm font-semibold text-highlighted">Items</h3>

      <BuildSection title="Starting items" :win-rate="core.starterItems?.winRate ?? null" sub>
        <div class="flex gap-1.5">
          <GameIcon v-for="(id, index) in core.starterItems?.itemIds ?? []" :key="`starter-${index}`" :source="item(id)" size="size-8" />
        </div>
      </BuildSection>

      <BuildSection title="Core build order" :win-rate="core.itemPath?.winRate ?? null" sub>
        <div class="flex items-center gap-1">
          <template v-for="(id, index) in path" :key="`path-${index}`">
            <GameIcon :source="item(id)" size="size-8" />
            <UIcon v-if="index < path.length - 1" name="i-lucide-chevron-right" class="size-3.5 shrink-0 text-dimmed" />
          </template>
        </div>
      </BuildSection>

      <BuildSection title="Boots" :win-rate="core.boots?.winRate ?? null" sub>
        <div class="flex gap-1.5">
          <GameIcon v-for="(id, index) in core.boots?.itemIds ?? []" :key="`boots-${index}`" :source="item(id)" size="size-8" />
        </div>
      </BuildSection>

      <BuildSection v-if="second.length" title="Second item" sub>
        <div class="flex gap-1.5">
          <div v-for="node in second" :key="node.itemId" class="flex flex-col items-center gap-1">
            <GameIcon :source="item(node.itemId)" size="size-8" :class="node.itemId === path[1] && 'ring-1 ring-primary/70'" />
            <span class="text-[10px] tabular-nums text-dimmed">{{ percent(node.pickRate) }}</span>
          </div>
        </div>
      </BuildSection>

      <BuildSection v-if="third.length" title="Third item" sub>
        <div class="flex gap-1.5">
          <div v-for="node in third" :key="node.itemId" class="flex flex-col items-center gap-1">
            <GameIcon :source="item(node.itemId)" size="size-8" :class="node.itemId === path[2] && 'ring-1 ring-primary/70'" />
            <span class="text-[10px] tabular-nums text-dimmed">{{ percent(node.pickRate) }}</span>
          </div>
        </div>
      </BuildSection>
    </div>
  </div>
</template>
