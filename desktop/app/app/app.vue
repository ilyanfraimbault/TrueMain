<script setup lang="ts">
const { state, screen, ready } = useLcuState()

useHead({ title: 'TrueMain' })
</script>

<template>
  <UApp>
    <div class="h-screen bg-default text-default">
      <!--
        `ready` covers the gap between mount and the first read of the Rust
        state. Rendering the no-client screen during it would tell the player
        their client is closed when it is not.
      -->
      <div v-if="!ready" class="flex h-full items-center justify-center">
        <UIcon name="i-lucide-loader-circle" class="size-6 animate-spin text-dimmed" />
      </div>

      <NoClientScreen v-else-if="screen === 'no-client'" />
      <DraftScreen v-else-if="screen === 'draft' && state.draft" :draft="state.draft" />
      <DashboardScreen v-else :state="state" />
    </div>
  </UApp>
</template>
