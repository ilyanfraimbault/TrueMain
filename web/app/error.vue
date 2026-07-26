<script setup lang="ts">
import type { NuxtError } from '#app'
import { describeFetchError } from '~/utils/errors'

const props = defineProps<{ error: NuxtError }>()

// Same wording as every inline fetch error in the app, and never the raw
// NuxtError message — that can carry the proxied backend's technical detail
// (or, for a client fetch failure, the full request URL).
const message = computed(() => describeFetchError(props.error))

function handleReturn() {
  clearError({ redirect: '/' })
}
</script>

<template>
  <UApp>
    <main class="mx-auto grid min-h-screen max-w-2xl place-items-center p-8">
      <section class="space-y-4 text-center">
        <p class="text-6xl font-bold tabular-nums text-primary">
          {{ error.statusCode }}
        </p>
        <h1 class="text-balance text-2xl font-semibold">
          {{ error.statusMessage || 'Something went wrong' }}
        </h1>
        <p class="text-sm text-muted">
          {{ message }}
        </p>
        <UButton @click="handleReturn">
          Go back home
        </UButton>
      </section>
    </main>
  </UApp>
</template>
