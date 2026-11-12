<script setup lang="ts">
// Variant H footer: the links in two short columns, the build stamp and the
// Riot disclaimer, and the wordmark set into the wall below them — dark until
// the cursor's light reaches it (`data-h-spot`, light-engine.ts). Its bottom
// padding clears the floating tab bar, so the last line is never under glass.
const explore = [
  { label: 'Champions', to: '/champions' },
  { label: 'Tier list', to: '/champions/tierlist' },
  { label: 'Matchup', to: '/matchup' },
  { label: 'Truemains', to: '/truemains' },
  { label: 'Favorites', to: '/truemains/favorites' },
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
  <footer class="h-footer">
    <div class="h-footer__inner">
      <div class="h-footer__top">
        <div class="h-footer__brand">
          <NuxtLink
            to="/"
            class="h-footer__logo"
            aria-label="TrueMain home"
          >
            <AppLogo class="text-lg" />
          </NuxtLink>
          <p class="h-footer__tagline">
            Champion builds, runes and skill orders from the players who truly main them.
          </p>
        </div>

        <nav
          aria-label="Footer"
          class="h-footer__nav"
        >
          <div>
            <p class="h-footer__heading">
              Explore
            </p>
            <ul>
              <li
                v-for="link in explore"
                :key="link.to"
              >
                <NuxtLink
                  :to="link.to"
                  class="h-footer__link"
                >
                  {{ link.label }}
                </NuxtLink>
              </li>
            </ul>
          </div>
          <div>
            <p class="h-footer__heading">
              TrueMain
            </p>
            <ul>
              <li
                v-for="link in company"
                :key="link.to"
              >
                <NuxtLink
                  :to="link.to"
                  class="h-footer__link"
                >
                  {{ link.label }}
                </NuxtLink>
              </li>
            </ul>
          </div>
        </nav>
      </div>

      <div
        class="h-footer__mark"
        data-h-spot
        aria-hidden="true"
      >
        TrueMain
      </div>

      <div class="h-footer__bottom">
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
