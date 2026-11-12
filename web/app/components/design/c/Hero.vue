<script setup lang="ts">
import { formatCompactCount } from '~~/shared/utils/counts'
import { documentTop, pinProgress, useScrollScene, writeVar } from './scroll-scenes'

/**
 * Design exploration, variant C — the opening shot. A giant headline rises out
 * of its masks over the rose-gold eclipse while embers drift up through the
 * frame. The stage is pinned for a short run: scrolling pushes the camera into
 * the eclipse (it grows, climbs and dims), the headline's lines slide apart,
 * the embers race upward and dissolve — then the page carries on.
 *
 * HUD details are either decorative (ticks, viewfinder corners, plainly so) or
 * real: the patch the site serves and the lifetime games and truemains counts.
 */
const props = defineProps<{
  patchVersion: string | null
  overviewPending: boolean
  gamesAnalyzed: number
  trackedTruemains: number | null
}>()

const root = ref<HTMLElement | null>(null)
let progress = 0
let top = 0
let height = 0
let pinned = true
const cache: Record<string, number> = {}

useScrollScene({
  measure() {
    if (!root.value) return
    pinned = !window.matchMedia('(prefers-reduced-motion: reduce)').matches
    top = documentTop(root.value)
    height = root.value.offsetHeight
  },
  update({ y, vh }) {
    if (!root.value) return
    progress = pinned ? pinProgress(y, top, height, vh) : 0
    writeVar(root.value, '--hp', progress, cache)
  },
})

const embersProgress = () => progress
const showStats = computed(() => !props.overviewPending)
</script>

<template>
  <section
    ref="root"
    class="c-hero"
    aria-labelledby="c-hero-title"
  >
    <div class="c-hero__stage">
      <div
        class="c-hero__eclipse"
        aria-hidden="true"
      >
        <AppBackdrop />
      </div>
      <DesignCEmbers
        class="c-hero__embers"
        :progress="embersProgress"
      />
      <div
        class="c-hero__vignette"
        aria-hidden="true"
      />

      <!-- Viewfinder corners and the left rail: decoration, nothing measured. -->
      <div
        class="c-hero__frame"
        aria-hidden="true"
      >
        <i /><i /><i /><i />
      </div>
      <div
        class="c-hero__rail"
        aria-hidden="true"
      >
        <span class="c-hero__rail-ticks" />
        <span class="c-hero__rail-dot" />
      </div>

      <div class="c-hero__content">
        <div class="c-hero__meta">
          <span class="c-mono c-hero__meta-item">
            <span class="c-hero__pulse" />Champion intelligence
          </span>
          <span
            class="c-hero__meta-rule"
            aria-hidden="true"
          />
          <span class="c-mono c-hero__meta-item">
            Patch
            <USkeleton
              v-if="overviewPending"
              class="inline-block h-3 w-10 align-middle"
            />
            <b
              v-else-if="patchVersion"
              class="c-hero__patch"
            >{{ patchVersion }}</b>
          </span>
        </div>

        <h1
          id="c-hero-title"
          class="c-hero__title"
        >
          <span
            class="c-hero__line c-hero__line--a"
            style="--i: 0"
          ><span>Real builds</span></span>{{ ' ' }}
          <span
            class="c-hero__line c-hero__line--b"
            style="--i: 1"
          ><span>from real</span></span>{{ ' ' }}
          <span
            class="c-hero__line c-hero__line--c"
            style="--i: 2"
          ><span class="c-hero__accent">mains.</span></span>
        </h1>

        <div class="c-hero__aside">
          <p class="c-hero__lede">
            Builds, runes and skill orders from the players who truly mastered your champion.
          </p>
          <LazyAppSearch
            variant="field"
            size="lg"
            class="c-hero__search"
            hydrate-on-idle
          />
        </div>

        <div class="c-hero__bottom">
          <dl class="c-hero__stats">
            <div class="c-hero__stat">
              <dt class="c-mono">
                Main games analyzed
              </dt>
              <dd>
                <USkeleton
                  v-if="!showStats"
                  class="h-7 w-20"
                />
                <span v-else-if="gamesAnalyzed > 0">{{ formatCompactCount(gamesAnalyzed) }}</span>
              </dd>
            </div>
            <div
              v-if="trackedTruemains !== null"
              class="c-hero__stat"
            >
              <dt class="c-mono">
                Truemains tracked
              </dt>
              <dd>{{ formatCompactCount(trackedTruemains) }}</dd>
            </div>
          </dl>

          <div
            class="c-hero__cue"
            aria-hidden="true"
          >
            <span class="c-mono">Scroll</span>
            <span class="c-hero__cue-line" />
          </div>
        </div>
      </div>
    </div>
  </section>
</template>
