<script setup lang="ts">
import type { ProfileResponse } from '#shared/types/profile'
import type { RankHistoryEntry } from '#shared/types/rank-history'

// The player-scoped champion page's left rail, top half: who the page is about
// and their Solo/Duo standing. Two grid cells, rendered as siblings of the page's
// other cells so the page grid places each one (`order-*`, `xl:*-start`).
defineProps<{
  profile: ProfileResponse | null
  /** Full ddragon version for the profile icon. */
  patch: string | null
  history: readonly RankHistoryEntry[]
  historyLoading: boolean
}>()
</script>

<template>
  <!-- Player identity. Renders even when the champion aggregate failed: it
       is the one thing on the page that never depended on it. -->
  <div class="order-1 min-w-0 xl:col-start-1 xl:row-start-1">
    <ProfileHeaderSkeleton
      v-if="!profile"
      stacked
    />
    <ProfileHeader
      v-else
      stacked
      :identity="profile.identity"
      :patch="patch"
    />
  </div>

  <!-- Ranked card (identical to the profile page's): the rail is the
       player's side of the page, and their elo is the first thing that
       qualifies every number in the middle column. Below xl it drops under
       the build with the performance card — see the page for why. -->
  <div class="order-3 min-w-0 xl:col-start-1 xl:row-start-2">
    <ProfileRankedCardSkeleton v-if="!profile" />
    <ProfileRankedCard
      v-else
      :ranked="profile.ranked"
      :history="history"
      :history-loading="historyLoading"
    />
  </div>
</template>
