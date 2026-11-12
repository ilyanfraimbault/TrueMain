<script setup lang="ts">
/**
 * Design exploration, variant C — chapter 03, the desktop app. The dashboard
 * capture sits on a screen that tips up from the floor and settles as it
 * scrolls into view (a CSS scroll-driven animation where the engine has them,
 * still elsewhere), with the app's own copy beside it.
 */
const DASHBOARD_SRCSET = '/desktop/dashboard-1180.webp 1180w, /desktop/dashboard.webp 2360w'
const DASHBOARD_SIZES = '(min-width: 1280px) 1100px, 92vw'

const points = [
  { label: 'Your client', text: 'Your games and your rank, read from your client.' },
  { label: 'Your pool', text: 'In champion select, picks from your own pool.' },
  { label: 'The draft', text: 'The enemy lanes worked out, and the build against the draft.' },
]
</script>

<template>
  <section
    class="c-desk"
    aria-labelledby="c-desk-title"
  >
    <DesignCChapter
      id="c-desk-title"
      index="03"
      kicker="Desktop app"
      :lines="['TrueMain,', 'on your desktop.']"
      meta="Beta · macOS and Windows"
    />

    <div class="c-desk__scene">
      <div
        class="c-desk__glow"
        aria-hidden="true"
      />
      <div class="c-desk__screen">
        <SkeletonPicture
          src="/desktop/dashboard.webp"
          :srcset="DASHBOARD_SRCSET"
          :sizes="DASHBOARD_SIZES"
          :width="1180"
          :height="760"
          loading="lazy"
          alt="The TrueMain app's dashboard: rank, recent form, match history and champions."
          class="c-desk__img"
        />
        <span
          class="c-desk__glare"
          aria-hidden="true"
        />
      </div>
    </div>

    <div class="c-desk__foot">
      <ol
        v-reveal.stagger="{ effect: 'rise', step: 90 }"
        class="c-desk__points"
      >
        <li
          v-for="(point, i) in points"
          :key="point.label"
        >
          <span class="c-mono"><b>{{ String(i + 1).padStart(2, '0') }}</b> {{ point.label }}</span>
          <p>{{ point.text }}</p>
        </li>
      </ol>
      <div class="c-desk__cta">
        <UButton
          v-magnetic="0.25"
          to="/download"
          size="xl"
          icon="i-lucide-download"
          label="Get the beta"
          class="c-btn-primary"
        />
        <span class="c-desk__platforms">
          <UIcon
            name="i-simple-icons-apple"
            class="size-3.5"
          />
          <UIcon
            name="i-simple-icons-windows"
            class="size-3"
          />
          <span class="c-mono">macOS and Windows</span>
        </span>
      </div>
    </div>
  </section>
</template>
