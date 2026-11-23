<script setup lang="ts">
// The four ways to settle a conflict (#2253), merge offered in both directions. An
// action that takes the account from someone or retires a page asks twice: the first
// click arms it, the second sends it. Emits the request body; the container posts it.
// Each button prints its key (#2293); the queue's shortcuts call `choose` and `confirm`.
import type { PersonaConflictReadModel, PersonaConflictResolveRequest } from '#site-shared/contract/api'
import { computed } from 'vue'
import { actionKbd } from '~~/shared/utils/conflict-shortcuts'
import { conflictActions } from '~~/shared/utils/persona-conflicts'
import { useArmedConfirm } from '~/composables/useArmedConfirm'

const props = defineProps<{
  conflict: Pick<PersonaConflictReadModel, 'claimant' | 'owner'>
  busy: boolean
}>()

const emit = defineEmits<{ resolve: [body: PersonaConflictResolveRequest] }>()

const actions = computed(() => conflictActions(props.conflict))
// Only the action sent spins (#2471), not all four.
const { armed, sending, press } = useArmedConfirm({ busy: () => props.busy, scope: () => props.conflict })

function choose(key: string) {
  const action = actions.value.find(a => a.key === key)
  if (action && press(key, { confirm: action.confirm })) {
    emit('resolve', action.body)
  }
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
        :loading="busy && sending === action.key"
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
