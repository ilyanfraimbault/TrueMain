<script setup lang="ts">
// TODO(panel): Database panel.
// Surface table sizes / row counts / maintenance actions from `/api/ops/*`.
// Replace <ComingSoon> with the real content; keep the chrome below intact.
</script>

<template>
  <UDashboardPanel id="database">
    <template #header>
      <UDashboardNavbar title="Database" icon="i-lucide-database">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
      </UDashboardNavbar>

      <UDashboardToolbar>
        <template #left>
          <!-- TODO(panel): schema / table filter placeholders. -->
          <UInput icon="i-lucide-search" placeholder="Filter tables…" disabled />
        </template>
      </UDashboardToolbar>
    </template>

    <template #body>
      <ComingSoon
        title="Database"
        icon="i-lucide-database"
        description="Table sizes, row counts, and maintenance operations."
      />
    </template>
  </UDashboardPanel>
</template>
