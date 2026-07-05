<template>
  <span class="inline-flex items-center gap-1.5 font-bold tracking-tight">
    <!-- M-check mark: an M whose last stroke becomes a checkmark — the "true"
         main, verified by the data. Same rose-gold ramp as the wordmark.
         The gradient id is shared between instances (header + footer render
         identical defs, which browsers resolve to the same paint). -->
    <svg
      viewBox="0 0 64 64"
      class="h-[1.15em] w-[1.15em]"
      aria-hidden="true"
    >
      <defs>
        <linearGradient
          id="tm-mark-grad"
          x1="0"
          y1="0"
          x2="0"
          y2="1"
        >
          <stop
            offset="0"
            stop-color="#f2cfa6"
          />
          <stop
            offset="0.55"
            stop-color="#e2917e"
          />
          <stop
            offset="1"
            stop-color="#8a3b44"
          />
        </linearGradient>
      </defs>
      <path
        d="M13 47V21l15 17L51 15"
        fill="none"
        stroke="url(#tm-mark-grad)"
        stroke-width="8"
        stroke-linecap="round"
        stroke-linejoin="round"
      />
    </svg>
    <span class="inline-flex items-baseline">
      <!-- "True" carries the rose→gold sheen (bg-clip-text gradient) that gives
           the wordmark its metallic rose-gold read; "Main" stays default ink.
           Light mode uses the deeper end of the ramp — the pale dark-mode stops
           (rosegold-300, gold) wash out on a white header. `forced-color-adjust:
           none` opts the clipped text out of Windows High Contrast, where the
           forced system text color would leave the transparent fill with no
           gradient to show through and "True" would vanish. -->
      <span
        class="bg-gradient-to-r from-rosegold-700 via-rosegold-600 to-rosegold-400 bg-clip-text text-transparent [forced-color-adjust:none] dark:from-rosegold-400 dark:via-rosegold-300 dark:to-gold"
      >True</span><span class="text-default">Main</span>
    </span>
  </span>
</template>
