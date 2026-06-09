<script setup lang="ts">
// TODO(panel): Champions admin panel.
// Map championId -> name/icon via `/api/static/champions` (already wired) and
// surface per-champion ops/data from `/api/ops/*`. Replace <ComingSoon> with
// the real table/filters; keep the UDashboardPanel chrome below intact.
</script>

<template>
  <UDashboardPanel id="champions">
    <template #header>
      <UDashboardNavbar title="Champions" icon="i-lucide-swords">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
      </UDashboardNavbar>

      <UDashboardToolbar>
        <template #left>
          <!-- TODO(panel): champion search / role filter placeholders. -->
          <UInput icon="i-lucide-search" placeholder="Search champions…" disabled />
        </template>
      </UDashboardToolbar>
    </template>

    <template #body>
      <ComingSoon
        title="Champions"
        icon="i-lucide-swords"
        description="Inspect and manage per-champion data and overrides."
      />
    </template>
  </UDashboardPanel>
</template>
