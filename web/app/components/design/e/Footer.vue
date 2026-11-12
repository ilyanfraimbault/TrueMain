<script setup lang="ts">
// Variant E footer: the content column's last row. The brand and the links on
// one line, the wordmark carved into the floor — dark until the cursor's light
// passes over it (`data-e-spot`) — then the build stamp and the Riot
// disclaimer. Same links, stamp and disclaimer as `AppFooter`.
const links = [
  { label: 'Champions', to: '/champions' },
  { label: 'Tier list', to: '/champions/tierlist' },
  { label: 'Truemains', to: '/truemains' },
  { label: 'Desktop app', to: '/download' },
  { label: 'About', to: '/about' },
  { label: 'Privacy', to: '/privacy' },
  { label: 'Terms', to: '/terms' },
]

const year = new Date().getUTCFullYear()
const { appEnv, appVersion } = useRuntimeConfig().public
const buildLabel = formatBuildLabel({ env: appEnv, version: appVersion })
</script>

<template>
  <footer class="e-footer">
    <div class="e-footer__inner">
      <div class="e-footer__top">
        <p class="e-footer__tagline">
          Champion builds, runes and skill orders from the players who truly main them.
        </p>
        <nav
          aria-label="Footer"
          class="e-footer__nav"
        >
          <NuxtLink
            v-for="link in links"
            :key="link.to"
            :to="link.to"
            class="e-footer__link"
          >
            {{ link.label }}
          </NuxtLink>
        </nav>
      </div>

      <div
        class="e-footer__mark"
        data-e-spot
        aria-hidden="true"
      >
        TrueMain
      </div>

      <div class="e-footer__bottom">
        <p class="font-mono text-xs text-dimmed">
          TrueMain · {{ year }}
          <span
            v-if="buildLabel"
            class="opacity-70"
          ><span
            class="mx-2 text-muted"
            aria-hidden="true"
          >|</span>{{ buildLabel }}</span>
        </p>
        <RiotDisclaimer class="max-w-3xl" />
      </div>
    </div>
  </footer>
</template>
