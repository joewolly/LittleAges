import { describe, expect, it } from 'vitest'
import { parseHealth, parseStatus } from './api'

describe('API response parsing', () => {
  it('accepts plain text health responses', () => expect(parseHealth('Healthy')).toEqual({ ok: true, label: 'Healthy' }))
  it('keeps status safe when fields are missing or malformed', () => expect(parseStatus({ worldMinute: 'later', state: 4 })).toEqual({ state: 'Unknown', worldMinute: null, pendingEventCount: null, worldSeed: null, error: null }))
  it('preserves the full decimal world seed without numeric coercion', () => {
    const status = parseStatus({ worldSeed: '18446744073709551615' })
    expect(status.worldSeed).toBe('18446744073709551615')
  })
})
