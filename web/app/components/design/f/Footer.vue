<script setup lang="ts">
// Variant F ("Dock"): the footer is the page's last line, nothing more — the
// dock already carries the navigation. The legal pages, the build stamp and
// Riot's disclaimer; its bottom padding keeps all of it clear of the dock.
const links = [
  { label: 'About', to: '/about' },
  { label: 'Privacy', to: '/privacy' },
  { label: 'Terms', to: '/terms' },
]

const year = new Date().getUTCFullYear()
const { appEnv, appVersion } = useRuntimeConfig().public
const buildLabel = formatBuildLabel({ env: appEnv, version: appVersion })
</script>

<template>
  <footer class="f-footer">
    <div class="f-footer__row">
      <p class="f-footer__stamp">
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
        class="f-footer__links"
        aria-label="Legal"
      >
        <NuxtLink
          v-for="link in links"
          :key="link.to"
          :to="link.to"
          class="f-footer__link"
        >
          {{ link.label }}
        </NuxtLink>
      </nav>
    </div>
    <RiotDisclaimer class="f-footer__legal" />
  </footer>
</template>
