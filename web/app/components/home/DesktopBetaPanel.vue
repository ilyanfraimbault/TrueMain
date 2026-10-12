<script setup lang="ts">
// The desktop companion's place on the home page (#1719, #1725): a banner right
// under the hero, above the strongest picks and their mains — the app is those
// two answers brought into champion select, so it is announced before them. It
// shows the app itself, whole: its dashboard, the screen it opens on. It says it
// is a beta up front. It sits on the page itself, like the hero above it, rather
// than in a card: the capture is the only raised surface.
</script>

<template>
  <section class="mx-auto max-w-6xl px-4 pb-20 md:px-6 lg:pb-28">
    <div class="relative isolate grid items-center gap-10 md:grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)] lg:gap-14">
      <div class="flex flex-col justify-center">
        <div class="flex items-center gap-2">
          <p class="eyebrow">
            Desktop app
          </p>
          <UBadge
            color="primary"
            variant="subtle"
            size="sm"
            label="Beta"
          />
        </div>
        <h2 class="mt-2 text-2xl font-semibold tracking-tight text-balance text-highlighted sm:text-3xl">
          TrueMain, on your desktop.
        </h2>
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
        <div
          aria-hidden="true"
          class="pointer-events-none absolute inset-x-[10%] top-[15%] -z-10 h-3/4 rounded-full bg-primary/12 blur-3xl"
        />
        <SkeletonPicture
          src="/desktop/dashboard.webp"
          :width="1180"
          :height="760"
          alt="The TrueMain app's dashboard: rank, recent form, match history and champions."
          loading="lazy"
          class="rounded-xl shadow-[0_30px_80px_-20px_rgb(0_0_0/0.9)] ring-1 ring-white/15"
        />
      </div>
    </div>
  </section>
</template>
