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
  <!-- Single card wrapping the whole tab-dependent section. The tab bar is
       docked to the top edge (body padding removed at every breakpoint — a
       bare `p-0` loses to the theme's `sm:p-6` in tw-merge — list background
       and rounding stripped, full-width from the horizontal orientation) with
       a divider separating it from the panel content, which carries the
       card's own padding via the `content` slot. The active pill is a dark
       overlay instead of the theme's white `bg-inverted`. -->
  <UCard
    v-if="items.length"
    :ui="{ body: 'p-0 sm:p-0' }"
  >
    <UTabs
      :items="items"
      :default-value="items[0]?.value"
      variant="pill"
      color="neutral"
      size="md"
      class="w-full"
      :unmount-on-hide="false"
      :ui="{
        list: 'rounded-none border-b border-default bg-transparent p-1.5',
        indicator: 'rounded-lg bg-black/30 shadow-none inset-y-1.5',
        trigger: 'flex-1 gap-1.5 py-2.5 data-[state=active]:text-highlighted',
        content: 'p-3 sm:p-4',
      }"
    >
      <template #leading="{ item }">
        <div class="flex items-center gap-1.5">
          <GameTooltipItemIcon
            v-if="itemsMap[item.build.firstItemId]"
            :item="itemsMap[item.build.firstItemId]"
            :width="24"
            :height="24"
            class="size-6 rounded"
          />
          <div
            v-if="runeTree?.perks[item.build.primaryKeystoneId]"
            class="relative size-6"
          >
            <GameTooltipPerkIcon
              :perk="runeTree?.perks[item.build.primaryKeystoneId] ?? null"
              :width="24"
              :height="24"
              class="size-6 rounded-full"
            />
            <GameTooltipPerkStyleIcon
              v-if="item.build.core.runePage && runeTree?.perkStyles[item.build.core.runePage.secondaryStyleId]"
              :style="runeTree?.perkStyles[item.build.core.runePage.secondaryStyleId] ?? null"
              :width="14"
              :height="14"
              class="absolute -bottom-0.5 -right-1 size-3.5"
            />
          </div>
        </div>
      </template>
      <template #default="{ item, index }">
        <!-- The leading item/rune icons already carry their own accessible
             names (see GameTooltip*Icon `alt`), but without this prefix the
             tab's full accessible name reads as just those names + a bare
             percentage — ambiguous with several builds open. sr-only text
             disambiguates without changing the visible design. -->
        <span class="sr-only">Build {{ index + 1 }}, </span>
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
  </UCard>
  <p
    v-else
    class="text-sm text-muted"
  >
    No build data
  </p>
</template>
