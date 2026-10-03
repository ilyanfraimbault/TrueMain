<script setup lang="ts">
import type { DraftClip } from '~/composables/useRecapDrafts'
import type { Clip, Moment } from '~/types/recordings'
import { formatClock } from '~/utils/recording-moments'

/**
 * Beside the recap's video: the clips — those being cut, each named and saved
 * on its own (or all at once), then those already saved from this game — and
 * the game's moments to jump to.
 */
const props = defineProps<{
  drafts: DraftClip[]
  selected: string | null
  saved: Clip[]
  moments: Moment[]
  currentMs: number
}>()
const emit = defineEmits<{
  select: [key: string]
  rename: [key: string, title: string]
  preview: [draft: DraftClip]
  remove: [key: string]
  save: [key: string]
  saveAll: []
  jump: [moment: Moment]
}>()

const tab = ref<'clips' | 'moments'>('clips')
const tabs = computed(() => [
  { label: 'Clips', value: 'clips', badge: props.drafts.length + props.saved.length || undefined },
  { label: 'Moments', value: 'moments', badge: props.moments.length || undefined },
])
const savingAny = computed(() => props.drafts.some(draft => draft.saving))
</script>

<template>
  <aside class="flex min-h-0 flex-col gap-2">
    <UTabs v-model="tab" :items="tabs" :content="false" size="xs" variant="link" class="shrink-0" />

    <div v-if="tab === 'clips'" class="flex min-h-0 flex-1 flex-col gap-2 overflow-y-auto pr-1">
      <p v-if="!drafts.length" class="rounded-lg bg-elevated p-3 text-xs leading-relaxed text-muted">
        Drag across the timeline to cut a clip, or press <UKbd value="I" size="sm" /> and <UKbd value="O" size="sm" /> at the
        playhead. Cut as many as you like: each is saved as its own video.
      </p>
      <RecapDraftRow
        v-for="draft in drafts"
        :key="draft.key"
        :draft="draft"
        :selected="draft.key === selected"
        @select="emit('select', draft.key)"
        @rename="title => emit('rename', draft.key, title)"
        @preview="emit('preview', draft)"
        @remove="emit('remove', draft.key)"
        @save="emit('save', draft.key)"
      />
      <UButton
        v-if="drafts.length > 1"
        :label="`Save all ${drafts.length}`"
        icon="i-lucide-save-all"
        variant="soft"
        size="sm"
        block
        :loading="savingAny"
        @click="emit('saveAll')"
      />

      <template v-if="saved.length">
        <h3 class="stat-label mt-2 px-1">Saved clips · {{ saved.length }}</h3>
        <NuxtLink
          v-for="clip in saved"
          :key="clip.id"
          :to="`/recordings/clips/${encodeURIComponent(clip.id)}`"
          class="flex items-center gap-2 rounded-md px-2 py-1.5 text-xs transition-colors hover:bg-accented"
        >
          <UIcon name="i-lucide-scissors" class="size-3.5 shrink-0 text-primary" />
          <span class="truncate text-default">{{ clip.title }}</span>
          <UIcon v-if="clip.favorite" name="i-lucide-star" class="size-3 shrink-0 text-gold" />
          <span class="ml-auto shrink-0 tabular-nums text-dimmed">{{ formatClock(clip.durationMs) }}</span>
        </NuxtLink>
      </template>
    </div>

    <div v-else class="min-h-0 flex-1 overflow-y-auto pr-1">
      <RecapMoments :moments="moments" :current-ms="currentMs" @jump="moment => emit('jump', moment)" />
    </div>
  </aside>
</template>
