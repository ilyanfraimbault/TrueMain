import { defineNuxtModule } from '@nuxt/kit'

// Keeps `onServerPrefetch` calls in the client bundle (#1590).
//
// Vue's `useId()` derives every id from the component's position *and* from
// how many async boundaries were opened before it: a component whose setup
// registers a `serverPrefetch` hook opens one (`markAsyncBoundary`), and every
// id generated after it — in its subtree and in its later siblings — gets a
// different prefix. Server and client must therefore register the same hooks
// on the same components, or every id after the first divergence mismatches.
//
// A production build breaks that: Nuxt's composable tree-shaking rewrites
// `onServerPrefetch(...)` to `false && onServerPrefetch(...)` in the client
// bundle, so a component that calls it unconditionally opens a boundary during
// SSR and none during hydration. `@nuxt/icon`'s CSS-mode icon (behind every
// `UIcon`) is such a component, so each icon rendered above a Reka/Nuxt UI
// widget shifted that widget's ids (`v-2-3` on the server, `v-0-3` in the
// browser) — the "Hydration completed but contains mismatches" on the champion
// page. Dev builds skip the tree-shaking, which is why only production showed it.
//
// Nuxt's own `useAsyncData` already compensates by hand (it seeds
// `instance.sp = []` on the client for that reason); dropping `onServerPrefetch`
// from the client tree-shake list applies the same fix to every caller. The
// hook is never invoked in the browser, so keeping it costs one registration.
export default defineNuxtModule({
  meta: { name: 'truemain-keep-server-prefetch-on-client' },
  setup(_options, nuxt) {
    const clientComposables = nuxt.options.optimization.treeShake.composables.client
    const vue = clientComposables.vue
    if (vue) clientComposables.vue = vue.filter(name => name !== 'onServerPrefetch')
  },
})
