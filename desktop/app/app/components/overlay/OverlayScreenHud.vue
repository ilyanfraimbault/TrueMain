<template>
  <!-- The game's own HUD on the layout editor's screen, so the panels can keep clear of it. -->
  <div class="pointer-events-none absolute bottom-[1.5%] right-[1%] aspect-square w-[14%] rounded border border-dashed border-default/80" />
  <div class="pointer-events-none absolute bottom-[1%] left-1/2 h-[12%] w-[30%] -translate-x-1/2 rounded border border-dashed border-default/80" />
  <span class="pointer-events-none absolute bottom-[3%] right-[3%] text-[10px] text-dimmed">Minimap</span>
  <span class="pointer-events-none absolute bottom-[5%] left-1/2 -translate-x-1/2 text-[10px] text-dimmed">Abilities and items</span>
</template>
