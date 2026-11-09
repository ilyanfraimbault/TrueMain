<script setup lang="ts">
import type { ProfileIdentityReadModel } from '#shared/contract/api'
import { platformIdToRegion } from '#shared/utils/region'

// The follow star on a player page's Riot ID line, from the profile's identity.
defineProps<{ identity: ProfileIdentityReadModel }>()
</script>

<template>
  <FavoriteToggle
    :game-name="identity.gameName"
    :tag-line="identity.tagLine"
    :region="platformIdToRegion(identity.platformId)"
    :profile-icon-id="identity.profileIconId"
  />
</template>
