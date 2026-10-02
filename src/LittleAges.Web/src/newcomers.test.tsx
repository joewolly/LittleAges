import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { parseCitizens, parseNewcomer } from './api'
import { CitizenRoster, NewcomerRecord } from './NewcomerRecords'
import { citizenObservationLabel, isCitizenGuest, isCitizenPresent, isCitizenResident, type Newcomer } from './newcomers'
import { shouldSnapToAuthority } from './world/presentation'

const metadata: Newcomer = { origin: 'External', phase: 'Visiting', hostSettlementId: '1', shelterStructureId: '4', entryTile: { x: 0, y: 3 }, firstSeenMinute: 100, visitingStartedMinute: 120, stayDeadlineMinute: 400, joinedMinute: null, departedMinute: null, deathMinute: null, provisionsRemaining: 20 }
const guest = parseCitizens([{ citizenId: '21', name: 'Iria Jory', age: 22, lifeStage: 'Adult', location: { x: 2, y: 3 }, health: 10000, currentAction: 'Idle', actionSequence: 1, newcomer: metadata }])[0]

describe('external observer identity', () => {
  it('separates residents, present guests and departed last-alive archives', () => {
    expect(isCitizenGuest(guest)).toBe(true)
    expect(isCitizenPresent(guest)).toBe(true)
    expect(isCitizenResident(guest)).toBe(false)
    const departed = { ...guest, newcomer: { ...metadata, phase: 'Departed' as const, departedMinute: 300 } }
    expect(departed.isAlive).toBe(true)
    expect(isCitizenPresent(departed)).toBe(false)
    expect(isCitizenGuest(departed)).toBe(false)
    expect(citizenObservationLabel(departed)).toContain('last observed alive')
    expect(isCitizenResident({ ...guest, newcomer: { ...metadata, phase: 'Dead', joinedMinute: 200, deathMinute: 300 } })).toBe(true)
  })
  it('rejects invalid phase and contradictory transition observations', () => {
    expect(() => parseNewcomer({ ...metadata, phase: 'Unknown' })).toThrow()
    expect(() => parseNewcomer({ ...metadata, phase: 'Departed' })).toThrow()
    expect(() => parseNewcomer({ ...metadata, joinedMinute: 50 })).toThrow()
    expect(() => parseNewcomer({ ...metadata, provisionsRemaining: -1 })).toThrow()
  })
  it('exposes visitor records and factual unknown external ancestry', () => {
    render(<><CitizenRoster citizens={[guest]} selectedId={guest.citizenId} onSelect={() => {}} /><NewcomerRecord citizen={guest} /></>)
    expect(screen.getByRole('heading', { name: 'Visitors · 1' })).toBeInTheDocument()
    expect(screen.getByText(/Birth date and external parents are unknown/)).toBeInTheDocument()
    expect(screen.getByText('Own provisions')).toBeInTheDocument()
  })
  it.each([[true, false, 5], [false, true, 5], [false, false, 100], [false, false, null]] as const)('snaps to authority for pause/reduced motion/high speed: %s %s %s', (paused, reduced, speed) => {
    expect(shouldSnapToAuthority(paused, reduced, speed)).toBe(true)
  })
  it('keeps canonical route interpolation at low operational speeds', () => {
    for (const speed of [1, 5, 10]) expect(shouldSnapToAuthority(false, false, speed)).toBe(false)
  })
})
