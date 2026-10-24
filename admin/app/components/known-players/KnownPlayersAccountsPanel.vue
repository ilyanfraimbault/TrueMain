<script setup lang="ts">
// A known player's accounts in the edit panel (#2019): each with its source and
// whether TrueMain tracks it, an unlink that asks twice (the player's sources can
// no longer link it back), and the form that adds one by hand. Emits; the
// container calls the API.
import type { PersonaAccountInput, PersonaAdminAccount } from '~~/shared/types/ops'
import { computed, ref } from 'vue'
import { ACCOUNT_PLATFORMS, accountInput, accountLabel } from '~~/shared/utils/known-players'

const props = defineProps<{
  accounts: PersonaAdminAccount[]
  busy: boolean
}>()

const emit = defineEmits<{
  unlink: [account: PersonaAdminAccount]
  add: [account: PersonaAccountInput]
}>()

const armed = ref<string | null>(null)

function unlink(account: PersonaAdminAccount) {
  if (props.busy) {
    return
  }
  if (armed.value !== account.id) {
    armed.value = account.id
    return
  }
  armed.value = null
  emit('unlink', account)
}

const riotId = ref('')
const platformId = ref(ACCOUNT_PLATFORMS[0]!)
const platformItems = ACCOUNT_PLATFORMS.map(p => ({ label: p, value: p }))
const parsed = computed(() => accountInput(riotId.value, platformId.value))
const error = computed(() => riotId.value.trim() && 'error' in parsed.value ? parsed.value.error : null)

function add() {
  const value = parsed.value
  if (props.busy || 'error' in value) {
    return
  }
  emit('add', value)
  riotId.value = ''
}

const tracked = computed(() => props.accounts.filter(a => a.riotAccountId).length)
</script>

<template>
  <section class="space-y-2">
    <p class="text-xs uppercase text-muted">
      Accounts · {{ accounts.length }} ({{ tracked }} tracked)
    </p>
    <ul class="space-y-1 text-sm">
      <li v-for="account in accounts" :key="account.id" data-account class="flex items-center gap-2 rounded px-2 py-1 hover:bg-elevated/40">
        <span class="text-highlighted truncate">{{ accountLabel(account) }}</span>
        <span class="font-mono text-xs text-muted">{{ account.platformId }}</span>
        <span class="ml-auto text-xs text-dimmed shrink-0">{{ account.source }}{{ account.riotAccountId ? '' : ' · untracked' }}</span>
        <UButton
          :data-unlink="account.id"
          icon="i-lucide-unlink"
          size="xs"
          color="error"
          :variant="armed === account.id ? 'solid' : 'ghost'"
          :label="armed === account.id ? 'Confirm unlink' : undefined"
          :aria-label="`Unlink ${account.gameName}#${account.tagLine}`"
          :disabled="busy"
          @click="unlink(account)"
        />
      </li>
      <li v-if="accounts.length === 0" class="text-xs text-dimmed px-2">
        No account.
      </li>
    </ul>
    <p v-if="armed" class="text-xs text-muted px-2">
      Unlinked, the account leaves the page, and this player's sources can no longer link it back.
    </p>

    <form class="flex flex-wrap items-start gap-2 pt-1" data-add-account @submit.prevent="add">
      <UFormField :error="error ?? undefined" class="flex-1 min-w-48">
        <UInput v-model="riotId" placeholder="Name#TAG" icon="i-lucide-user-plus" class="w-full" aria-label="Riot ID to add" />
      </UFormField>
      <USelect v-model="platformId" :items="platformItems" class="w-28" aria-label="Platform" />
      <UButton type="submit" label="Add account" color="neutral" variant="subtle" :disabled="busy || !riotId.trim() || Boolean(error)" />
    </form>
  </section>
</template>
