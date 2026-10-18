<script setup lang="ts">
const links = [
  { label: 'Champions', to: '/champions' },
  { label: 'Truemains', to: '/truemains' },
  { label: 'Desktop app', to: '/download' },
  { label: 'About', to: '/about' },
  { label: 'Privacy', to: '/privacy' },
  { label: 'Terms', to: '/terms' },
]

const year = new Date().getUTCFullYear()

// Which build is serving this page. Empty in dev, so the label is absent
// locally and only appears on a real deploy — see app/utils/app-version.ts for
// why prod prints the bare version and preprod names itself.
// Not a computed: runtime config is fixed for the life of the process, and it
// is serialised into the payload, so server and client resolve the same label
// and hydration can't disagree.
const { appEnv, appVersion } = useRuntimeConfig().public
const buildLabel = formatBuildLabel({ env: appEnv, version: appVersion })
</script>

<template>
  <UFooter
    :ui="{
      container: 'border-t border-default pb-4 lg:pt-8 lg:pb-4',
      right: 'gap-x-0 flex-wrap',
      bottom: 'pt-0 pb-8 lg:pt-0 lg:pb-8',
    }"
  >
    <template #left>
      <p class="font-mono text-xs text-dimmed">
        TrueMain · {{ year }}
        <!-- Deliberately quiet: this is a build stamp for us, not copy for the
             reader. Absent entirely in dev, and on preprod it is the one thing
             on the page that says which build you are looking at. -->
        <span v-if="buildLabel" class="opacity-70"><span class="mx-2 text-muted" aria-hidden="true">|</span>{{ buildLabel }}</span>
      </p>
    </template>

    <template #right>
      <UButton
        v-for="link in links"
        :key="link.label"
        :label="link.label"
        :to="link.to"
        color="neutral"
        variant="link"
        class="font-light"
        size="sm"
      />
    </template>

    <template #bottom>
      <UContainer>
        <RiotDisclaimer class="mx-auto max-w-3xl text-center lg:mx-0 lg:text-left" />
      </UContainer>
    </template>
  </UFooter>
</template>
