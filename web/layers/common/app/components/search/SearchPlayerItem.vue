<script setup lang="ts">
import type { SearchPlayer } from '#shared/types/search'
import { getProfileIconUrl } from '#shared/utils/ddragon'

// A known player in the search palette (`AppSearch`, #2018): the face of their
// best tracked account, their name, how many accounts they have, and that
// account's rank crest. It opens `/players/{slug}`, the page listing every one.
// Drawn here rather than through `Account` for the same reason as
// `SearchTruemainItem`: the palette re-fills the row DOM on every keystroke.
const props = defineProps<{
  player: SearchPlayer
  /** Data Dragon patch for the icon; null until the version list has loaded. */
  patch: string | null
}>()

const iconUrl = computed(() => (props.player.profileIconId
  ? getProfileIconUrl(props.player.profileIconId, props.patch)
  : null))
const accounts = computed(() => `${props.player.accountCount} ${props.player.accountCount === 1 ? 'account' : 'accounts'}`)
</script>

<template>
  <span class="flex min-w-0 flex-1 items-center gap-1.5">
    <SearchItemAvatar :src="iconUrl" />

    <span class="flex min-w-0 flex-1 flex-col items-start gap-0.5">
      <span class="truncate text-highlighted">{{ player.persona.displayName }}</span>
      <span class="text-xs text-dimmed">{{ accounts }}</span>
    </span>

    <RankEmblem v-if="player.ranked" :rank="player.ranked" :size="22" crest-only class="ms-auto shrink-0" />
  </span>
</template>
