<script setup lang="ts">
import type { NuxtError } from '#app'

defineProps<{ error: NuxtError }>()

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
        <p
          v-if="error.message"
          class="text-sm text-muted"
        >
          {{ error.message }}
        </p>
        <UButton @click="handleReturn">
          Go back home
        </UButton>
      </section>
    </main>
  </UApp>
</template>
