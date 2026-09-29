<script setup lang="ts">
import { getProfileIconUrl } from '~~/shared/utils/ddragon'

/**
 * The true mains followed in this app. The site keeps its own list in the
 * browser, which the app cannot read; this one lives on this machine. A row
 * opens the player's page on the site.
 */
const { favorites } = useFavoriteTruemains()
const { patch } = useChampionStatics()

const profilePath = (gameName: string, tagLine: string | null) =>
  `/truemains/${encodeURIComponent(favoriteNameTag(gameName, tagLine))}`
</script>

<template>
  <div class="flex h-full flex-col gap-4 p-6">
    <PageHeader title="Favorites" icon="i-lucide-star" />

    <div v-if="favorites.length" class="flex min-h-0 flex-col gap-1.5 overflow-y-auto">
      <ListRowSurface
        v-for="favorite in favorites"
        :key="favoriteNameTag(favorite.gameName, favorite.tagLine)"
        dense
        class="group relative gap-3"
      >
        <button
          type="button"
          :aria-label="`${favorite.gameName}${favorite.tagLine ? ` #${favorite.tagLine}` : ''}`"
          class="absolute inset-0 z-[1] rounded-lg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          @click="openOnSite(profilePath(favorite.gameName, favorite.tagLine))"
        />
        <SkeletonImage
          :src="favorite.profileIconId !== null ? getProfileIconUrl(favorite.profileIconId, patch) : null"
          :alt="favorite.gameName"
          class="size-10 shrink-0 rounded"
          width="40"
          height="40"
        />
        <div class="min-w-0 flex-1">
          <div class="flex items-baseline gap-1 truncate">
            <span class="truncate text-sm font-bold text-default">{{ favorite.gameName }}</span>
            <span v-if="favorite.tagLine" class="shrink-0 text-[11px] text-muted">#{{ favorite.tagLine }}</span>
          </div>
          <div v-if="favorite.region" class="mt-0.5 flex"><LeaderboardRegionFlag :region="favorite.region" :width="18" /></div>
        </div>
        <UIcon name="i-lucide-external-link" class="size-4 text-dimmed" />
        <FavoriteToggle
          :game-name="favorite.gameName"
          :tag-line="favorite.tagLine"
          :region="favorite.region"
          :profile-icon-id="favorite.profileIconId"
        />
      </ListRowSurface>
    </div>

    <UEmpty
      v-else
      icon="i-lucide-star"
      description="Follow a true main from the leaderboard to keep them here."
      :actions="[{ label: 'Open the leaderboard', to: '/truemains', color: 'neutral', variant: 'subtle', icon: 'i-lucide-trophy' }]"
      class="flex-1"
    />
  </div>
</template>
