<template>
  <span class="inline-flex items-baseline font-bold tracking-tight">
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
</template>
