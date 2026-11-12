<script setup lang="ts">
import { formatCompactCount } from '~~/shared/utils/counts'

// Variant B hero: the eclipse is the light source and the headline sits under
// it — lit from above (a vertical ramp), swept once by a sheen as it rises in,
// and lit again wherever the cursor passes (`data-b-spot`, spotlight-engine.ts).
// The search field carries a light that orbits its edge. Every figure comes from
// the page's payloads; a missing one is left out, never invented.
const props = defineProps<{
  patch: string | null
  overviewPending: boolean
  gamesAnalyzed: number
  trackedTruemains: number | null
}>()

const fx = useFx()
// The WebGL eclipse renders on the server like everywhere else; a lite machine
// swaps it for the static CSS eclipse once mounted (a client-only decision, so
// the first client render matches the server's).
const lite = ref(false)
const heroEl = ref<HTMLElement | null>(null)
const offscreen = ref(false)
let observer: IntersectionObserver | null = null

onMounted(() => {
  lite.value = fx.value.level === 'lite'
  if (!heroEl.value) return
  // Park the hero's CSS loops (the orbiting edge light, the scroll cue) once it is gone.
  observer = new IntersectionObserver(([entry]) => {
    offscreen.value = !entry?.isIntersecting
  })
  observer.observe(heroEl.value)
})
onBeforeUnmount(() => observer?.disconnect())

const showGames = computed(() => !props.overviewPending && props.gamesAnalyzed > 0)
</script>

<template>
  <section
    ref="heroEl"
    class="b-hero"
    :class="{ 'is-parked': offscreen }"
  >
    <AppBackdrop v-if="!lite" />
    <div
      v-else
      aria-hidden="true"
      class="b-hero__eclipse"
    />
    <div
      aria-hidden="true"
      class="b-hero__fade"
    />

    <div class="b-hero__inner">
      <p class="b-hero__eyebrow">
        <span
          class="b-hero__pulse"
          aria-hidden="true"
        />
        <template v-if="patch">
          <span class="text-highlighted">Patch {{ patch }}</span>
          <span
            class="b-hero__sep"
            aria-hidden="true"
          />
        </template>
        <span>Champion intelligence</span>
      </p>

      <h1 class="b-hero__title">
        <span class="b-line"><span
          class="b-line__in b-lit"
          data-b-spot
          style="--i: 0"
        >Real builds from</span></span>
        <span class="b-line"><span
          class="b-line__in b-lit"
          data-b-spot
          style="--i: 1"
        ><span
          class="b-lit--rose"
          data-b-spot
        >real mains</span>.</span></span>
      </h1>

      <p class="b-hero__lede">
        Builds, runes and skill orders from the players who truly mastered your champion.
      </p>

      <div
        class="b-search"
        data-b-spot
      >
        <LazyAppSearch
          variant="field"
          size="lg"
          class="w-full"
          hydrate-on-idle
        />
      </div>

      <dl class="b-hero__stats">
        <div class="b-hero__stat">
          <dt>Main games analyzed</dt>
          <dd>
            <USkeleton
              v-if="overviewPending"
              class="h-7 w-20"
            />
            <FxCount
              v-else-if="showGames"
              :value="gamesAnalyzed"
              :format="formatCompactCount"
            />
            <span v-else>—</span>
          </dd>
        </div>
        <div
          v-if="trackedTruemains !== null"
          class="b-hero__stat"
        >
          <dt>Truemains tracked</dt>
          <dd>
            <FxCount
              :value="trackedTruemains"
              :format="formatCompactCount"
            />
          </dd>
        </div>
      </dl>
    </div>

    <div
      aria-hidden="true"
      class="b-hero__cue"
    >
      <span />
    </div>
  </section>
</template>
