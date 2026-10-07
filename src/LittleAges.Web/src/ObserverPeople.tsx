import { useMemo } from 'react'
import type { Citizen } from './api'
import { FavoriteButton } from './FavoriteButton'
import { citizenObservationLabel } from './newcomers'
import { PEOPLE_PAGE_SIZE, personGroup, queryPeople, type PeopleQuery } from './observer'

type Props = { citizens: Citizen[]; query: PeopleQuery; onQuery: (query: PeopleQuery) => void; selectedId: string | null; onSelect: (id: string) => void; favorites: ReadonlySet<string>; onFavorite: (id: string) => void }
export function PeopleDirectory({ citizens, query, onQuery, selectedId, onSelect, favorites, onFavorite }: Props) {
  const matches = useMemo(() => queryPeople(citizens, query, favorites), [citizens, query, favorites])
  const choices = useMemo(() => ({ occupations: [...new Set(citizens.map(c => c.occupation))].sort(), stages: [...new Set(citizens.map(c => c.lifeStage))].sort() }), [citizens])
  const counts = useMemo(() => {
    const result = { Residents: 0, Visitors: 0, Archives: 0, favorites: 0 }
    for (const citizen of citizens) { result[personGroup(citizen)]++; if (favorites.has(citizen.citizenId)) result.favorites++ }
    return result
  }, [citizens, favorites])
  const page = Math.min(query.page, Math.max(0, Math.ceil(matches.length / PEOPLE_PAGE_SIZE) - 1))
  const update = (change: Partial<PeopleQuery>) => onQuery({ ...query, ...change, page: 0 })
  return <section className="people-directory" aria-label="People directory">
    <label className="people-search">Search people by name or ID<input type="search" value={query.search} onChange={event => update({ search: event.target.value })} placeholder="Name or citizen ID" /></label>
    <div className="people-filters">
      <label>Records<select value={query.role} onChange={event => update({ role: event.target.value as PeopleQuery['role'] })}><option value="all">All · {citizens.length}</option>{(['Residents', 'Visitors', 'Archives'] as const).map(role => <option key={role} value={role}>{role} · {counts[role]}</option>)}</select></label>
      <label>Occupation<select value={query.occupation} onChange={event => update({ occupation: event.target.value })}><option value="">All occupations</option>{choices.occupations.map(value => <option key={value}>{value}</option>)}</select></label>
      <label>Life stage<select value={query.lifeStage} onChange={event => update({ lifeStage: event.target.value })}><option value="">All life stages</option>{choices.stages.map(value => <option key={value}>{value}</option>)}</select></label>
      <button className="observer-button" type="button" aria-pressed={query.favorites} onClick={() => update({ favorites: !query.favorites })}>Favorites · {counts.favorites}</button>
    </div>
    <p role="status" className="people-result-count">{matches.length} matching {matches.length === 1 ? 'record' : 'records'} · {citizens.length} total</p>
    {matches.length === 0 && <p>No people match these filters.</p>}
    <div className="people-results" role="list" aria-label="Citizens">{matches.slice(page * PEOPLE_PAGE_SIZE, (page + 1) * PEOPLE_PAGE_SIZE).map(person => <div role="listitem" className={`people-row${selectedId === person.citizenId ? ' is-selected' : ''}`} key={person.citizenId}>
      <button type="button" data-person-link={person.citizenId} className="person-select" aria-label={`${person.name}, ${person.lifeStage}, ${person.currentAction}`} onClick={() => onSelect(person.citizenId)}><strong>{person.name}</strong><small>#{person.citizenId}</small><span>{citizenObservationLabel(person)} · {person.occupation}</span></button>
      <FavoriteButton id={person.citizenId} name={person.name} favorites={favorites} onToggle={onFavorite} />
    </div>)}</div>
    {matches.length > PEOPLE_PAGE_SIZE && <nav className="people-pagination" aria-label="People pages"><button type="button" className="observer-button" disabled={page === 0} onClick={() => onQuery({ ...query, page: page - 1 })}>Previous people</button><span>Page {page + 1} of {Math.ceil(matches.length / PEOPLE_PAGE_SIZE)}</span><button type="button" className="observer-button" disabled={(page + 1) * PEOPLE_PAGE_SIZE >= matches.length} onClick={() => onQuery({ ...query, page: page + 1 })}>Next people</button></nav>}
  </section>
}
