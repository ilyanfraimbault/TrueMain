<!--
  The dashboard's latest clips (#2077): the four most recently saved, two by
  two, on the Recordings page's own picture (`RecordingsThumb`: how long ago and
  the length), with the champion and the title under it. A clip opens the
  player the Recordings page opens; "See all" lands on its Clips filter. With
  fewer than four clips the free cells stay as empty slots, so the grid keeps
  its shape. Nothing renders when this shell records nothing or no clip was saved.
-->
<script setup lang="ts">
import { itemTitle, latestClips } from '~/utils/recording-library'

const SLOTS = 4

const { library, status } = useRecordings()
const { nameOf } = useChampionStatics()

const clips = computed(() => (status.value ? latestClips(library.value, SLOTS) : []))
const free = computed(() => SLOTS - clips.value.length)

const playerPath = (id: string) => `/recordings/clips/${encodeURIComponent(id)}`
</script>

<template>
  <section v-if="clips.length" class="surface flex flex-col gap-3 rounded-lg px-4 py-3">
    <div class="flex items-center justify-between">
      <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">Latest clips</h2>
      <UButton
        to="/recordings?kind=clips"
        label="See all"
        trailing-icon="i-lucide-chevron-right"
        color="neutral"
        variant="link"
        size="xs"
        class="-my-1"
      />
    </div>

    <!-- Four across under the match history, two by two once the window narrows. -->
    <ul class="grid grid-cols-2 gap-2 sm:grid-cols-4">
      <li v-for="item in clips" :key="item.id">
        <NuxtLink
          :to="playerPath(item.id)"
          class="group flex flex-col gap-1.5 rounded-md focus-visible:outline-2 focus-visible:outline-primary"
          :aria-label="itemTitle(item, nameOf)"
        >
          <RecordingsThumb :item="item" class="rounded-md ring-1 ring-default transition-shadow group-hover:ring-primary/60" />
          <span class="flex min-w-0 items-center gap-1.5">
            <ChampionPortrait :champion-id="item.championId" size="sm" class="size-4! rounded-sm!" />
            <span class="truncate text-[11px] font-medium text-default group-hover:text-highlighted" :title="itemTitle(item, nameOf)">
              {{ itemTitle(item, nameOf) }}
            </span>
          </span>
        </NuxtLink>
      </li>
      <!-- A free slot is as tall as a clip, its title line included, so the card keeps one height. -->
      <li v-for="slot in free" :key="`free-${slot}`" class="flex flex-col gap-1.5" aria-hidden="true">
        <div class="flex aspect-video items-center justify-center rounded-md border border-dashed border-default">
          <UIcon name="i-lucide-scissors" class="size-4 text-ink-700" />
        </div>
        <span class="h-4" />
      </li>
    </ul>
  </section>
</template>
