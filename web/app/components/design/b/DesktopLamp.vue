<script setup lang="ts">
// The desktop companion (#1719) under a pendant lamp: a lamp hangs over the
// app's dashboard and lights it, the rest of the capture falling into shade. The
// lamp leans toward the cursor and the pool of light slides across the capture
// with it — the stage is a `.b-spot` target, so `--b-u` / `--b-spot` arrive from
// the spotlight engine and the swing is pure CSS. At rest it sways on its cord.
// Same copy and capture as `HomeDesktopBetaPanel`.
const DASHBOARD_SRCSET = '/desktop/dashboard-1180.webp 1180w, /desktop/dashboard.webp 2360w'
const DASHBOARD_SIZES = '(min-width: 1152px) 640px, (min-width: 768px) 58vw, 100vw'
</script>

<template>
  <section
    class="b-section b-lampsec"
    aria-labelledby="b-desktop-title"
  >
    <div class="b-lampsec__grid">
      <div
        v-reveal="{ effect: 'left' }"
        class="b-lampsec__copy"
      >
        <p class="b-head__eyebrow flex items-center gap-2">
          Desktop app
          <span class="b-nav__badge">Beta</span>
        </p>
        <h2
          id="b-desktop-title"
          class="b-head__title b-lit b-lit--scroll"
          data-b-spot
        >
          TrueMain, on your desktop.
        </h2>
        <p class="b-head__lede">
          Your games and your rank, read from your client. In champion select: picks from your own pool, the enemy lanes worked out, and the build against the draft.
        </p>
        <div class="mt-8 flex flex-wrap items-center gap-x-5 gap-y-3">
          <UButton
            v-magnetic="0.3"
            to="/download"
            size="lg"
            icon="i-lucide-download"
            label="Get the beta"
            class="b-cta"
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

      <div class="b-stage b-spot">
        <div
          class="b-stage__pivot"
          aria-hidden="true"
        >
          <div class="b-stage__lamp">
            <span class="b-lamp__cord" />
            <svg
              class="b-lamp__shade"
              viewBox="0 0 120 64"
            >
              <defs>
                <linearGradient
                  id="b-lamp-metal"
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
                  id="b-lamp-bulb"
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
                fill="url(#b-lamp-metal)"
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
                fill="url(#b-lamp-bulb)"
              />
            </svg>
            <span class="b-lamp__cone" />
          </div>
        </div>

        <div class="b-stage__screen">
          <SkeletonPicture
            src="/desktop/dashboard.webp"
            :srcset="DASHBOARD_SRCSET"
            :sizes="DASHBOARD_SIZES"
            :width="1180"
            :height="760"
            loading="lazy"
            alt="The TrueMain app's dashboard: rank, recent form, match history and champions."
            class="b-stage__picture"
          />
          <span
            class="b-stage__shadow"
            aria-hidden="true"
          />
          <span
            class="b-stage__pool"
            aria-hidden="true"
          />
        </div>
      </div>
    </div>
  </section>
</template>
