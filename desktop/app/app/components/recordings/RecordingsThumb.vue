<script setup lang="ts">
import type { LibraryItem } from '~/utils/recording-library'
import { formatRelativeTime } from '#common/utils/relativeTime'
import { formatClock } from '~/utils/recording-moments'

/**
 * A card's 16:9 picture: the recording's thumbnail, else the champion's splash
 * dimmed (a game still recording has neither, and the shell may not have cut
 * one yet), with how long ago and the length at its bottom right, as on the
 * reference page. A game still recording or being finalised says so.
 */
const props = defineProps<{ item: LibraryItem }>()

const { fileSrc } = useRecordings()
const { aliasOf } = useChampionStatics()

const failed = ref(false)
const thumbnail = computed(() => (failed.value ? null : fileSrc(props.item.thumbnailPath)))
watch(() => props.item.thumbnailPath, () => (failed.value = false))

const splash = computed(() => {
  const alias = props.item.championId ? aliasOf(props.item.championId) : null
  return alias ? splashOfAlias(alias) : null
})

const ago = computed(() => formatRelativeTime(new Date(props.item.atMs).toISOString()))
</script>

<template>
  <div class="relative aspect-video overflow-hidden bg-ink-900">
    <img
      v-if="thumbnail"
      :src="thumbnail"
      alt=""
      class="img-skeleton size-full object-cover"
      loading="lazy"
      @error="failed = true"
    >
    <template v-else>
      <img v-if="splash" :src="splash" alt="" class="img-skeleton size-full object-cover object-[70%_20%] opacity-35" loading="lazy">
      <UIcon v-else name="i-lucide-film" class="absolute left-1/2 top-1/2 size-7 -translate-1/2 text-ink-700" />
    </template>

    <span
      v-if="item.status !== 'ready'"
      class="absolute left-2 top-2 flex items-center gap-1.5 rounded-md bg-black/70 px-1.5 py-0.5 text-[11px] font-medium text-highlighted"
    >
      <template v-if="item.status === 'recording'">
        <span class="size-1.5 animate-tm-pulse rounded-full bg-red-500" />
        Recording
      </template>
      <template v-else>
        <UIcon name="i-lucide-loader-circle" class="size-3" />
        Finalising
      </template>
    </span>

    <span v-if="item.pinned" class="absolute left-2 top-2 flex size-5 items-center justify-center rounded-md bg-black/70" :class="item.status !== 'ready' && 'hidden'">
      <UIcon :name="item.kind === 'clip' ? 'i-lucide-star' : 'i-lucide-pin'" class="size-3 text-gold" />
    </span>

    <div class="absolute bottom-1.5 right-1.5 flex items-center gap-1 text-[11px] font-medium tabular-nums">
      <span class="rounded bg-black/70 px-1.5 py-0.5 text-muted">{{ ago }}</span>
      <span v-if="item.durationMs !== null" class="rounded bg-black/70 px-1.5 py-0.5 text-highlighted">{{ formatClock(item.durationMs) }}</span>
    </div>
  </div>
</template>
