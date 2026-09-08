<script setup lang="ts">
import type { ProfileIdentity } from '~~/shared/types/profile'
import { getProfileIconUrl } from '~~/shared/utils/ddragon'
import { platformIdToRegion } from '~~/shared/utils/region'

const props = defineProps<{
  identity: ProfileIdentity
  patch: string | null
  /**
   * Stack the icon over the identity from `xl` up, for the narrow left rail of
   * the player-scoped champion page. The default row layout keeps its full
   * width on the profile page; in a ~17rem rail it would squeeze the Riot ID
   * into three wrapped lines beside the icon. Below `xl` the rail is full width
   * again, so the row layout is the right one there in both cases.
   */
  stacked?: boolean
}>()

const iconUrl = computed(() => getProfileIconUrl(props.identity.profileIconId, props.patch))

const region = computed(() => platformIdToRegion(props.identity.platformId))

const displayName = computed(() => {
  return props.identity.tagLine
    ? `${props.identity.gameName}#${props.identity.tagLine}`
    : props.identity.gameName
})
</script>

<template>
  <section
    class="flex items-center gap-4"
    :class="stacked ? 'xl:flex-col xl:items-start xl:gap-3' : undefined"
  >
    <SkeletonImage
      :src="iconUrl"
      :alt="`${identity.gameName} profile icon`"
      class="size-20 rounded-lg"
    />
    <div class="flex min-w-0 flex-col items-start gap-1">
      <!-- The follow star sits on the Riot ID, not under the identity block:
           it acts on the account the title names, and as a labelled pill on its
           own line it read as a third stat under the level rather than as a
           control attached to the name. -->
      <div class="flex min-w-0 items-center gap-2">
        <h1 class="min-w-0 break-words text-2xl font-semibold leading-tight">
          {{ displayName }}
        </h1>
        <FavoriteToggle
          :game-name="identity.gameName"
          :tag-line="identity.tagLine"
          :region="region"
          :profile-icon-id="identity.profileIconId"
        />
      </div>
      <div class="flex flex-wrap items-center gap-2 text-sm text-muted">
        <LeaderboardRegionFlag :region="region" :width="18" />
        <span>Level {{ identity.summonerLevel }}</span>
      </div>
    </div>
  </section>
</template>
