<script setup lang="ts">
import type { RunePageOptionResponse } from '~/types/champions'
import type { StaticPerkData, StaticPerkStyleData } from '~/types/static-data'

const props = defineProps<{
  page: RunePageOptionResponse
  perks: Record<number, StaticPerkData>
  perkStyles: Record<number, StaticPerkStyleData>
}>()

function lookupPerk(id: number): StaticPerkData | null {
  return id > 0 ? (props.perks[id] ?? null) : null
}

function lookupStyle(id: number): StaticPerkStyleData | null {
  return id > 0 ? (props.perkStyles[id] ?? null) : null
}

const primaryStyle = computed(() => lookupStyle(props.page.primaryStyleId))
const secondaryStyle = computed(() => lookupStyle(props.page.secondaryStyleId))
const keystone = computed(() => lookupPerk(props.page.primaryKeystoneId))
const primaryPerks = computed(() => [
  lookupPerk(props.page.primaryPerk1Id),
  lookupPerk(props.page.primaryPerk2Id),
  lookupPerk(props.page.primaryPerk3Id)
].filter((perk): perk is StaticPerkData => perk !== null))
const secondaryPerks = computed(() => [
  lookupPerk(props.page.secondaryPerk1Id),
  lookupPerk(props.page.secondaryPerk2Id)
].filter((perk): perk is StaticPerkData => perk !== null))
const statShards = computed(() => [
  lookupPerk(props.page.statOffense),
  lookupPerk(props.page.statFlex),
  lookupPerk(props.page.statDefense)
].filter((shard): shard is StaticPerkData => shard !== null))
</script>

<template>
  <div class="flex flex-col gap-3">
    <!-- Primary tree: small style header + big keystone + 3 minor perks -->
    <div class="flex flex-col gap-2">
      <div
        v-if="primaryStyle"
        class="flex items-center gap-1.5 text-xs text-muted"
      >
        <ChampionsChampionAsyncImage
          :src="primaryStyle.iconUrl"
          :alt="primaryStyle.name"
          size-class="h-5 w-5"
          image-class="object-contain"
          width="20"
          height="20"
        />
        <span>{{ primaryStyle.name }}</span>
      </div>
      <div class="flex flex-wrap items-center gap-2">
        <UTooltip
          v-if="keystone"
          :text="keystone.name"
          :content="{ side: 'top' }"
          arrow
        >
          <ChampionsChampionAsyncImage
            :src="keystone.iconUrl"
            :alt="keystone.name"
            size-class="h-12 w-12"
            image-class="rounded-full border border-default bg-default object-contain"
            wrapper-class="rounded-full"
            width="48"
            height="48"
          />
        </UTooltip>

        <div class="flex items-center gap-1.5">
          <UTooltip
            v-for="perk in primaryPerks"
            :key="`primary-${perk.id}`"
            :text="perk.name"
            :content="{ side: 'top' }"
            arrow
          >
            <ChampionsChampionAsyncImage
              :src="perk.iconUrl"
              :alt="perk.name"
              size-class="h-9 w-9"
              image-class="rounded-full border border-default bg-default object-contain"
              wrapper-class="rounded-full"
              width="36"
              height="36"
            />
          </UTooltip>
        </div>
      </div>
    </div>

    <!-- Secondary tree -->
    <div class="flex flex-col gap-2">
      <div
        v-if="secondaryStyle"
        class="flex items-center gap-1.5 text-xs text-muted"
      >
        <ChampionsChampionAsyncImage
          :src="secondaryStyle.iconUrl"
          :alt="secondaryStyle.name"
          size-class="h-5 w-5"
          image-class="object-contain"
          width="20"
          height="20"
        />
        <span>{{ secondaryStyle.name }}</span>
      </div>
      <div class="flex items-center gap-1.5">
        <UTooltip
          v-for="perk in secondaryPerks"
          :key="`secondary-${perk.id}`"
          :text="perk.name"
          :content="{ side: 'top' }"
          arrow
        >
          <ChampionsChampionAsyncImage
            :src="perk.iconUrl"
            :alt="perk.name"
            size-class="h-9 w-9"
            image-class="rounded-full border border-default bg-default object-contain"
            wrapper-class="rounded-full"
            width="36"
            height="36"
          />
        </UTooltip>
      </div>
    </div>

    <!-- Stat shards -->
    <div class="flex items-center gap-1.5">
      <UTooltip
        v-for="shard in statShards"
        :key="`shard-${shard.id}`"
        :text="shard.name"
        :content="{ side: 'top' }"
        arrow
      >
        <ChampionsChampionAsyncImage
          :src="shard.iconUrl"
          :alt="shard.name"
          size-class="h-6 w-6"
          image-class="rounded-full border border-default bg-default object-contain"
          wrapper-class="rounded-full"
          width="24"
          height="24"
        />
      </UTooltip>
    </div>
  </div>
</template>
