import { fileURLToPath } from 'node:url'
import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  // Same shape as `web/vitest.config.ts`, plus the Vue plugin: the admin suite
  // also mounts an SFC (`ProcessSummaryView`) to pin the shape -> render mapping,
  // which needs the single-file-component compiler.
  plugins: [vue()],
  test: {
    environment: 'happy-dom',
    include: ['tests/**/*.test.ts'],
  },
  resolve: {
    alias: {
      '~~': fileURLToPath(new URL('./', import.meta.url)),
      // Mirror Nuxt 4's `~` → app/ alias so utils that import sibling modules
      // via `~/utils/...` resolve under Vitest the same way they do in the app.
      '~': fileURLToPath(new URL('./app', import.meta.url)),
    },
  },
})
