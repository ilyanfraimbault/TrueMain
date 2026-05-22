<script setup lang="ts">
import type { ChampionBuild } from '~~/shared/types/champions'
import type {
  ChampionStaticData,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'

const props = defineProps<{
  builds: ChampionBuild[]
  championStatic: ChampionStaticData
  itemsMap: Record<number, StaticItemData>
  summonersMap: Record<number, StaticSummonerSpellData>
  runeTree: RuneTreeResponse | null
}>()

const items = computed(() =>
  props.builds.map((build, index) => ({
    value: `build-${index}`,
    slot: `build-${index}` as const,
    build,
  })),
)
</script>

<template>
  <UTabs
    v-if="items.length"
    :items="items"
    :default-value="items[0]?.value"
    variant="link"
    class="w-full"
    :unmount-on-hide="false"
    :ui="{ trigger: 'flex-1 gap-2' }"
  >
    <template #leading="{ item }">
      <div class="flex items-center gap-2">
        <GameTooltipItemIcon
          v-if="itemsMap[item.build.firstItemId]"
          :item="itemsMap[item.build.firstItemId]"
          :width="28"
          :height="28"
          class="size-7 rounded"
        />
        <div
          v-if="runeTree?.perks[item.build.primaryKeystoneId]"
          class="relative size-7"
        >
          <GameTooltipPerkIcon
            :perk="runeTree?.perks[item.build.primaryKeystoneId] ?? null"
            :width="28"
            :height="28"
            class="size-7 rounded-full"
          />
          <GameTooltipPerkStyleIcon
            v-if="item.build.core.runePage && runeTree?.perkStyles[item.build.core.runePage.secondaryStyleId]"
            :style="runeTree?.perkStyles[item.build.core.runePage.secondaryStyleId] ?? null"
            :width="16"
            :height="16"
            class="absolute -bottom-1 -right-2 size-4"
          />
        </div>
      </div>
    </template>
    <template #default="{ item }">
      <span class="text-xs tabular-nums text-muted">
        {{ (item.build.pickRate * 100).toFixed(0) }}%
      </span>
    </template>
    <template
      v-for="item in items"
      :key="item.value"
      #[item.slot]
    >
      <ChampionBuildPanel
        :build="item.build"
        :champion-static="championStatic"
        :items-map="itemsMap"
        :summoners-map="summonersMap"
        :rune-tree="runeTree"
      />
    </template>
  </UTabs>
  <p
    v-else
    class="text-sm text-muted"
  >
    No build data
  </p>
</template>
