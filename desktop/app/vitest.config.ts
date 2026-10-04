import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vitest/config'

// The pure-function suite, the same shape as the `unit` project of
// `web/vitest.config.ts`: a bare happy-dom environment (it provides the
// `localStorage` the rank history is kept in), no Nuxt runtime.
export default defineConfig({
  test: {
    environment: 'happy-dom',
    include: ['tests/**/*.test.ts'],
  },
  resolve: {
    alias: {
      '~': fileURLToPath(new URL('./app', import.meta.url)),
      // The shared layer's alias (`web/layers/common/nuxt.config.ts`).
      '#shared': fileURLToPath(new URL('../../web/shared', import.meta.url)),
    },
  },
})
