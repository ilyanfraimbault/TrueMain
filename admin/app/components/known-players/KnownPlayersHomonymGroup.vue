<script setup lang="ts">
// One group of the homonyms queue (#2271): known players sharing a name and no
// account, one row each (#2273) with the channels they share marked. The operator
// picks who belongs to it and either merges them into the one kept — oldest by
// default, the slug the site advertised first — or marks them as different people.
// The merge retires pages, so it asks twice.
import type { PersonaHomonymDistinctBody, PersonaHomonymGroup, PersonaHomonymMergeBody } from '~~/shared/types/ops'
import { computed, ref, watch } from 'vue'
import { markedDistinct } from '~~/shared/utils/persona-homonyms'

const props = defineProps<{
  group: PersonaHomonymGroup
  busy: boolean
}>()

const emit = defineEmits<{
  merge: [body: PersonaHomonymMergeBody]
  distinct: [body: PersonaHomonymDistinctBody]
}>()

const keepId = ref('')
const selected = ref<string[]>([])
const armed = ref(false)

watch(() => props.group, (group) => {
  keepId.value = group.personas[0]?.id ?? ''
  selected.value = group.personas.map(p => p.id)
  armed.value = false
}, { immediate: true })

const kept = computed(() => props.group.personas.find(p => p.id === keepId.value))
const mergeIds = computed(() => selected.value.filter(id => id !== keepId.value))
const markedPairs = computed(() => props.group.distinctPairs.length)

function toggle(id: string) {
  armed.value = false
  selected.value = selected.value.includes(id) ? selected.value.filter(s => s !== id) : [...selected.value, id]
}

function keep(id: string) {
  armed.value = false
  keepId.value = id
  if (!selected.value.includes(id)) {
    selected.value = [...selected.value, id]
  }
}

function merge() {
  if (props.busy || mergeIds.value.length === 0) {
    return
  }

  if (!armed.value) {
    armed.value = true
    return
  }

  armed.value = false
  emit('merge', { keepId: keepId.value, mergeIds: mergeIds.value })
}

function distinct() {
  if (!props.busy && selected.value.length >= 2) {
    emit('distinct', { personaIds: selected.value })
  }
}

function note(id: string) {
  const others = props.group.personas.filter(p => p.id !== id && markedDistinct(props.group, id, p.id))
  return others.length > 0 ? `Different from ${others.map(p => p.slug).join(', ')}` : ''
}
</script>

<template>
  <section class="space-y-3" :data-group="group.key">
    <div v-if="group.sharedLinks.length > 0" class="flex items-center gap-1.5 text-xs text-success">
      <UIcon name="i-lucide-link" class="size-3.5" />
      {{ group.sharedLinks.length === 1 ? 'One channel' : `${group.sharedLinks.length} channels` }} listed by more than one of them — likely one person.
    </div>

    <div class="overflow-hidden rounded-lg ring-1 ring-default">
      <div class="hidden grid-cols-[15rem_minmax(0,1fr)_minmax(0,1fr)] gap-x-6 border-b border-default bg-elevated/50 px-3 py-1.5 text-xs uppercase text-muted md:grid">
        <span>Known player</span>
        <span>Accounts</span>
        <span>Links</span>
      </div>
      <div class="divide-y divide-default">
        <KnownPlayersHomonymRow
          v-for="persona in group.personas"
          :key="persona.id"
          :persona="persona"
          :kept="persona.id === keepId"
          :included="selected.includes(persona.id)"
          :busy="busy"
          :shared-links="group.sharedLinks"
          :note="note(persona.id)"
          @keep="keep(persona.id)"
          @toggle="toggle(persona.id)"
        />
      </div>
    </div>

    <div class="flex flex-wrap items-center gap-2">
      <UButton
        data-action="merge"
        icon="i-lucide-merge"
        color="primary"
        :variant="armed ? 'solid' : 'subtle'"
        :disabled="busy || mergeIds.length === 0"
        :label="armed ? `Confirm: merge ${mergeIds.length} into ${kept?.slug}` : `Merge ${mergeIds.length} into ${kept?.slug}`"
        @click="merge()"
      />
      <UButton
        data-action="distinct"
        icon="i-lucide-users"
        color="neutral"
        variant="subtle"
        :disabled="busy || selected.length < 2"
        label="Different people"
        @click="distinct()"
      />
      <p class="text-xs text-muted">
        {{ armed ? 'Their accounts and links join the kept one; their pages redirect there.' : '' }}
        {{ !armed && markedPairs > 0 ? `${markedPairs} pair${markedPairs === 1 ? '' : 's'} already marked as different people.` : '' }}
      </p>
    </div>
  </section>
</template>
