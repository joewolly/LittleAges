import { describe, expect, it } from 'vitest'
import { parseCitizens } from './api'
import { buildFamilyIndex, descendantCounts, descendantIds, familyRelatives, familyTopologyKey, projectFamily } from './family'
import type { Newcomer } from './newcomers'

// Same minimal older-server read fixture used by the newcomer observer tests.
export const person = (citizenId: string, extra = {}) => parseCitizens([{ citizenId, name: 'Iria Jory', age: 22, lifeStage: 'Adult', location: { x: 2, y: 3 }, health: 10000, currentAction: 'Idle', actionSequence: 1, ...extra }])[0]
const outsider: Newcomer = { origin: 'External', phase: 'Visiting', hostSettlementId: '1', shelterStructureId: '4', entryTile: { x: 0, y: 3 }, firstSeenMinute: 100, visitingStartedMinute: 120, stayDeadlineMinute: 400, joinedMinute: null, departedMinute: null, deathMinute: null, provisionsRemaining: 20 }

describe('recorded family projection', () => {
  it('keeps shared ancestors unique and every parentage edge, without partner relatives', () => {
    const index = buildFamilyIndex([person('1'), person('2', { parentAId: '1' }), person('3', { parentAId: '1' }), person('4', { parentAId: '2', parentBId: '3', partnerId: '5' }), person('5', { parentAId: '6' }), person('6'), person('7', { parentAId: '2' })])
    const graph = projectFamily(index, '4', 'ancestors', 2)
    expect(graph.nodes.map(n => n.id).sort()).toEqual(['1', '2', '3', '4', '5', '7'])
    expect(graph.edges).toEqual(expect.arrayContaining([{ parent: '1', child: '2' }, { parent: '1', child: '3' }, { parent: '2', child: '4' }, { parent: '3', child: '4' }]))
    expect(graph.partnerships).toEqual([{ first: '4', second: '5' }])
    expect(familyRelatives(index, '4').siblings).toEqual(['7'])
    expect(familyRelatives(index, '1').siblings).toEqual([])
  })
  it('retains large IDs, missing links and cycles without recursive loops or self descendants', () => {
    const root = '9007199254740993', child = '9007199254740994'
    const index = buildFamilyIndex([person(root, { parentAId: child, parentBId: '999', childrenIds: [child] }), person(child, { parentAId: root })])
    expect(index.hasCycle).toBe(true)
    expect([...descendantIds(index, root)]).toEqual([child])
    expect(projectFamily(index, root, 'ancestors', 4).nodes.map(n => n.id)).toEqual(expect.arrayContaining([root, child, '999']))
    expect(familyRelatives(index, child).parents).toEqual([root])
  })
  it('counts unique recorded descendants beyond depth and node caps; guests and archives are not living residents', () => {
    const roster = [person('1'), ...Array.from({ length: 150 }, (_, i) => person(String(i + 2), { parentAId: '1' })), person('152', { parentAId: '2', parentBId: '3', isAlive: false, deathMinute: 200 }), person('153', { parentAId: '152', newcomer: outsider }), person('154', { parentAId: '152', newcomer: { ...outsider, phase: 'Resident', joinedMinute: 150 } }), person('155', { parentAId: '152', newcomer: { ...outsider, phase: 'Departed', departedMinute: 180 } })]
    const index = buildFamilyIndex(roster)
    const graph = projectFamily(index, '1', 'descendants', 2)
    expect(graph.nodes).toHaveLength(100)
    expect(graph.nodeLimited).toBe(true)
    expect(descendantCounts(descendantIds(index, '1'), new Map(roster.map(c => [c.citizenId, c])))).toEqual({ total: 154, livingResidents: 151 })
    expect(projectFamily(index, '152', 'descendants', 2).nodes.map(n => n.id)).toContain('154')
  })
  it('discloses depth limits and rebuilds topology only for relevant facts', () => {
    const roster = [person('1'), person('2', { parentAId: '1' }), person('3', { parentAId: '2' }), person('4', { parentAId: '3' })]
    expect(projectFamily(buildFamilyIndex(roster), '1', 'descendants', 2).depthLimited).toBe(true)
    expect(familyTopologyKey(roster)).toBe(familyTopologyKey(roster.map(c => ({ ...c, location: { x: 10, y: 10 }, hunger: 400, isAlive: false }))))
    expect(familyTopologyKey(roster)).not.toBe(familyTopologyKey([...roster, person('5', { parentAId: '4' })]))
  })
  it('traverses a deep archive iteratively and excludes missing records from factual counts', () => {
    const records = Array.from({ length: 10000 }, (_, i) => ({ citizenId: String(i + 1), parentAId: i ? String(i) : null, parentBId: null, childrenIds: i === 9999 ? ['10001'] : [], partnerId: null }))
    const index = buildFamilyIndex(records)
    expect(index.hasCycle).toBe(false)
    expect(descendantIds(index, '1').size).toBe(10000)
    expect(projectFamily(index, '1', 'descendants', 4).nodes).toHaveLength(5)
    expect(descendantCounts(descendantIds(index, '1'), new Map([['2', person('2')]]))).toEqual({ total: 1, livingResidents: 1 })
  })
  it.each([-1000, 0, 200, null, undefined])('reads signed/null/absent birth minutes: %s', birthMinute => {
    expect(person('1', { birthMinute }).birthMinute).toBe(birthMinute ?? null)
  })
  it.each([1.5, '123', NaN, Infinity, Number.MAX_SAFE_INTEGER + 1, {}])('rejects invalid birth minutes: %s', birthMinute => {
    expect(() => person('1', { birthMinute })).toThrow(/birth minute/)
  })
})
