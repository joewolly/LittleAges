import { describe, expect, it } from 'vitest'
import { parseCitizens, parseHealth, parseStatus } from './api'

describe('API response parsing', () => {
  it('accepts plain text health responses', () => expect(parseHealth('Healthy')).toEqual({ ok: true, label: 'Healthy' }))
  it('keeps status safe when fields are missing or malformed', () => expect(parseStatus({ worldMinute: 'later', state: 4 })).toEqual({ state: 'Unknown', worldMinute: null, pendingEventCount: null, worldSeed: null, error: null }))
  it('preserves the full decimal world seed without numeric coercion', () => {
    const status = parseStatus({ worldSeed: '18446744073709551615' })
    expect(status.worldSeed).toBe('18446744073709551615')
  })
  it('preserves positive decimal citizen IDs losslessly', () => {
    const citizen = { citizenId: '9223372036854775807', name: 'Elara Venn', age: 18, lifeStage: 'Adult', location: { x: 1, y: 2 }, health: 10000, currentAction: 'Idle', actionSequence: 0 }
    expect(parseCitizens([citizen])[0].citizenId).toBe(citizen.citizenId)
  })
  it('rejects malformed citizen IDs', () => {
    expect(() => parseCitizens([{ citizenId: '01' }])).toThrow()
  })
})
