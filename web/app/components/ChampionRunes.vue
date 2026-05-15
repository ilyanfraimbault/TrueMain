<script setup lang="ts">
import type { RunePageOptionResponse } from '~~/shared/types/champions'
import type { ChampionStaticData } from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'

const props = defineProps<{
  pages: RunePageOptionResponse[]
  championStatic: ChampionStaticData
}>()

function perkIcon(id: number): string {
  return id > 0 ? (props.championStatic.perks[id]?.iconUrl ?? '') : ''
}

function perkStyleIcon(id: number): string {
  return id > 0 ? (props.championStatic.perkStyles[id]?.iconUrl ?? '') : ''
}
</script>

<template>
  <section>
    <ul class="space-y-3">
      <li
        v-for="(page, index) in pages"
        :key="`rune-${index}`"
        class="flex flex-wrap items-center gap-2"
      >
        <NuxtImg
          v-if="perkStyleIcon(page.primaryStyleId)"
          :src="perkStyleIcon(page.primaryStyleId)"
          :alt="`Style ${page.primaryStyleId}`"
          width="24"
          height="24"
          class="size-6"
        />
        <NuxtImg
          v-if="perkIcon(page.primaryKeystoneId)"
          :src="perkIcon(page.primaryKeystoneId)"
          :alt="`Keystone ${page.primaryKeystoneId}`"
          width="40"
          height="40"
          class="size-10 rounded-full"
        />
        <template
          v-for="perkId in [page.primaryPerk1Id, page.primaryPerk2Id, page.primaryPerk3Id]"
          :key="`p-${index}-${perkId}`"
        >
          <NuxtImg
            v-if="perkIcon(perkId)"
            :src="perkIcon(perkId)"
            :alt="`Perk ${perkId}`"
            width="28"
            height="28"
            class="size-7 rounded-full"
          />
        </template>
        <span class="text-sm text-muted">|</span>
        <NuxtImg
          v-if="perkStyleIcon(page.secondaryStyleId)"
          :src="perkStyleIcon(page.secondaryStyleId)"
          :alt="`Style ${page.secondaryStyleId}`"
          width="24"
          height="24"
          class="size-6"
        />
        <template
          v-for="perkId in [page.secondaryPerk1Id, page.secondaryPerk2Id]"
          :key="`s-${index}-${perkId}`"
        >
          <NuxtImg
            v-if="perkIcon(perkId)"
            :src="perkIcon(perkId)"
            :alt="`Perk ${perkId}`"
            width="28"
            height="28"
            class="size-7 rounded-full"
          />
        </template>
        <span class="ml-auto text-sm text-muted">
          {{ formatPercentage(page.playRate) }} · {{ formatPercentage(page.winRate) }} WR · {{ page.games }}g
        </span>
      </li>
      <li
        v-if="!pages.length"
        class="text-sm text-muted"
      >
        No data
      </li>
    </ul>
  </section>
</template>
