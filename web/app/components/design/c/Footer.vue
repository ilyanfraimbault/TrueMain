<script setup lang="ts">
/**
 * Design exploration, variant C — the closing credits. Link columns over a
 * giant wordmark that rises letter by letter out of a rose-gold horizon (the
 * eclipse's rim, drawn in CSS), each letter lifting under the cursor; the build
 * stamp and the Riot disclaimer underneath.
 */
const columns = [
  {
    title: 'Explore',
    links: [
      { label: 'Champions', to: '/champions' },
      { label: 'Tier list', to: '/champions/tierlist' },
      { label: 'Matchup', to: '/matchup' },
      { label: 'Truemains', to: '/truemains' },
    ],
  },
  {
    title: 'Yours',
    links: [
      { label: 'Favorites', to: '/truemains/favorites' },
      { label: 'Desktop app', to: '/download' },
    ],
  },
  {
    title: 'TrueMain',
    links: [
      { label: 'About', to: '/about' },
      { label: 'Privacy', to: '/privacy' },
      { label: 'Terms', to: '/terms' },
    ],
  },
]

const WORD = 'TRUEMAIN'.split('')
const year = new Date().getUTCFullYear()
const { appEnv, appVersion } = useRuntimeConfig().public
const buildLabel = formatBuildLabel({ env: appEnv, version: appVersion })

function toTop() {
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches
  window.scrollTo({ top: 0, behavior: reduced ? 'auto' : 'smooth' })
}
</script>

<template>
  <footer class="c-footer">
    <div class="c-footer__top">
      <div class="c-footer__intro">
        <AppLogo class="text-xl" />
        <p>Champion builds, runes and skill orders from the players who truly main them.</p>
        <button
          v-magnetic="0.35"
          type="button"
          class="c-footer__totop"
          @click="toTop"
        >
          <UIcon
            name="i-lucide-arrow-up"
            class="size-4"
          />
          <span class="sr-only">Back to top</span>
        </button>
      </div>
      <nav
        class="c-footer__cols"
        aria-label="Footer"
      >
        <div
          v-for="col in columns"
          :key="col.title"
          class="c-footer__col"
        >
          <p class="c-mono">
            {{ col.title }}
          </p>
          <ul>
            <li
              v-for="link in col.links"
              :key="link.to"
            >
              <NuxtLink
                :to="link.to"
                class="c-footer__link"
              >
                <span
                  class="c-roll"
                  :data-text="link.label"
                ><span>{{ link.label }}</span></span>
              </NuxtLink>
            </li>
          </ul>
        </div>
      </nav>
    </div>

    <div
      class="c-footer__mark"
      aria-hidden="true"
    >
      <div class="c-footer__horizon" />
      <div
        v-reveal.stagger="{ effect: 'c-letter', step: 55 }"
        class="c-footer__word"
      >
        <span
          v-for="(letter, i) in WORD"
          :key="i"
          class="c-footer__letter"
          :class="{ 'is-true': i < 4 }"
        >{{ letter }}</span>
      </div>
    </div>

    <div class="c-footer__base">
      <p class="c-mono">
        © {{ year }} TrueMain
        <span v-if="buildLabel"><span
          class="c-footer__sep"
          aria-hidden="true"
        >/</span>{{ buildLabel }}</span>
      </p>
      <RiotDisclaimer class="c-footer__legal" />
    </div>
  </footer>
</template>
