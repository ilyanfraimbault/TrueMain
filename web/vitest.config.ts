import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vitest/config'

export default defineConfig({
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
