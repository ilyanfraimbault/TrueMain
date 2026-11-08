<script setup lang="ts">
// One known player in the edit panel (#2019): rename, accounts, links, what holds
// it against its sources, its history (#2288), and removal. Every edit is written where the weekly
// scrapers cannot undo it, and audited. Emits `changed` after each one so the list
// behind the panel catches up, and `removed` once the player is gone.
import type { PersonaAccountInput, PersonaAdminAccount, PersonaAdminDetail, PersonaAdminLink, PersonaLinkInput } from '~~/shared/types/ops'
import type { PersonaEditAction, PersonaEditorMode } from '~/utils/persona-editor-mode'
import { computed, ref, shallowRef, watch } from 'vue'
import { formatDateTime } from '~~/shared/utils/format'
import { accountOutcomeMessage, displayNameError, editErrorMessage } from '~~/shared/utils/known-players'
import { knownPlayerPath } from '~~/shared/utils/site-link'
import { useKeyedFetch } from '~/composables/useKeyedFetch'
import { panelPhase } from '~/composables/useLastGoodPayload'
import { keyedId, keyedValue } from '~/utils/keyed-fetch'
import { modeFor, settleSaving, startSaving, VIEW_MODE } from '~/utils/persona-editor-mode'

const props = defineProps<{ personaId: string }>()
const emit = defineEmits<{ changed: [], removed: [] }>()

// Keyed by the player (#2471): after a ⌘K jump A→B, A's late answer is dropped, so
// the accounts on screen are always those of the player every edit is sent for.
const { state, reload } = useKeyedFetch<string, PersonaAdminDetail>(id => getPersona(id), { id: () => props.personaId })
// The player on screen and its detail — only ever the one `personaId` names.
const shownId = computed(() => keyedId(state.value) === props.personaId ? props.personaId : null)
const detail = computed(() => shownId.value === null ? null : keyedValue(state.value))
const loadError = computed(() => state.value.s === 'error' ? state.value.error : null)
// A failed reload after an edit keeps the player drawn with a note (#2471).
const phase = computed(() => panelPhase({ hasData: detail.value !== null, pending: state.value.s === 'loading', error: loadError.value }))

// Moves after each edit so the History reloads with the action just recorded.
const historyVersion = ref(0)
const toast = useActionToast()

const mode = shallowRef<PersonaEditorMode>(VIEW_MODE)
// A rename never follows the operator to another player.
watch(() => props.personaId, () => {
  mode.value = VIEW_MODE
})
const current = computed(() => modeFor(mode.value, props.personaId))
const busy = computed(() => current.value.s === 'saving')

const live = computed(() => Boolean(detail.value && !detail.value.removedAtUtc && !detail.value.mergedIntoSlug))

/**
 * Runs one edit on the player on screen: the call, its toast, then a reload of the
 * panel and the list. The id is taken once, before the call, and every step after
 * it — the toast, the mode, the reload — is about that player only.
 */
async function edit(action: PersonaEditAction, failureTitle: string, call: (personaId: string) => Promise<unknown>, success: () => void, draft?: string) {
  const personaId = shownId.value
  if (personaId === null || busy.value) {
    return
  }
  mode.value = startSaving(mode.value, personaId, action, draft)
  let ok = false
  try {
    await call(personaId)
    success()
    ok = true
    emit('changed')
  }
  catch (e) {
    toast.failure(failureTitle, editErrorMessage(e))
  }
  finally {
    mode.value = settleSaving(mode.value, personaId, ok)
    if (keyedId(state.value) === personaId) {
      await reload()
    }
    historyVersion.value++
  }
}

// The name field: open while renaming, and while that rename is being saved.
const renameOpen = computed(() => current.value.s === 'renaming' || (current.value.s === 'saving' && current.value.action === 'rename'))
const newName = computed({
  get: () => current.value.s === 'view' ? '' : current.value.draft ?? '',
  set: (draft: string) => {
    const now = current.value
    if (now.s === 'renaming') {
      mode.value = { ...now, draft }
    }
  },
})
const nameError = computed(() => displayNameError(newName.value))

function startRename() {
  if (shownId.value !== null && !busy.value) {
    mode.value = { s: 'renaming', personaId: shownId.value, draft: detail.value?.displayName ?? '' }
  }
}

function cancelRename() {
  if (current.value.s === 'renaming') {
    mode.value = VIEW_MODE
  }
}

function rename() {
  if (current.value.s !== 'renaming' || nameError.value) {
    return
  }
  const draft = current.value.draft
  const name = draft.trim()
  void edit('rename', 'Not renamed', personaId => renamePersona(personaId, name), () => {
    toast.success('Renamed', 'The sources no longer overwrite this name.')
  }, draft)
}

function addAccount(account: PersonaAccountInput) {
  void edit('addAccount', 'Account not added', async (personaId) => {
    const edited = await addPersonaAccount(personaId, account)
    const message = accountOutcomeMessage(edited.accounts[0]?.status ?? 'invalid')
    toast[message.tone](message.title, message.description)
  }, () => {})
}

function unlinkAccount(account: PersonaAdminAccount) {
  void edit('unlinkAccount', 'Account not unlinked', personaId => unlinkPersonaAccount(personaId, account.id), () => {
    toast.success(`${account.gameName}#${account.tagLine} unlinked`, 'This player\'s sources can no longer link it back.')
  })
}

function addLink(link: PersonaLinkInput) {
  void edit('addLink', 'Link not added', personaId => addPersonaLink(personaId, link), () => toast.success('Link added'))
}

function removeLink(link: PersonaAdminLink) {
  void edit('removeLink', 'Link not removed', personaId => removePersonaLink(personaId, link.id), () => {
    toast.success('Link removed', 'The sources can no longer add it back.')
  })
}

function remove(takedown: boolean) {
  const name = detail.value?.displayName ?? 'Known player'
  void edit('remove', 'Not removed', personaId => removePersona(personaId, takedown), () => {
    toast.success(`${name} ${takedown ? 'taken down' : 'removed'}`, takedown
      ? 'The page is gone, and its accounts are barred from every known player.'
      : 'The page is gone, and the sources can no longer recreate it.')
    emit('removed')
  })
}

defineExpose({ reload })
</script>

<template>
  <div>
    <FetchErrorAlert v-if="phase === 'failed'" :error="loadError" title="Failed to load this known player" />
    <!-- Keyed by the player: the panels below (an armed unlink, a typed account) start over on a ⌘K jump. -->
    <div v-else-if="detail" :key="shownId ?? ''" class="space-y-6">
      <p v-if="phase === 'stale'" data-stale class="text-xs text-muted">
        The last reload failed; showing this known player as it was.
      </p>
      <header class="space-y-1">
        <div v-if="live && renameOpen" class="flex flex-wrap items-start gap-2">
          <UFormField :error="nameError ?? undefined" class="flex-1 min-w-48">
            <UInput v-model="newName" aria-label="Name" class="w-full" @keydown.enter.prevent="rename" />
          </UFormField>
          <UButton data-rename-save label="Save" :disabled="busy || Boolean(nameError)" @click="rename" />
          <UButton label="Cancel" color="neutral" variant="ghost" :disabled="busy" @click="cancelRename" />
        </div>
        <div v-else class="flex items-center gap-2">
          <p class="text-base font-semibold text-highlighted">
            {{ detail.displayName }}
          </p>
          <UButton v-if="live" data-rename icon="i-lucide-pencil" size="xs" color="neutral" variant="ghost" aria-label="Rename" @click="startRename" />
          <SiteLink v-if="!detail.removedAtUtc" :path="knownPlayerPath(detail.slug)" />
        </div>
        <p class="text-xs text-muted">
          <span class="font-mono">{{ detail.slug }}</span> · from {{ detail.source }} · since {{ formatDateTime(detail.createdAtUtc) }}
          <template v-if="detail.displayNameLocked">
            · name set by an operator
          </template>
        </p>
        <UAlert
          v-if="detail.removedAtUtc"
          data-removed
          color="error"
          variant="subtle"
          icon="i-lucide-user-x"
          :title="`Removed on ${formatDateTime(detail.removedAtUtc)}`"
          description="Its slug is kept so the sources cannot recreate it."
        />
        <UAlert
          v-else-if="detail.mergedIntoSlug"
          color="info"
          variant="subtle"
          icon="i-lucide-merge"
          :title="`Merged into ${detail.mergedIntoSlug}`"
          description="Its page redirects there; edit that known player instead."
        />
      </header>

      <template v-if="live">
        <KnownPlayersAccountsPanel :accounts="detail.accounts" :busy="busy" @add="addAccount" @unlink="unlinkAccount" />
        <KnownPlayersLinksPanel :links="detail.links" :busy="busy" @add="addLink" @remove="removeLink" />
      </template>
      <KnownPlayersDecisions :conflicts="detail.conflicts" :exclusions="detail.exclusions" :removed-links="detail.removedLinks" />
      <KnownPlayersHistory :persona-id="personaId" :version="historyVersion" />
      <template v-if="live">
        <USeparator />
        <KnownPlayersRemoval :display-name="detail.displayName" :busy="busy" @remove="remove" />
      </template>
    </div>
    <div v-else class="py-10 text-center">
      <UIcon name="i-lucide-loader-circle" class="size-5 animate-spin text-muted" />
    </div>
  </div>
</template>
