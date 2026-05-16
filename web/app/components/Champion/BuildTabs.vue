<script setup lang="ts">
import type { ChampionBuild } from '~~/shared/types/champions'
import type { ChampionStaticData, RuneTreeResponse } from '~~/shared/types/static-data'

const props = defineProps<{
  builds: ChampionBuild[]
  championStatic: ChampionStaticData
  runeTree: RuneTreeResponse | null
}>()

const items = computed(() =>
  props.builds.map((build, index) => ({
    value: `build-${index}`,
    slot: `build-${index}` as const,
    build,
  })),
)

function itemIcon(id: number): string {
  return props.championStatic.items[id]?.iconUrl ?? ''
}

function itemName(id: number): string {
  return props.championStatic.items[id]?.name ?? `Item ${id}`
}

function perkIcon(id: number): string {
  return props.runeTree?.perks[id]?.iconUrl ?? ''
}

function perkName(id: number): string {
  return props.runeTree?.perks[id]?.name ?? `Perk ${id}`
}

function styleIcon(id: number): string {
  return props.runeTree?.perkStyles[id]?.iconUrl ?? ''
}

function styleName(id: number): string {
  return props.runeTree?.perkStyles[id]?.name ?? `Style ${id}`
}
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
        <NuxtImg
          v-if="itemIcon(item.build.firstItemId)"
          :src="itemIcon(item.build.firstItemId)"
          :alt="itemName(item.build.firstItemId)"
          :title="itemName(item.build.firstItemId)"
          width="28"
          height="28"
          class="size-7 rounded"
        />
        <div
          v-if="perkIcon(item.build.primaryKeystoneId)"
          class="relative size-7"
        >
          <NuxtImg
            :src="perkIcon(item.build.primaryKeystoneId)"
            :alt="perkName(item.build.primaryKeystoneId)"
            :title="perkName(item.build.primaryKeystoneId)"
            width="28"
            height="28"
            class="size-7 rounded-full"
          />
          <NuxtImg
            v-if="item.build.core.runePage && styleIcon(item.build.core.runePage.secondaryStyleId)"
            :src="styleIcon(item.build.core.runePage.secondaryStyleId)"
            :alt="styleName(item.build.core.runePage.secondaryStyleId)"
            :title="styleName(item.build.core.runePage.secondaryStyleId)"
            width="16"
            height="16"
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
