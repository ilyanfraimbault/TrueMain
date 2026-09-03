<script setup lang="ts">
// Retired route (#1410): the candidates panel is the "Pipeline" tab of the
// Accounts hub. A redirect rather than a deletion so an existing bookmark or a
// link pasted into an issue still lands on the panel it named — including the
// deep link to one candidate's slide-over (`?candidate=`), which is why the
// query rides along instead of being dropped. Pipeline is the hub's default
// tab, so no `view` is added.
definePageMeta({
  redirect: to => ({ path: '/accounts', query: to.query }),
})
</script>

<template>
  <div />
</template>
