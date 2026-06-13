<template>
  <UApp :tooltip="{ delayDuration: 0 }">
    <NuxtRouteAnnouncer />
    <AppBackdrop />
    <AppHeader />

    <UMain>
      <NuxtPage />
    </UMain>

    <AppFooter />
  </UApp>
</template>
