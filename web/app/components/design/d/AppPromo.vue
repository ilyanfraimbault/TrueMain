<script setup lang="ts">
// Variant D ("Cockpit"): the desktop companion (#1719) as a dashboard card. A
// small pendant lamp hangs from the card's ceiling over the app's dashboard and
// leans toward the cursor — the stage is a `data-d-spot` target, so `--d-u` and
// `--d-spot` arrive from the light engine and the lean is pure CSS. At rest it
// sways on its cord, only while the card is on screen. Same copy and capture
// as `HomeDesktopBetaPanel`.
const DASHBOARD_SRCSET = '/desktop/dashboard-1180.webp 1180w, /desktop/dashboard.webp 2360w'
const DASHBOARD_SIZES = '(min-width: 1280px) 460px, (min-width: 768px) 60vw, 100vw'

const card = ref<HTMLElement | null>(null)
const onScreen = ref(false)
let observer: IntersectionObserver | null = null
onMounted(() => {
  if (!card.value) return
  observer = new IntersectionObserver(([entry]) => {
    onScreen.value = Boolean(entry?.isIntersecting)
  })
  observer.observe(card.value)
})
onBeforeUnmount(() => observer?.disconnect())
</script>

<template>
  <section
    ref="card"
    class="d-promo d-panel"
    :class="{ 'is-on-screen': onScreen }"
    aria-labelledby="d-promo-title"
  >
    <span
      class="d-ring"
      data-d-spot
      aria-hidden="true"
    />

    <div
      class="d-stagelamp"
      data-d-spot
    >
      <div
        class="d-stagelamp__pivot"
        aria-hidden="true"
      >
        <div class="d-stagelamp__lamp">
          <span class="d-lamp__cord" />
          <svg
            class="d-lamp__shade"
            viewBox="0 0 120 64"
          >
            <defs>
              <linearGradient
                id="d-lamp-metal"
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
                id="d-lamp-bulb"
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
              fill="url(#d-lamp-metal)"
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
              fill="url(#d-lamp-bulb)"
            />
          </svg>
          <span class="d-lamp__cone" />
        </div>
      </div>

      <div class="d-stagelamp__screen">
        <SkeletonPicture
          src="/desktop/dashboard.webp"
          :srcset="DASHBOARD_SRCSET"
          :sizes="DASHBOARD_SIZES"
          :width="1180"
          :height="760"
          loading="lazy"
          alt="The TrueMain app's dashboard: rank, recent form, match history and champions."
          class="d-stagelamp__picture"
        />
        <span
          class="d-stagelamp__shadow"
          aria-hidden="true"
        />
        <span
          class="d-stagelamp__pool"
          aria-hidden="true"
        />
      </div>
    </div>

    <div class="d-promo__copy">
      <p class="d-promo__eyebrow">
        Desktop app
        <span class="d-sb__badge">Beta</span>
      </p>
      <h2
        id="d-promo-title"
        class="d-promo__title"
      >
        TrueMain, on your desktop.
      </h2>
      <p class="d-promo__lede">
        Your games and your rank, read from your client. In champion select: picks from your own pool, the enemy lanes worked out, and the build against the draft.
      </p>
      <div class="d-promo__actions">
        <UButton
          v-magnetic="0.25"
          to="/download"
          icon="i-lucide-download"
          label="Get the beta"
          class="d-cta"
        />
        <span class="d-promo__os">
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
      </div>
    </div>
  </section>
</template>
