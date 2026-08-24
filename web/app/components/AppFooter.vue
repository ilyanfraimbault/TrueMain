<script setup lang="ts">
const links = [
  { label: 'Champions', to: '/champions' },
  { label: 'Truemains', to: '/truemains' },
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
      container: 'border-t border-default lg:py-8',
      right: 'gap-x-0 flex-wrap',
    }"
  >
    <template #left>
      <p class="text-sm text-dimmed">
        TrueMain · {{ year }}
        <!-- Deliberately quiet: this is a build stamp for us, not copy for the
             reader. Absent entirely in dev, and on preprod it is the one thing
             on the page that says which build you are looking at. -->
        <span v-if="buildLabel" class="ml-1 text-xs opacity-70">{{ buildLabel }}</span>
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
  </UFooter>
</template>
