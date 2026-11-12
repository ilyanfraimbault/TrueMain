<script setup lang="ts">
// The desktop companion (#1719) as a tile: a small window of the real app,
// cycling through its three screens (dashboard, champion select, in game) while
// the tile is on screen, under a pendant lamp that leans toward the cursor and
// slides its pool of light across the window (`--g-u` / `--g-spot` from the
// light engine, so the lean is pure CSS). Same copy as `HomeDesktopBetaPanel`.
const props = defineProps<{ index: number }>()

const ipx = useImage()
const screens = [
  { src: '/desktop/dashboard.webp', alt: 'dashboard' },
  { src: '/desktop/champion-select.webp', alt: 'champion select' },
  { src: '/desktop/game.webp', alt: 'in game' },
].map(screen => ({ ...screen, url: ipx(screen.src, { width: 760, format: 'webp' }) }))

// The screens only cycle (and the lamp only sways) while the tile is visible.
const stage = ref<HTMLElement | null>(null)
const live = ref(false)
let observer: IntersectionObserver | null = null
onMounted(() => {
  if (!stage.value) return
  observer = new IntersectionObserver(([entry]) => {
    live.value = entry?.isIntersecting ?? false
  })
  observer.observe(stage.value)
})
onBeforeUnmount(() => observer?.disconnect())
</script>

<template>
  <DesignGTile
    area="app"
    :index="props.index"
    to="/download"
    label="Desktop app"
    icon="i-lucide-monitor-down"
    class="g-app"
  >
    <template #label-end>
      <span class="g-badge">Beta</span>
    </template>

    <template #art>
      <span
        ref="stage"
        class="g-app__stage"
        :class="{ 'is-live': live }"
        aria-hidden="true"
      >
        <span class="g-app__pivot">
          <span class="g-app__lamp">
            <span class="g-lamp__cord" />
            <svg
              class="g-lamp__shade"
              viewBox="0 0 120 64"
            >
              <defs>
                <linearGradient
                  id="g-lamp-metal"
                  x1="0"
                  y1="0"
                  x2="1"
                  y2="0"
                >
                  <stop
                    offset="0"
                    stop-color="#1b1b20"
                  />
                  <stop
                    offset="0.42"
                    stop-color="#4e4e57"
                  />
                  <stop
                    offset="0.55"
                    stop-color="#2a2a31"
                  />
                  <stop
                    offset="1"
                    stop-color="#121216"
                  />
                </linearGradient>
                <radialGradient
                  id="g-lamp-bulb"
                  cx="0.5"
                  cy="0"
                  r="0.8"
                >
                  <stop
                    offset="0"
                    stop-color="#fff4ea"
                  />
                  <stop
                    offset="0.5"
                    stop-color="#f2cfa6"
                  />
                  <stop
                    offset="1"
                    stop-color="#e58f83"
                    stop-opacity="0"
                  />
                </radialGradient>
              </defs>
              <path
                d="M52 2h16v8c22 4 44 22 50 50H2C8 32 30 14 52 10z"
                fill="url(#g-lamp-metal)"
              />
              <path
                d="M52 2h16v8c22 4 44 22 50 50H2C8 32 30 14 52 10z"
                fill="none"
                stroke="rgb(255 255 255 / 0.14)"
                stroke-width="1"
              />
              <ellipse
                cx="60"
                cy="60"
                rx="58"
                ry="4"
                fill="url(#g-lamp-bulb)"
              />
            </svg>
            <span class="g-lamp__cone" />
          </span>
        </span>

        <span class="g-app__window">
          <span class="g-app__bar">
            <i /><i /><i />
          </span>
          <span class="g-app__screens">
            <img
              v-for="(screen, i) in screens"
              :key="screen.src"
              :src="screen.url"
              alt=""
              width="760"
              height="490"
              loading="lazy"
              decoding="async"
              class="g-app__screen"
              :style="{ '--s': i }"
            >
          </span>
          <span class="g-app__pool" />
        </span>
      </span>
    </template>

    <span class="g-app__copy">
      <span class="g-app__title">TrueMain, on your desktop.</span>
      <span class="g-app__lede">In champion select: picks from your own pool, the enemy lanes worked out, and the build against the draft.</span>
      <span class="g-app__os">
        <UIcon
          name="i-simple-icons-apple"
          class="size-3.5"
        />
        <UIcon
          name="i-simple-icons-windows"
          class="size-3"
        />
        macOS and Windows
      </span>
    </span>
  </DesignGTile>
</template>
