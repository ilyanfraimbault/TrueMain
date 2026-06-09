<script setup lang="ts">
// TODO(panel): Seed a main.
// Form that POSTs a new tracked main to `/api/ops/*`. Replace <ComingSoon>
// with the real form; keep the chrome below intact.
</script>

<template>
  <UDashboardPanel id="seed">
    <template #header>
      <UDashboardNavbar title="Seed a main" icon="i-lucide-user-plus">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
      </UDashboardNavbar>

      <UDashboardToolbar>
        <template #left>
          <!-- TODO(panel): region / queue filter placeholders. -->
          <UInput icon="i-lucide-search" placeholder="Riot ID…" disabled />
        </template>
      </UDashboardToolbar>
    </template>

    <template #body>
      <ComingSoon
        title="Seed a main"
        icon="i-lucide-user-plus"
        description="Register a new tracked main for ingestion."
      />
    </template>
  </UDashboardPanel>
</template>
