<script setup lang="ts">
// Adding a known player by hand (#2019): a name, the slug it suggests (editable,
// never used before), and optionally a first account and link — the edit panel
// adds the rest. Everything it writes carries the source `manual`, and the name is
// the operator's from then on. Emits `created` with the API's answer.
import type { PersonaCreateBody, PersonaEdited } from '~~/shared/types/ops'
import { computed, ref, watch } from 'vue'
import { ACCOUNT_PLATFORMS, accountInput, displayNameError, editErrorMessage, LINK_KINDS, linkUrlError, slugError, slugify } from '~~/shared/utils/known-players'

const emit = defineEmits<{ created: [edited: PersonaEdited], cancel: [] }>()

const displayName = ref('')
const slug = ref('')
const slugEdited = ref(false)
watch(displayName, (name) => {
  if (!slugEdited.value) {
    slug.value = slugify(name)
  }
})

const riotId = ref('')
const platformId = ref(ACCOUNT_PLATFORMS[0]!)
const linkKind = ref<string>(LINK_KINDS[0]!)
const linkUrl = ref('')

const errors = computed(() => {
  const account = riotId.value.trim() ? accountInput(riotId.value, platformId.value) : null
  return {
    displayName: displayName.value ? displayNameError(displayName.value) : null,
    slug: slug.value ? slugError(slug.value) : null,
    account: account && 'error' in account ? account.error : null,
    link: linkUrl.value.trim() ? linkUrlError(linkUrl.value) : null,
  }
})
const ready = computed(() => !displayNameError(displayName.value) && !slugError(slug.value)
  && !errors.value.account && !errors.value.link)

const busy = ref(false)
const failure = ref<string | null>(null)

function body(): PersonaCreateBody {
  const account = riotId.value.trim() ? accountInput(riotId.value, platformId.value) : null
  return {
    slug: slug.value,
    displayName: displayName.value.trim(),
    accounts: account && !('error' in account) ? [account] : [],
    links: linkUrl.value.trim() ? [{ kind: linkKind.value, url: linkUrl.value.trim() }] : [],
  }
}

async function submit() {
  if (!ready.value || busy.value) {
    return
  }
  busy.value = true
  failure.value = null
  try {
    emit('created', await createPersona(body()))
  }
  catch (e) {
    failure.value = editErrorMessage(e, 'The known player was not added.')
  }
  finally {
    busy.value = false
  }
}
</script>

<template>
  <form class="space-y-4" data-create @submit.prevent="submit">
    <UFormField label="Name" :error="errors.displayName ?? undefined" required>
      <UInput v-model="displayName" placeholder="Caps" class="w-full" />
    </UFormField>
    <UFormField label="Slug" :error="errors.slug ?? undefined" :help="`/players/${slug || '…'}`" required>
      <UInput v-model="slug" placeholder="caps" class="w-full font-mono" @update:model-value="slugEdited = true" />
    </UFormField>
    <UFormField label="First account" :error="errors.account ?? undefined" help="Optional: more from the edit panel.">
      <div class="flex gap-2">
        <UInput v-model="riotId" placeholder="Name#TAG" class="flex-1" aria-label="Riot ID" />
        <USelect v-model="platformId" :items="ACCOUNT_PLATFORMS.map(p => ({ label: p, value: p }))" class="w-28" aria-label="Platform" />
      </div>
    </UFormField>
    <UFormField label="First link" :error="errors.link ?? undefined" help="Optional.">
      <div class="flex gap-2">
        <USelect v-model="linkKind" :items="LINK_KINDS.map(k => ({ label: k, value: k }))" class="w-32" aria-label="Link kind" />
        <UInput v-model="linkUrl" placeholder="https://…" class="flex-1" aria-label="Link URL" />
      </div>
    </UFormField>

    <UAlert v-if="failure" data-failure color="error" variant="subtle" icon="i-lucide-triangle-alert" :title="failure" />

    <div class="flex justify-end gap-2">
      <UButton label="Cancel" color="neutral" variant="ghost" @click="emit('cancel')" />
      <UButton type="submit" data-submit label="Add known player" icon="i-lucide-user-plus" :loading="busy" :disabled="!ready || busy" />
    </div>
  </form>
</template>
