<script setup lang="ts">
import type { TruemainActivityResponse } from '#shared/types/activity'
import type { ProfileResponse } from '#shared/types/profile'
import type { RankHistoryEntry } from '#shared/types/rank-history'
import type { ChampionStaticListItem } from '#shared/types/static-data'

// A player profile's 18rem column, for every host's profile page: ranked card,
// activity grid, Truemain score card, then the `mains` slot (the site fills it
// with the main champions card) and the position breakdown. A failed profile is
// said once, instead of four skeletons that never end (#2476).
defineProps<{
  nameTag: string
  profile: ProfileResponse | null | undefined
  profileLoading: boolean
  profileError: unknown
  rankHistory: readonly RankHistoryEntry[]
  rankHistoryLoading: boolean
  activity: TruemainActivityResponse | null | undefined
  activityLoading: boolean
  champions: ChampionStaticListItem[]
}>()

defineSlots<{ mains?: (props: { profile: ProfileResponse }) => unknown }>()
</script>

<template>
  <FetchErrorAlert
    v-if="profileError && !profile"
    :error="profileError"
    title="This player's profile could not be loaded"
  />
  <template v-else>
    <ProfileRanked
      :ranked="profile?.ranked"
      :loading="profileLoading || !profile"
      :history="rankHistory"
      :history-loading="rankHistoryLoading"
      :platform-id="profile?.identity.platformId"
    />

    <!-- Under the ranked card: the LP curve says how the climb went, the grid
         how much was played to get there. Its own loading state is internal, so
         the window switch is usable while the payload lands. -->
    <ProfileActivityHeatmap
      v-if="!profileLoading && profile"
      :data="activity ?? null"
      :loading="activityLoading"
    />

    <ProfileDedicationCardSkeleton v-if="profileLoading || !profile" />
    <ProfileDedicationCard
      v-else-if="profile.dedication"
      :dedication="profile.dedication"
      :champions="champions"
      :name-tag="nameTag"
    />

    <slot v-if="!profileLoading && profile" name="mains" :profile="profile" />

    <ProfilePositionBreakdownSkeleton v-if="profileLoading || !profile" />
    <ProfilePositionBreakdown
      v-else
      :positions="profile.positions"
    />
  </template>
</template>
