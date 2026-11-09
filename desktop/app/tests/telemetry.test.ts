import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import { PAGE_KEYS, telemetryPage } from '~/utils/telemetry'

const catalog = readFileSync(
  join(import.meta.dirname, '../../../backend/Api/Services/Desktop/DesktopTelemetryCatalog.cs'),
  'utf8',
)

describe('telemetryPage', () => {
  it('names a page by its route and nothing else', () => {
    expect(telemetryPage('champions-id')).toBe('champion')
    expect(telemetryPage('overlay')).toBe('overlay')
    expect(telemetryPage('dev-kits')).toBeNull()
    expect(telemetryPage(undefined)).toBeNull()
  })

  it.each(Object.values(PAGE_KEYS))('sends %s, a key the API catalog lists', (key) => {
    expect(catalog).toContain(`new("${key}"`)
  })
})
