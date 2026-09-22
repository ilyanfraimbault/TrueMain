<template>
  <span class="inline-flex items-center gap-1.5 font-bold tracking-tight">
    <!-- The site's M-check mark: an M whose last stroke becomes a checkmark.
         Same rose-gold ramp as the wordmark. -->
    <svg viewBox="0 0 64 64" class="h-[1.15em] w-[1.15em]" aria-hidden="true">
      <defs>
        <linearGradient id="tm-mark-grad" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stop-color="#f2cfa6" />
          <stop offset="0.55" stop-color="#e2917e" />
          <stop offset="1" stop-color="#8a3b44" />
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
      <span class="bg-gradient-to-r from-rosegold-400 via-rosegold-300 to-gold bg-clip-text text-transparent">True</span><span class="text-default">Main</span>
    </span>
  </span>
</template>
