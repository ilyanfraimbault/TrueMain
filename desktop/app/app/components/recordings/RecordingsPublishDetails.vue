<script setup lang="ts">
import { YOUTUBE_DESCRIPTION_MAX } from '~/utils/clip-publish'

/**
 * The text the publish dialog prepares for YouTube's form, each field with its
 * copy button. The title is read-only: TrueMain recognises the video by it.
 */
defineProps<{ title: string }>()
const description = defineModel<string>('description', { required: true })
const tags = defineModel<string>('tags', { required: true })

const { copy } = useClipPublish()
</script>

<template>
  <UFormField label="Title" help="Keep it as is: TrueMain recognises the video by its title. Rename the clip to change it.">
    <UInput :model-value="title" readonly class="w-full">
      <template #trailing>
        <UButton icon="i-lucide-copy" color="neutral" variant="link" size="xs" aria-label="Copy the title" @click="copy(title, 'Title')" />
      </template>
    </UInput>
  </UFormField>
  <UFormField label="Description">
    <div class="relative">
      <UTextarea v-model="description" :maxlength="YOUTUBE_DESCRIPTION_MAX" :rows="4" autoresize class="w-full" />
      <UButton
        icon="i-lucide-copy"
        color="neutral"
        variant="link"
        size="xs"
        class="absolute right-1.5 top-1.5"
        aria-label="Copy the description"
        @click="copy(description, 'Description')"
      />
    </div>
  </UFormField>
  <UFormField label="Tags" help="Under &quot;Show more&quot; in YouTube's form.">
    <UInput v-model="tags" class="w-full">
      <template #trailing>
        <UButton icon="i-lucide-copy" color="neutral" variant="link" size="xs" aria-label="Copy the tags" @click="copy(tags, 'Tags')" />
      </template>
    </UInput>
  </UFormField>
</template>
