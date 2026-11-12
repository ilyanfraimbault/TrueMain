<script setup lang="ts">
// The TrueMain M-check mark alone (the same path as `AppLogo`), for the dock's
// home tile — the dock's first icon is the brand, the way Finder opens the
// macOS dock. Its own gradient id so it never resolves to another instance's.
</script>

<template>
  <svg
    viewBox="0 0 64 64"
    aria-hidden="true"
  >
    <defs>
      <linearGradient
        id="f-mark-grad"
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
      stroke="url(#f-mark-grad)"
      stroke-width="8"
      stroke-linecap="round"
      stroke-linejoin="round"
    />
  </svg>
</template>
