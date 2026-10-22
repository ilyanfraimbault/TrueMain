<!--
  The favourite clips on the Favorites page (#2080), the app's own kind next to
  the site's players, champions and matchups. A clip's favourite is the
  library's flag (#1755), set from its player or the Recordings card and kept by
  the shell, so this reads that flag rather than a copy of it: one row per clip
  — champion, title, "Ahri · Victory · 9 / 2 / 11" — opening its player, and
  the star to drop it. Nothing renders without a favourite clip.
-->
<script setup lang="ts">
import { favouriteClips, recordingKda } from '~/utils/recording-library'

const { library, updateClip } = useRecordings()
const { nameOf } = useChampionStatics()

const clips = computed(() => favouriteClips(library.value))

function summary(clip: (typeof clips.value)[number]) {
  const parts = [clip.championId ? nameOf(clip.championId) : 'Game']
  if (clip.win !== null) parts.push(clip.win ? 'Victory' : 'Defeat')
  const kda = recordingKda(clip)
  if (kda) parts.push(kda.line)
  return parts.join(' · ')
}
</script>

<template>
  <section v-if="clips.length" class="space-y-3" aria-labelledby="favorites-clips">
    <h2 id="favorites-clips" class="eyebrow">
      Clips
    </h2>
    <ul class="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
      <li v-for="clip in clips" :key="clip.id" class="surface flex items-center gap-2 rounded-lg p-1.5">
        <NuxtLink
          :to="`/recordings/clips/${encodeURIComponent(clip.id)}`"
          class="surface-hover flex min-w-0 flex-1 items-center gap-2.5 rounded-md p-1"
        >
          <ChampionPortrait :champion-id="clip.championId" size="sm" />
          <span class="min-w-0 flex-1 leading-tight">
            <span class="block truncate text-sm font-medium text-highlighted">{{ clip.title }}</span>
            <span class="block truncate text-xs text-muted tabular-nums">{{ summary(clip) }}</span>
          </span>
        </NuxtLink>
        <FavoriteStar
          active
          :label="`Remove ${clip.title} from favorites`"
          @toggle="updateClip(clip.id, { favorite: false })"
        />
      </li>
    </ul>
  </section>
</template>
