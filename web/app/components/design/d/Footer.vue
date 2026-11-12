<script setup lang="ts">
// Variant D ("Cockpit"): the content column's foot. The wordmark is etched in
// the floor and only the cursor's light brings it out (`data-d-spot`, the light
// engine); the links, the build stamp and Riot's notice sit under it.
const links = [
  { label: 'Champions', to: '/champions' },
  { label: 'Truemains', to: '/truemains' },
  { label: 'Desktop app', to: '/download' },
  { label: 'About', to: '/about' },
  { label: 'Privacy', to: '/privacy' },
  { label: 'Terms', to: '/terms' },
]

const year = new Date().getUTCFullYear()

// Same build stamp as `AppFooter`: empty in dev, the version on a deploy.
const { appEnv, appVersion } = useRuntimeConfig().public
const buildLabel = formatBuildLabel({ env: appEnv, version: appVersion })
</script>

<template>
  <footer class="d-foot">
    <p
      class="d-foot__mark"
      data-d-spot
      aria-hidden="true"
    >
      TrueMain
    </p>

    <div class="d-foot__row">
      <p class="d-foot__stamp">
        TrueMain · {{ year }}
        <span
          v-if="buildLabel"
          class="opacity-70"
        ><span
          class="mx-2"
          aria-hidden="true"
        >|</span>{{ buildLabel }}</span>
      </p>
      <nav
        aria-label="Footer"
        class="d-foot__links"
      >
        <NuxtLink
          v-for="link in links"
          :key="link.to"
          :to="link.to"
          class="d-foot__link"
        >
          {{ link.label }}
        </NuxtLink>
      </nav>
    </div>

    <RiotDisclaimer class="d-foot__legal" />
  </footer>
</template>
