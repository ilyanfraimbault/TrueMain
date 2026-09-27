<script setup lang="ts">
import { profilePath } from '~/types/truemains'

/**
 * The true mains starred in this app. The site keeps its own list in the
 * browser, which the app cannot read; this one lives on this machine.
 */
const { favorites, toggle } = useFavorites()
const { profileIconOf } = useChampionStatics()
</script>

<template>
  <div class="flex h-full flex-col gap-4 p-6">
    <PageHeader title="Favorites" icon="i-lucide-star" />

    <div v-if="favorites.length" class="grid grid-cols-2 gap-3 overflow-y-auto">
      <div v-for="identity in favorites" :key="`${identity.gameName}-${identity.tagLine}`" class="surface group flex items-center gap-3 rounded-xl p-3">
        <img :src="profileIconOf(identity.profileIconId) ?? undefined" alt="" class="size-11 rounded-full bg-ink-800 ring-1 ring-default">
        <button type="button" class="min-w-0 flex-1 text-left leading-tight" @click="openOnSite(profilePath(identity))">
          <p class="truncate text-sm font-semibold text-highlighted group-hover:underline">
            {{ identity.gameName }}<span v-if="identity.tagLine" class="font-normal text-dimmed"> #{{ identity.tagLine }}</span>
          </p>
          <p class="mt-0.5 text-[11px] text-dimmed">{{ identity.platformId }} · level {{ identity.summonerLevel }}</p>
        </button>
        <UButton icon="i-lucide-external-link" color="neutral" variant="ghost" size="xs" aria-label="Open on truemain.lol" @click="openOnSite(profilePath(identity))" />
        <UButton icon="i-lucide-star" color="neutral" variant="ghost" size="xs" class="text-gold" aria-label="Remove from favorites" @click="toggle(identity)" />
      </div>
    </div>

    <div v-else class="flex flex-1 flex-col items-center justify-center gap-3 text-center">
      <UIcon name="i-lucide-star" class="size-8 text-dimmed" />
      <p class="text-sm text-muted">Star a true main on the leaderboard to keep them here</p>
      <UButton to="/truemains" label="Open the leaderboard" color="neutral" variant="subtle" size="sm" icon="i-lucide-trophy" />
    </div>
  </div>
</template>
