<script setup lang="ts">
// Variant G footer: quiet on purpose. The links in two short columns, the build
// stamp and the Riot disclaimer, and the wordmark set into the ground at the
// bottom — barely there until the cursor's light passes over it. Same links,
// build stamp and disclaimer as `AppFooter`.
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
  <footer class="g-footer">
    <div class="g-footer__inner">
      <div class="g-footer__top">
        <div>
          <NuxtLink
            to="/"
            class="g-logo inline-flex"
            aria-label="TrueMain home"
          >
            <AppLogo class="text-[17px]" />
          </NuxtLink>
          <p class="g-footer__tagline">
            Champion builds, runes and skill orders from the players who truly main them.
          </p>
        </div>

        <nav
          aria-label="Footer"
          class="g-footer__nav"
        >
          <div>
            <p class="g-footer__heading">
              Explore
            </p>
            <ul>
              <li
                v-for="link in explore"
                :key="link.to"
              >
                <NuxtLink
                  :to="link.to"
                  class="g-footer__link"
                >
                  {{ link.label }}
                </NuxtLink>
              </li>
            </ul>
          </div>
          <div>
            <p class="g-footer__heading">
              TrueMain
            </p>
            <ul>
              <li
                v-for="link in company"
                :key="link.to"
              >
                <NuxtLink
                  :to="link.to"
                  class="g-footer__link"
                >
                  {{ link.label }}
                </NuxtLink>
              </li>
            </ul>
          </div>
        </nav>
      </div>

      <div
        class="g-footer__mark"
        data-g-spot
        aria-hidden="true"
      >
        TrueMain
      </div>

      <div class="g-footer__bottom">
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
