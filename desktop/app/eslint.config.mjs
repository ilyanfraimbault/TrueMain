import vueA11y from 'eslint-plugin-vuejs-accessibility'
import withNuxt from './.nuxt/eslint.config.mjs'

const ratchet = [
  '@typescript-eslint/no-unused-vars',
  'prefer-const',
]

const demoted = rule => rule.startsWith('vuejs-accessibility/') || ratchet.includes(rule)

export default withNuxt(...vueA11y.configs['flat/recommended']).onResolved((configs) => {
  for (const { rules } of configs) {
    for (const [rule, entry] of Object.entries(rules ?? {})) {
      const [severity, ...options] = [entry].flat()
      if (demoted(rule) && (severity === 'error' || severity === 2))
        rules[rule] = ['warn', ...options]
    }
  }
})
