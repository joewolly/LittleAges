import { describe, expect, it } from 'vitest'
import type { Citizen } from '../api'
import { CrowdLayout, citizenHitIds, displayedPoint } from './crowds'

const person = (id: string, change: Partial<Citizen> = {}) => ({ citizenId: id, isAlive: true, location: { x: 105, y: 60 }, ...change }) as Citizen

describe('tile crowd presentation', () => {
  it('retains occupied slots through reorder, arrival, departure and unrelated needs changes', () => {
    const layout = new CrowdLayout()
    const people = ['11', '13', '15'].map(id => person(id))
    const first = layout.update(people)
    const changed = layout.update([person('9'), { ...people[2], hunger: 200 }, people[0]])
    for (const id of ['11', '15']) expect(changed.get(id)?.offset).toEqual(first.get(id)?.offset)
    expect(changed.get('11')?.memberIds).toEqual(['9', '11', '15'])
    const remaining = layout.update([people[2]])
    expect(remaining.get('15')?.offset).toEqual(first.get('15')?.offset)
    expect(layout.update([]).size).toBe(0)
    expect(layout.update(people).get('11')?.offset).toEqual(first.get('11')?.offset)
  })

  it('separates a recorded co-location, stays within its tile and never changes observations', () => {
    // Seed 0, M17, minute 35316: Experiment workers 11 and 13 at (105,60).
    const people = [person('11'), person('13')]
    const original = structuredClone(people)
    const placements = new CrowdLayout().update(people)
    expect(displayedPoint(people[0].location, placements.get('11'))).not.toEqual(displayedPoint(people[1].location, placements.get('13')))
    expect(people).toEqual(original)
    for (const { offset } of placements.values()) { expect(Math.abs(offset.x)).toBeLessThanOrEqual(.45); expect(Math.abs(offset.y)).toBeLessThanOrEqual(.45) }
  })

  it('keeps every dense crowd member reachable from a displayed target, including overflow', () => {
    const people = Array.from({ length: 40 }, (_, i) => person(String(i + 1)))
    const placements = new CrowdLayout().update(people)
    const targets = people.map(citizen => ({ id: citizen.citizenId, point: displayedPoint(citizen.location, placements.get(citizen.citizenId)) }))
    expect(citizenHitIds(targets[7].point, targets, .01, placements)).toHaveLength(40)
    expect(citizenHitIds({ x: 0, y: 0 }, targets, .1, placements)).toEqual([])
  })

  it('retains guest slots on admission, prunes archives and allocates the new authoritative tile', () => {
    const layout = new CrowdLayout()
    const guest = person('21', { newcomer: { phase: 'Visiting', joinedMinute: null } as Citizen['newcomer'] })
    const first = layout.update([person('11'), guest])
    const admitted = layout.update([person('11'), { ...guest, newcomer: { phase: 'Resident', joinedMinute: 100 } as Citizen['newcomer'] }])
    expect(admitted.get('21')?.offset).toEqual(first.get('21')?.offset)
    expect(layout.update([person('11'), { ...guest, newcomer: { phase: 'Departed' } as Citizen['newcomer'] }]).has('21')).toBe(false)
    expect(layout.update([person('11', { location: { x: 106, y: 60 } })]).get('11')?.offset).toEqual({ x: 0, y: 0 })
  })
})
