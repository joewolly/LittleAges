import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { Citizen } from './api'
import { favoritesKey, initialPeopleQuery, personGroup, queryPeople, readFavorites, toggleFavorite, useFavorites } from './observer'

const person = (id: string, name: string, overrides = {}): Citizen => ({ citizenId: id, name, isAlive: true, occupation: 'Builder', lifeStage: 'Adult', ...overrides } as Citizen)
beforeEach(() => localStorage.clear())
afterEach(() => vi.restoreAllMocks())

describe('people search', () => {
  const roster = [person('9223372036854775807', 'Zoe'), person('10', 'Ana'), person('2', 'Ana'), person('3', 'Bram', { isAlive: false }), person('4', 'Guest', { newcomer: { phase: 'Visiting', joinedMinute: null } }), person('5', 'Departed', { newcomer: { phase: 'Departed', joinedMinute: null } })]
  it('uses exact decimal IDs without rounding and stable name/ID order', () => {
    expect(queryPeople(roster, initialPeopleQuery, new Set()).map(c => c.citizenId)).toEqual(['2', '10', '3', '5', '4', '9223372036854775807'])
    expect(queryPeople(roster, { ...initialPeopleQuery, search: '9223372036854775807' }, new Set()).map(c => c.name)).toEqual(['Zoe'])
    expect(queryPeople(roster, { ...initialPeopleQuery, search: ' ANa ' }, new Set()).map(c => c.citizenId)).toEqual(['2', '10'])
  })
  it('combines favorites and role/occupation/life filters across archives', () => {
    expect(roster.map(personGroup)).toEqual(['Residents', 'Residents', 'Residents', 'Archives', 'Visitors', 'Archives'])
    expect(queryPeople(roster, { ...initialPeopleQuery, role: 'Archives', occupation: 'Builder', lifeStage: 'Adult', favorites: true }, new Set(['3', '5', '4'])).map(c => c.citizenId)).toEqual(['3', '5'])
  })
})

describe('browser favorites', () => {
  it('keeps historical IDs and isolates worlds with the same citizen IDs', () => {
    const first = toggleFavorite(readFavorites('a'.repeat(64)), '9223372036854775807')
    expect(first.persistent).toBe(true)
    expect(readFavorites('a'.repeat(64)).ids).toEqual(first.ids)
    expect(readFavorites('b'.repeat(64)).ids.size).toBe(0)
    expect(toggleFavorite(first, '9223372036854775807').ids.size).toBe(0)
  })
  it('falls back honestly when missing identity, corrupt storage, or quota errors occur', () => {
    expect(toggleFavorite(readFavorites(null), '1')).toMatchObject({ persistent: false, ids: new Set(['1']) })
    localStorage.setItem(favoritesKey('a'), '{bad')
    expect(readFavorites('a')).toMatchObject({ persistent: false, notice: expect.stringContaining('session') })
    const first = readFavorites('b')
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('Quota') })
    expect(toggleFavorite(first, '1')).toMatchObject({ persistent: false, ids: new Set(['1']), notice: expect.stringContaining('session') })
  })
  it('resets immediately on world change and synchronizes other tabs', () => {
    const { result, rerender } = renderHook(({ id }) => useFavorites(id), { initialProps: { id: 'a' } })
    act(() => result.current.toggle('1'))
    rerender({ id: 'b' })
    expect(result.current.ids.size).toBe(0)
    localStorage.setItem(favoritesKey('b'), JSON.stringify({ version: 1, ids: ['2'] }))
    act(() => window.dispatchEvent(new StorageEvent('storage', { key: favoritesKey('b') })))
    expect(result.current.ids).toEqual(new Set(['2']))
    rerender({ id: 'a' })
    expect(result.current.ids).toEqual(new Set(['1']))
  })
})
