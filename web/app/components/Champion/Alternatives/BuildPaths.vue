<script setup lang="ts">
import type { ChampionBuildTreeNodeResponse, ChampionBuildTreeResponse } from '~~/shared/types/champions'
import type { ChampionStaticData, StaticItemData } from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'

const props = defineProps<{
  buildTree: ChampionBuildTreeResponse | null
  championStatic: ChampionStaticData
}>()

type BuildPath = { items: StaticItemData[], pickRate: number, games: number, winRate: number }

function followBranch(node: ChampionBuildTreeNodeResponse, depth = 0): ChampionBuildTreeNodeResponse[] {
  if (depth > 6 || !node.children.length) return [node]
  const next = [...node.children].sort((a, b) => b.pickRate - a.pickRate)[0]
  return next ? [node, ...followBranch(next, depth + 1)] : [node]
}

const topBuildPaths = computed<BuildPath[]>(() => {
  const roots = props.buildTree?.build ?? []
  return [...roots]
    .sort((a, b) => b.pickRate - a.pickRate)
    .slice(0, 3)
    .map((root): BuildPath => {
      const chain = followBranch(root)
      const last = chain[chain.length - 1] ?? root
      return {
        items: chain
          .map(n => props.championStatic.items[n.itemId])
          .filter((item): item is StaticItemData => Boolean(item)),
        pickRate: root.pickRate,
        games: last.games,
        winRate: last.wins / Math.max(last.games, 1),
      }
    })
})
</script>

<template>
  <div>
    <ul class="space-y-2">
      <li
        v-for="(path, pathIndex) in topBuildPaths"
        :key="`path-${pathIndex}`"
        class="flex flex-wrap items-center gap-1"
      >
        <span class="text-sm tabular-nums text-muted">
          {{ formatPercentage(path.pickRate) }}
        </span>
        <template
          v-for="(item, index) in path.items"
          :key="`pathitem-${pathIndex}-${item.id}-${index}`"
        >
          <NuxtImg
            :src="item.iconUrl"
            :alt="item.name"
            :title="item.name"
            width="32"
            height="32"
            class="size-8 rounded"
          />
          <UIcon
            v-if="index < path.items.length - 1"
            name="i-lucide-chevron-right"
            class="size-4 text-dimmed"
          />
        </template>
        <span class="ml-auto text-sm text-muted">
          {{ formatPercentage(path.winRate) }} WR · {{ path.games }} games
        </span>
      </li>
      <li
        v-if="!topBuildPaths.length"
        class="text-sm text-muted"
      >
        No data
      </li>
    </ul>
  </div>
</template>
