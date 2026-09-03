<script setup lang="ts">
// Retired route (#1410): "Add mains" is a tab of the Accounts hub. Kept as a
// redirect so bookmarks and links in issues still resolve; the query rides along
// because nothing on this page used one and a future param should not be eaten.
definePageMeta({
  redirect: to => ({ path: '/accounts', query: { ...to.query, view: 'seed' } }),
})
</script>

<template>
  <div />
</template>
