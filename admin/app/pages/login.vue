<script setup lang="ts">
import type { FormError, FormSubmitEvent } from '@nuxt/ui'

// The global auth middleware already redirects an authenticated visitor away
// from `/login`, so this page only ever renders for signed-out operators. It
// owns its own bare layout (no dashboard shell) via `layout: false`.
definePageMeta({
  layout: false,
})

const { fetch: refreshSession } = useUserSession()

const state = reactive({
  username: '',
  password: '',
})

const loading = ref(false)
const errorMessage = ref('')

function validate(state: { username: string, password: string }): FormError[] {
  const errors: FormError[] = []
  if (!state.username) errors.push({ name: 'username', message: 'Required' })
  if (!state.password) errors.push({ name: 'password', message: 'Required' })
  return errors
}

async function onSubmit(_event: FormSubmitEvent<typeof state>) {
  errorMessage.value = ''
  loading.value = true
  try {
    await $fetch('/api/auth/login', {
      method: 'POST',
      body: { username: state.username, password: state.password },
    })
    // Refresh the client session so `loggedIn` flips before we navigate;
    // otherwise the global middleware would bounce us straight back here.
    await refreshSession()
    await navigateTo('/')
  }
  catch {
    errorMessage.value = 'Invalid username or password.'
  }
  finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="min-h-svh flex items-center justify-center p-4 bg-default">
    <UCard class="w-full max-w-sm bg-elevated/25">
      <template #header>
        <div class="flex items-center gap-2">
          <UIcon name="i-lucide-shield" class="size-6 text-primary" />
          <div>
            <h1 class="text-lg font-semibold text-highlighted leading-tight">
              TrueMain Admin
            </h1>
            <p class="text-sm text-muted">
              Sign in to continue
            </p>
          </div>
        </div>
      </template>

      <UForm
        :state="state"
        :validate="validate"
        class="space-y-4"
        @submit="onSubmit"
      >
        <UFormField label="Username" name="username">
          <UInput
            v-model="state.username"
            placeholder="truemain"
            autocomplete="username"
            class="w-full"
          />
        </UFormField>

        <UFormField label="Password" name="password">
          <UInput
            v-model="state.password"
            type="password"
            placeholder="••••••••"
            autocomplete="current-password"
            class="w-full"
          />
        </UFormField>

        <UAlert
          v-if="errorMessage"
          color="error"
          variant="subtle"
          icon="i-lucide-circle-alert"
          :title="errorMessage"
        />

        <UButton
          type="submit"
          color="primary"
          block
          :loading="loading"
          label="Sign in"
        />
      </UForm>
    </UCard>
  </div>
</template>
