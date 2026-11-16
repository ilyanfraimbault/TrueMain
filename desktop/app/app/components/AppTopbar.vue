<script setup lang="ts">
/**
 * The bar over the content: the search (⌘K), and nothing else. No back/forward
 * arrows and no patch label — browser chrome that made the window read as a web
 * page; the sidebar is how the app is navigated. The app never *opens* on the
 * search — it opens on the player — but a champion, a known player or a truemain
 * is always two keystrokes away. It is the site's search (`AppSearch`), so both
 * apps find the same things.
 */
</script>

<template>
  <header class="flex items-center px-4">
    <AppSearch shortcut size="sm" class="mx-auto w-full max-w-md" />
  </header>
</template>
