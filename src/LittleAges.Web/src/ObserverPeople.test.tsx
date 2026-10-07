import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { useState } from 'react'
import type { Citizen } from './api'
import { PeopleDirectory } from './ObserverPeople'
import { initialPeopleQuery } from './observer'

afterEach(cleanup)
it('paginates the complete roster, reports exact counts, and resets the page for a filter', () => {
  const people = Array.from({ length: 121 }, (_, index) => ({ citizenId: String(index + 1), name: `Person ${String(index + 1).padStart(3, '0')}`, isAlive: true, occupation: 'Builder', lifeStage: 'Adult', currentAction: 'Idle' } as Citizen))
  const onSelect = vi.fn()
  function Directory() {
    const [query, onQuery] = useState(initialPeopleQuery)
    return <PeopleDirectory citizens={people} query={query} onQuery={onQuery} selectedId={null} onSelect={onSelect} favorites={new Set(['121'])} onFavorite={vi.fn()} />
  }
  render(<Directory />)
  expect(screen.getByText('121 matching records · 121 total')).toBeInTheDocument()
  expect(screen.getAllByRole('listitem')).toHaveLength(50)
  fireEvent.click(screen.getByRole('button', { name: 'Next people' }))
  fireEvent.click(screen.getByRole('button', { name: 'Next people' }))
  expect(screen.getAllByRole('listitem')).toHaveLength(21)
  fireEvent.click(screen.getByRole('button', { name: /^Favorites/ }))
  expect(screen.getByText('1 matching record · 121 total')).toBeInTheDocument()
  expect(screen.getAllByRole('listitem')).toHaveLength(1)
  fireEvent.click(screen.getByRole('button', { name: /^Person 121,/ }))
  expect(onSelect).toHaveBeenCalledWith('121')
  fireEvent.click(screen.getByRole('button', { name: /^Favorites/ }))
  expect(screen.getByRole('button', { name: 'Previous people' })).toBeDisabled()
})
