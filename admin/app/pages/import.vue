<script setup lang="ts">
// TODO(panel): Import OTP.
// Bulk-import one-trick-pony mains (paste / upload a list) via `/api/ops/*`.
// Replace <ComingSoon> with the real importer; keep the chrome below intact.
</script>

<template>
  <UDashboardPanel id="import">
    <template #header>
      <UDashboardNavbar title="Import OTP" icon="i-lucide-list-plus">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
      </UDashboardNavbar>

      <UDashboardToolbar>
        <template #left>
          <!-- TODO(panel): source / format filter placeholders. -->
          <UInput icon="i-lucide-search" placeholder="Filter import…" disabled />
        </template>
      </UDashboardToolbar>
    </template>

    <template #body>
      <ComingSoon
        title="Import OTP"
        icon="i-lucide-list-plus"
        description="Bulk-import one-trick-pony mains for ingestion."
      />
    </template>
  </UDashboardPanel>
</template>
