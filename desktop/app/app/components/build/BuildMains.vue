<script setup lang="ts">
import { formatRank, profilePath, rankCrestUrl } from '~/types/truemains'

/**
 * The champion's best true mains, each with the keystone and first item they
 * run on it — where the reference apps list pro players, TrueMain lists the
 * people who main the champion. A row opens the player's page on the site.
 */
const props = defineProps<{ championId: number }>()

const { rows, pending } = useChampionMains(toRef(props, 'championId'))
const { profileIconOf } = useChampionStatics()
const { perk, item } = useBuildStatics()

const on = (row: NonNullable<typeof rows.value>[number]) => row.topChampions.find(champion => champion.championId === props.championId)
</script>

<template>
  <div class="flex flex-col gap-1">
    <h3 class="px-2 pb-1 stat-label">True mains</h3>

    <button
      v-for="row in rows ?? []"
      :key="`${row.identity.gameName}-${row.identity.tagLine}`"
      type="button"
      class="flex items-center gap-2 rounded-lg px-2 py-1.5 text-left transition-colors hover:bg-elevated"
      :title="`Open ${row.identity.gameName} on truemain.lol`"
      @click="openOnSite(profilePath(row.identity))"
    >
      <img :src="profileIconOf(row.identity.profileIconId) ?? undefined" alt="" class="size-8 shrink-0 rounded-full bg-ink-800 ring-1 ring-default">
      <div class="min-w-0 flex-1 leading-tight">
        <p class="truncate text-[13px] font-semibold text-default">{{ row.identity.gameName }}</p>
        <p class="mt-0.5 flex items-center gap-1 truncate text-[11px] text-dimmed">
          <img v-if="rankCrestUrl(row.ranked.tier)" :src="rankCrestUrl(row.ranked.tier)!" alt="" class="size-3.5">
          {{ formatRank(row.ranked) }}
        </p>
      </div>
      <div class="flex shrink-0 items-center gap-0.5">
        <GameIcon v-if="on(row)?.primaryKeystoneId" :source="perk(on(row)!.primaryKeystoneId!)" size="size-6" round />
        <GameIcon v-if="on(row)?.firstItemId" :source="item(on(row)!.firstItemId!)" size="size-6" />
      </div>
    </button>

    <div v-if="pending && !rows" class="flex flex-col gap-1.5 px-2">
      <USkeleton v-for="index in 3" :key="index" class="h-9 w-full" />
    </div>
    <p v-else-if="rows && !rows.length" class="px-2 text-xs text-dimmed">No true main tracked on this champion yet</p>
  </div>
</template>
