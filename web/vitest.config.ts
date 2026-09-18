import { fileURLToPath } from 'node:url'
import { defineVitestProject } from '@nuxt/test-utils/config'
import { defineConfig } from 'vitest/config'

// Two projects (#1620). `unit` is the fast suite of pure-function tests: a bare
// happy-dom environment, no Nuxt runtime. `nuxt` boots the real Nuxt app for the
// tests under `tests/nuxt/` — mounting components, auto-imports, `useState`,
// hydration paths — which is the class of bug the unit suite cannot see. Kept
// apart so the runtime's startup cost never lands on the fast suite.
export default defineConfig({
  test: {
    projects: [
      {
        test: {
          name: 'unit',
          environment: 'happy-dom',
          include: ['tests/**/*.test.ts'],
          exclude: ['tests/nuxt/**'],
        },
        resolve: {
          alias: {
            '~~': fileURLToPath(new URL('./', import.meta.url)),
            // Mirror Nuxt 4's `~` → app/ alias so utils that import sibling modules
            // via `~/utils/...` resolve under Vitest the same way they do in the app.
            '~': fileURLToPath(new URL('./app', import.meta.url)),
          },
        },
      },
      await defineVitestProject({
        test: {
          name: 'nuxt',
          environment: 'nuxt',
          testTimeout: 30_000,
          hookTimeout: 120_000,
          include: ['tests/nuxt/**/*.test.ts'],
          environmentOptions: {
            nuxt: {
              domEnvironment: 'happy-dom',
            },
          },
        },
      }),
    ],
  },
})
