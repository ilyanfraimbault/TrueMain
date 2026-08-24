import { describe, expect, it } from 'vitest'
import { formatBuildLabel } from '~/utils/app-version'

describe('formatBuildLabel', () => {
  it('names the environment for a preprod build', () => {
    expect(formatBuildLabel({ env: 'preprod', version: '1.20.0-rc.4' }))
      .toBe('preprod · 1.20.0-rc.4')
  })

  it('prints the bare version in production', () => {
    // On truemain.lol the release tag is unambiguous on its own; a
    // "production" badge would be noise on every page.
    expect(formatBuildLabel({ env: 'production', version: '1.19.0' })).toBe('1.19.0')
    expect(formatBuildLabel({ env: 'prod', version: '1.19.0' })).toBe('1.19.0')
    expect(formatBuildLabel({ env: 'Production', version: '1.19.0' })).toBe('1.19.0')
  })

  it('shows nothing at all when no version was injected', () => {
    // Local dev leaves both empty. A deploy that forgot APP_VERSION lands on
    // the second case — better a missing label than a bare "preprod ·".
    expect(formatBuildLabel({ env: '', version: '' })).toBeNull()
    expect(formatBuildLabel({ env: 'preprod', version: '' })).toBeNull()
    expect(formatBuildLabel({ env: 'preprod', version: '   ' })).toBeNull()
    expect(formatBuildLabel({})).toBeNull()
  })

  it('falls back to the version alone when the environment is unset', () => {
    expect(formatBuildLabel({ version: '1.19.0' })).toBe('1.19.0')
  })

  it('trims what the env injected', () => {
    // Compose passes these through a .env file; a trailing space there should
    // not reach the footer.
    expect(formatBuildLabel({ env: ' preprod ', version: ' 1.20.0-rc.4 ' }))
      .toBe('preprod · 1.20.0-rc.4')
  })
})
