<script setup lang="ts">
// Variant F ("Dock") — the desktop companion (#1719): same copy and capture as
// `HomeDesktopBetaPanel`. The capture tilts toward the cursor with a glare
// across its glass, and its edge catches the light like the panels do.
const DASHBOARD_SRCSET = '/desktop/dashboard-1180.webp 1180w, /desktop/dashboard.webp 2360w'
const DASHBOARD_SIZES = '(min-width: 1152px) 640px, (min-width: 768px) 56vw, 100vw'
</script>

<template>
  <section
    class="f-section f-split f-split--desktop"
    aria-labelledby="f-desktop-title"
  >
    <div
      v-reveal="{ effect: 'rise' }"
      class="f-split__copy"
    >
      <p class="eyebrow flex items-center gap-2">
        Desktop app
        <span class="f-beta">Beta</span>
      </p>
      <h2
        id="f-desktop-title"
        class="f-h2"
      >
        TrueMain, on your desktop.
      </h2>
      <p class="f-lede">
        Your games and your rank, read from your client. In champion select: picks from your own pool, the enemy lanes worked out, and the build against the draft.
      </p>
      <div class="mt-8 flex flex-wrap items-center gap-x-5 gap-y-3">
        <NuxtLink
          to="/download"
          class="f-cta f-cta--primary"
        >
          <UIcon
            name="i-lucide-download"
            class="size-4"
          />
          Get the beta
          <kbd class="f-key">6</kbd>
        </NuxtLink>
        <span class="flex items-center gap-2 text-xs text-dimmed">
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

    <div
      v-reveal="{ effect: 'rise', delay: 90 }"
      class="f-shot-wrap"
    >
      <div
        v-tilt="4"
        class="f-shot"
        data-f-spot
      >
        <SkeletonPicture
          src="/desktop/dashboard.webp"
          :srcset="DASHBOARD_SRCSET"
          :sizes="DASHBOARD_SIZES"
          :width="1180"
          :height="760"
          loading="lazy"
          alt="The TrueMain app's dashboard: rank, recent form, match history and champions."
          class="f-shot__picture"
        />
        <span
          class="f-shot__glare"
          aria-hidden="true"
        />
      </div>
    </div>
  </section>
</template>
