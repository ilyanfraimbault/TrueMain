<script setup lang="ts">
// A known player's links in the edit panel (#2019): each with its source, a removal
// that asks twice (the sources can no longer add the URL back), and the form that
// adds one by hand. Emits; the container calls the API.
import type { PersonaAdminLink, PersonaLinkInput } from '~~/shared/types/ops'
import { computed, ref } from 'vue'
import { LINK_KINDS, linkUrlError } from '~~/shared/utils/known-players'

const props = defineProps<{
  links: PersonaAdminLink[]
  busy: boolean
}>()

const emit = defineEmits<{
  remove: [link: PersonaAdminLink]
  add: [link: PersonaLinkInput]
}>()

const armed = ref<string | null>(null)

function remove(link: PersonaAdminLink) {
  if (props.busy) {
    return
  }
  if (armed.value !== link.id) {
    armed.value = link.id
    return
  }
  armed.value = null
  emit('remove', link)
}

const kind = ref<string>(LINK_KINDS[0]!)
const url = ref('')
const kindItems = LINK_KINDS.map(k => ({ label: k, value: k }))
const error = computed(() => url.value.trim() ? linkUrlError(url.value) : null)

function add() {
  if (props.busy || !url.value.trim() || error.value) {
    return
  }
  emit('add', { kind: kind.value, url: url.value.trim() })
  url.value = ''
}
</script>

<template>
  <section class="space-y-2">
    <p class="text-xs uppercase text-muted">
      Links · {{ links.length }}
    </p>
    <ul class="space-y-1 text-sm">
      <li v-for="link in links" :key="link.id" data-link class="flex items-center gap-2 rounded px-2 py-1 hover:bg-elevated/40">
        <span class="text-xs text-muted w-20 shrink-0">{{ link.kind }}</span>
        <a :href="link.url" target="_blank" rel="noopener noreferrer" class="truncate text-primary hover:underline">{{ link.url }}</a>
        <span class="ml-auto text-xs text-dimmed shrink-0">{{ link.source }}</span>
        <UButton
          :data-remove-link="link.id"
          icon="i-lucide-trash-2"
          size="xs"
          color="error"
          :variant="armed === link.id ? 'solid' : 'ghost'"
          :label="armed === link.id ? 'Confirm removal' : undefined"
          :aria-label="`Remove ${link.url}`"
          :disabled="busy"
          @click="remove(link)"
        />
      </li>
      <li v-if="links.length === 0" class="text-xs text-dimmed px-2">
        No link.
      </li>
    </ul>

    <form class="flex flex-wrap items-start gap-2 pt-1" data-add-link @submit.prevent="add">
      <USelect v-model="kind" :items="kindItems" class="w-32" aria-label="Link kind" />
      <UFormField :error="error ?? undefined" class="flex-1 min-w-48">
        <UInput v-model="url" placeholder="https://…" icon="i-lucide-link" class="w-full" aria-label="Link URL" />
      </UFormField>
      <UButton type="submit" label="Add link" color="neutral" variant="subtle" :disabled="busy || !url.trim() || Boolean(error)" />
    </form>
  </section>
</template>
