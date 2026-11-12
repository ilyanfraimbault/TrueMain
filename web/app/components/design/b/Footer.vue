<script setup lang="ts">
// Variant B footer: a horizon of light along its top edge, the links in two
// short columns, and the wordmark carved into the wall at the bottom — dark
// until the cursor's light passes over it (`.b-spot`). Same links, build stamp
// and Riot disclaimer as `AppFooter`.
const explore = [
  { label: 'Champions', to: '/champions' },
  { label: 'Tier list', to: '/champions/tierlist' },
  { label: 'Matchup', to: '/matchup' },
  { label: 'Truemains', to: '/truemains' },
  { label: 'Desktop app', to: '/download' },
]
const company = [
  { label: 'About', to: '/about' },
  { label: 'Privacy', to: '/privacy' },
  { label: 'Terms', to: '/terms' },
]

const year = new Date().getUTCFullYear()
const { appEnv, appVersion } = useRuntimeConfig().public
const buildLabel = formatBuildLabel({ env: appEnv, version: appVersion })
</script>

<template>
  <footer class="b-footer">
    <span
      class="b-footer__horizon"
      aria-hidden="true"
    />
    <div class="b-footer__inner">
      <div class="b-footer__top">
        <div class="b-footer__brand">
          <NuxtLink
            to="/"
            class="b-logo inline-flex rounded-lg px-1.5 py-1 -mx-1.5"
            aria-label="TrueMain home"
          >
            <AppLogo class="text-lg" />
          </NuxtLink>
          <p class="b-footer__tagline">
            Champion builds, runes and skill orders from the players who truly main them.
          </p>
        </div>

        <nav
          aria-label="Footer"
          class="b-footer__nav"
        >
          <div>
            <p class="b-footer__heading">
              Explore
            </p>
            <ul>
              <li
                v-for="link in explore"
                :key="link.to"
              >
                <NuxtLink
                  :to="link.to"
                  class="b-footer__link"
                >
                  {{ link.label }}
                </NuxtLink>
              </li>
            </ul>
          </div>
          <div>
            <p class="b-footer__heading">
              TrueMain
            </p>
            <ul>
              <li
                v-for="link in company"
                :key="link.to"
              >
                <NuxtLink
                  :to="link.to"
                  class="b-footer__link"
                >
                  {{ link.label }}
                </NuxtLink>
              </li>
            </ul>
          </div>
        </nav>
      </div>

      <div
        class="b-footer__mark b-spot"
        aria-hidden="true"
      >
        TrueMain
      </div>

      <div class="b-footer__bottom">
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
