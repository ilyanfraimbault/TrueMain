<script setup lang="ts">
// The desktop companion's place on the home page (#1719, #1725): a banner right
// under the hero, above the tier-list teaser and its mains — the app is those
// two answers brought into champion select, so it is announced before them. It
// shows the app itself, whole: its dashboard, the screen it opens on. It says it
// is a beta up front. It sits on the page itself, like the hero above it, rather
// than in a card: the capture is the only raised surface.
//
// It is also the page's largest picture, what a visitor waits for: announced in the head so its
// fetch starts with the HTML, and at the size its box needs on most screens (#2425).
const DASHBOARD_SRCSET = '/desktop/dashboard-1180.webp 1180w, /desktop/dashboard.webp 2360w'
const DASHBOARD_SIZES = '(min-width: 1152px) 640px, (min-width: 768px) 58vw, 100vw'

useHead({
  link: [{
    rel: 'preload',
    as: 'image',
    href: '/desktop/dashboard-1180.webp',
    imagesrcset: DASHBOARD_SRCSET,
    imagesizes: DASHBOARD_SIZES,
    fetchpriority: 'high',
  }],
})
</script>

<template>
  <section class="mx-auto max-w-6xl px-4 pb-20 md:px-6 lg:pb-28">
    <div class="relative isolate grid items-center gap-10 md:grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)] lg:gap-14">
      <div class="flex flex-col justify-center">
        <div class="flex items-center gap-2">
          <h2 class="text-2xl font-semibold tracking-tight text-highlighted sm:text-3xl">
            Desktop app
          </h2>
          <UBadge
            color="primary"
            variant="subtle"
            size="sm"
            label="Beta"
          />
        </div>
        <p class="mt-3 max-w-md text-sm leading-relaxed text-muted sm:text-base">
          Your games and your rank, read from your client. In champion select: picks from your own pool, the enemy lanes worked out, and the build against the draft.
        </p>
        <div class="mt-6 flex flex-wrap items-center gap-x-4 gap-y-3">
          <UButton
            to="/download"
            size="lg"
            icon="i-lucide-download"
            label="Get the beta"
          />
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

      <!-- The app's dashboard, whole, at the app's own window proportions. -->
      <div class="relative">
        <SkeletonPicture
          src="/desktop/dashboard.webp"
          :srcset="DASHBOARD_SRCSET"
          :sizes="DASHBOARD_SIZES"
          :width="1180"
          :height="760"
          alt="The TrueMain app's dashboard: rank, recent form, match history and champions."
          priority
          class="rounded-xl shadow-[0_30px_80px_-20px_rgb(0_0_0/0.9)] ring-1 ring-white/15"
        />
      </div>
    </div>
  </section>
</template>
