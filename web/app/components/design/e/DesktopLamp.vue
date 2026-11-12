<script setup lang="ts">
// The desktop companion (#1719) under a pendant lamp, from round 1's
// "Spotlight": the lamp hangs over the app's dashboard and lights it, the rest
// of the capture falling into shade. The lamp leans toward the cursor and its
// pool of light slides across the capture with it — the stage is a
// `data-e-spot` target, so `--e-u` / `--e-spot` arrive from the light engine
// and the swing is pure CSS. At rest it sways on its cord. Same copy and
// capture as `HomeDesktopBetaPanel`.
const DASHBOARD_SRCSET = '/desktop/dashboard-1180.webp 1180w, /desktop/dashboard.webp 2360w'
const DASHBOARD_SIZES = '(min-width: 1280px) 680px, (min-width: 900px) 52vw, 100vw'
</script>

<template>
  <section
    class="e-section e-lampsec"
    aria-labelledby="e-desktop-title"
  >
    <div class="e-lampsec__grid">
      <div class="e-lampsec__copy">
        <p class="e-eyebrow flex items-center gap-2">
          Desktop app
          <span class="e-badge">Beta</span>
        </p>
        <h2
          id="e-desktop-title"
          class="e-title"
        >
          TrueMain, on your desktop.
        </h2>
        <p class="e-lede">
          Your games and your rank, read from your client. In champion select: picks from your own pool, the enemy lanes worked out, and the build against the draft.
        </p>
        <div class="mt-8 flex flex-wrap items-center gap-x-5 gap-y-3">
          <UButton
            to="/download"
            size="xl"
            icon="i-lucide-download"
            label="Get the beta"
            class="e-play"
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

      <div
        class="e-lamp"
        data-e-spot
      >
        <div
          class="e-lamp__pivot"
          aria-hidden="true"
        >
          <div class="e-lamp__arm">
            <span class="e-lamp__cord" />
            <svg
              class="e-lamp__shade"
              viewBox="0 0 120 64"
            >
              <defs>
                <linearGradient
                  id="e-lamp-metal"
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
                  id="e-lamp-bulb"
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
                fill="url(#e-lamp-metal)"
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
                fill="url(#e-lamp-bulb)"
              />
            </svg>
            <span class="e-lamp__cone" />
          </div>
        </div>

        <div class="e-lamp__screen">
          <SkeletonPicture
            src="/desktop/dashboard.webp"
            :srcset="DASHBOARD_SRCSET"
            :sizes="DASHBOARD_SIZES"
            :width="1180"
            :height="760"
            loading="lazy"
            alt="The TrueMain app's dashboard: rank, recent form, match history and champions."
            class="e-lamp__picture"
          />
          <span
            class="e-lamp__shadow"
            aria-hidden="true"
          />
          <span
            class="e-lamp__pool"
            aria-hidden="true"
          />
        </div>
      </div>
    </div>
  </section>
</template>
