<script setup lang="ts">
import type { ProfileIdentity } from '~~/shared/types/profile'
import { getProfileIconUrl } from '~~/shared/utils/ddragon'
import { platformIdToRegion } from '~~/shared/utils/region'

const props = defineProps<{
  identity: ProfileIdentity
  patch: string | null
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
  <section class="flex items-center gap-4">
    <SkeletonImage
      :src="iconUrl"
      :alt="`${identity.gameName} profile icon`"
      class="size-20 rounded-lg"
    />
    <div class="flex flex-col gap-1">
      <h1 class="text-2xl font-semibold leading-tight">
        {{ displayName }}
      </h1>
      <div class="flex flex-wrap items-center gap-2 text-sm text-muted">
        <LeaderboardRegionFlag :region="region" :width="18" />
        <span>Level {{ identity.summonerLevel }}</span>
      </div>
    </div>
  </section>
</template>
