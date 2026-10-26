<script setup lang="ts">
// One known player in the edit panel (#2019): rename, accounts, links, what holds
// it against its sources, its history (#2288), and removal. Every edit is written where the weekly
// scrapers cannot undo it, and audited. Emits `changed` after each one so the list
// behind the panel catches up, and `removed` once the player is gone.
import type { PersonaAccountInput, PersonaAdminAccount, PersonaAdminDetail, PersonaAdminLink, PersonaLinkInput } from '~~/shared/types/ops'
import { computed, ref, watch } from 'vue'
import { formatDateTime } from '~~/shared/utils/format'
import { accountOutcomeMessage, displayNameError, editErrorMessage } from '~~/shared/utils/known-players'

const props = defineProps<{ personaId: string }>()
const emit = defineEmits<{ changed: [], removed: [] }>()

const detail = ref<PersonaAdminDetail | null>(null)
const loadError = ref<unknown>(null)
const busy = ref(false)
// Moves after each edit so the History reloads with the action just recorded.
const historyVersion = ref(0)
const toast = useActionToast()

async function load() {
  loadError.value = null
  try {
    detail.value = await getPersona(props.personaId)
  }
  catch (e) {
    loadError.value = e
  }
}

watch(() => props.personaId, () => {
  detail.value = null
  load()
}, { immediate: true })

const live = computed(() => Boolean(detail.value && !detail.value.removedAtUtc && !detail.value.mergedIntoSlug))

/** Runs one edit: the call, its toast, then a reload of the panel and the list. */
async function edit(failureTitle: string, call: () => Promise<unknown>, success: () => void) {
  busy.value = true
  try {
    await call()
    success()
    emit('changed')
  }
  catch (e) {
    toast.failure(failureTitle, editErrorMessage(e))
  }
  finally {
    busy.value = false
    await load()
    historyVersion.value++
  }
}

const renaming = ref(false)
const newName = ref('')
const nameError = computed(() => displayNameError(newName.value))

function startRename() {
  newName.value = detail.value?.displayName ?? ''
  renaming.value = true
}

function rename() {
  if (nameError.value) {
    return
  }
  const name = newName.value.trim()
  edit('Not renamed', () => renamePersona(props.personaId, name), () => {
    toast.success('Renamed', 'The sources no longer overwrite this name.')
    renaming.value = false
  })
}

function addAccount(account: PersonaAccountInput) {
  edit('Account not added', async () => {
    const edited = await addPersonaAccount(props.personaId, account)
    const message = accountOutcomeMessage(edited.accounts[0]?.status ?? 'invalid')
    toast[message.tone](message.title, message.description)
  }, () => {})
}

function unlinkAccount(account: PersonaAdminAccount) {
  edit('Account not unlinked', () => unlinkPersonaAccount(props.personaId, account.id), () => {
    toast.success(`${account.gameName}#${account.tagLine} unlinked`, 'This player\'s sources can no longer link it back.')
  })
}

function addLink(link: PersonaLinkInput) {
  edit('Link not added', () => addPersonaLink(props.personaId, link), () => toast.success('Link added'))
}

function removeLink(link: PersonaAdminLink) {
  edit('Link not removed', () => removePersonaLink(props.personaId, link.id), () => {
    toast.success('Link removed', 'The sources can no longer add it back.')
  })
}

function remove(takedown: boolean) {
  const name = detail.value?.displayName ?? 'Known player'
  edit('Not removed', () => removePersona(props.personaId, takedown), () => {
    toast.success(`${name} ${takedown ? 'taken down' : 'removed'}`, takedown
      ? 'The page is gone, and its accounts are barred from every known player.'
      : 'The page is gone, and the sources can no longer recreate it.')
    emit('removed')
  })
}

defineExpose({ reload: load })
</script>

<template>
  <div>
    <FetchErrorAlert v-if="loadError" :error="loadError" title="Failed to load this known player" />
    <div v-else-if="detail" class="space-y-6">
      <header class="space-y-1">
        <div v-if="renaming" class="flex flex-wrap items-start gap-2">
          <UFormField :error="nameError ?? undefined" class="flex-1 min-w-48">
            <UInput v-model="newName" aria-label="Name" class="w-full" @keydown.enter.prevent="rename" />
          </UFormField>
          <UButton data-rename-save label="Save" :disabled="busy || Boolean(nameError)" @click="rename" />
          <UButton label="Cancel" color="neutral" variant="ghost" @click="renaming = false" />
        </div>
        <div v-else class="flex items-center gap-2">
          <p class="text-base font-semibold text-highlighted">
            {{ detail.displayName }}
          </p>
          <UButton v-if="live" data-rename icon="i-lucide-pencil" size="xs" color="neutral" variant="ghost" aria-label="Rename" @click="startRename" />
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
