import { useEffect, useState } from 'react'
import type { Citizen } from './api'
import { isCitizenGuest, isCitizenResident } from './newcomers'

export type PeopleQuery = { search: string; role: 'all' | 'Residents' | 'Visitors' | 'Archives'; occupation: string; lifeStage: string; favorites: boolean; page: number }
export const initialPeopleQuery: PeopleQuery = { search: '', role: 'all', occupation: '', lifeStage: '', favorites: false, page: 0 }
export const PEOPLE_PAGE_SIZE = 50
export function personGroup(person: Citizen): Exclude<PeopleQuery['role'], 'all'> {
  return isCitizenGuest(person) ? 'Visitors' : isCitizenResident(person) && person.isAlive ? 'Residents' : 'Archives'
}
export function queryPeople(people: Citizen[], query: PeopleQuery, favorites: ReadonlySet<string>) {
  const search = query.search.trim().toLocaleLowerCase('en-US')
  return people.filter(person => (!search || person.name.toLocaleLowerCase('en-US').includes(search) || person.citizenId.includes(search)) &&
    (query.role === 'all' || personGroup(person) === query.role) && (!query.occupation || person.occupation === query.occupation) &&
    (!query.lifeStage || person.lifeStage === query.lifeStage) && (!query.favorites || favorites.has(person.citizenId)))
    .sort((a, b) => a.name.localeCompare(b.name, 'en-US') || (BigInt(a.citizenId) < BigInt(b.citizenId) ? -1 : BigInt(a.citizenId) > BigInt(b.citizenId) ? 1 : 0))
}

export type FavoriteState = { worldId: string | null; ids: Set<string>; persistent: boolean; notice: string | null }
export const favoritesKey = (worldId: string) => `little-ages:observer-favorites:v1:${worldId}`
export function readFavorites(worldId: string | null): FavoriteState {
  if (!worldId) return { worldId, ids: new Set(), persistent: false, notice: 'This server does not identify the world. Favorites last for this session only.' }
  try {
    const stored = localStorage.getItem(favoritesKey(worldId))
    if (!stored) return { worldId, ids: new Set(), persistent: true, notice: null }
    const data: unknown = JSON.parse(stored)
    if (typeof data !== 'object' || data === null || !('version' in data) || data.version !== 1 || !('ids' in data) ||
      !Array.isArray(data.ids) || data.ids.some(id => typeof id !== 'string' || !/^[1-9]\d*$/.test(id))) throw new Error('Invalid favorites')
    return { worldId, ids: new Set(data.ids), persistent: true, notice: null }
  } catch {
    return { worldId, ids: new Set(), persistent: false, notice: 'Saved favorites are unavailable. Favorites last for this session only.' }
  }
}
export function toggleFavorite(state: FavoriteState, id: string): FavoriteState {
  if (!/^[1-9]\d*$/.test(id)) return state
  const ids = new Set(state.ids)
  if (ids.has(id)) ids.delete(id); else ids.add(id)
  const next = { ...state, ids }
  if (next.persistent && next.worldId) {
    try { localStorage.setItem(favoritesKey(next.worldId), JSON.stringify({ version: 1, ids: [...ids] })) }
    catch { next.persistent = false; next.notice = 'Favorites could not be saved. They last for this session only.' }
  }
  return next
}
export function useFavorites(worldId: string | null) {
  const [state, setState] = useState(() => readFavorites(worldId))
  // Reset before children render so IDs from another world never appear as favorites.
  if (state.worldId !== worldId) setState(readFavorites(worldId))
  useEffect(() => {
    if (!worldId) return
    const onStorage = (event: StorageEvent) => {
      if (event.key === favoritesKey(worldId) || event.key === null) setState(readFavorites(worldId))
    }
    window.addEventListener('storage', onStorage)
    return () => window.removeEventListener('storage', onStorage)
  }, [worldId])
  return { ...state, toggle: (id: string) => setState(toggleFavorite(state, id)) }
}
