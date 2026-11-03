<script setup lang="ts">
// The four ways to settle a conflict (#2253), merge offered in both directions. An
// action that takes the account from someone or retires a page asks twice: the first
// click arms it, the second sends it. Emits the request body; the container posts it.
// Each button prints its key (#2293); the queue's shortcuts call `choose` and `confirm`.
import type { PersonaConflict, PersonaConflictResolveBody } from '~~/shared/types/ops'
import { computed, ref } from 'vue'
import { actionKbd } from '~~/shared/utils/conflict-shortcuts'
import { conflictActions } from '~~/shared/utils/persona-conflicts'

const props = defineProps<{
  conflict: Pick<PersonaConflict, 'claimant' | 'owner'>
  busy: boolean
}>()

const emit = defineEmits<{ resolve: [body: PersonaConflictResolveBody] }>()

const actions = computed(() => conflictActions(props.conflict))
const armed = ref<string | null>(null)

function choose(key: string) {
  const action = actions.value.find(a => a.key === key)
  if (!action || props.busy) {
    return
  }

  if (action.confirm && armed.value !== key) {
    armed.value = key
    return
  }

  armed.value = null
  emit('resolve', action.body)
}

/** Sends the armed action — Enter on the keyboard. */
function confirm() {
  if (armed.value) {
    choose(armed.value)
  }
}

defineExpose({ choose, confirm, armed })
</script>

<template>
  <ul class="space-y-3">
    <li v-for="action in actions" :key="action.key" class="flex items-start gap-3">
      <UButton
        :data-action="action.key"
        :icon="action.icon"
        :color="action.color"
        :variant="armed === action.key ? 'solid' : 'subtle'"
        :loading="busy && armed === null"
        :disabled="busy"
        :label="armed === action.key ? `Confirm: ${action.label}` : action.label"
        class="shrink-0"
        @click="choose(action.key)"
      >
        <template v-if="actionKbd(action.key)" #trailing>
          <UKbd :value="actionKbd(action.key)!" size="sm" />
          <UKbd v-if="armed === action.key" value="enter" size="sm" />
        </template>
      </UButton>
      <p class="text-xs text-muted pt-1.5">
        {{ action.description }}
      </p>
    </li>
  </ul>
</template>
