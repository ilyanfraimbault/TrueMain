<script setup lang="ts">
/**
 * The pages shared with the site (#1732) link to routes only the site has — a
 * known player's page, a player's champion page. Any such route lands here,
 * opens on the site instead, and the app stays where it was.
 *
 * A route rather than a guard on the router: Nuxt answers a navigation that
 * matches no route with its 404 page, even one a guard has cancelled.
 */
definePageMeta({
  middleware: defineNuxtRouteMiddleware((to) => {
    void openOnSite(to.fullPath)
    return false
  }),
})
</script>

<template>
  <div />
</template>
