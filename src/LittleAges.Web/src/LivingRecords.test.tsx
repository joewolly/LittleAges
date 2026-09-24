import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, expect, it } from 'vitest'
import { LivingRecords } from './LivingRecords'
import { parseLivingWorld } from './living'

const baseWorld = {
  version: 1, rulesVersion: 'v02-rng1-living2', worldMinute: 1440, age: 'Foraging', capabilities: ['work'], weather: 'Rain', temperature: 12, rainfall: 80,
  completedOrders: 3, foodHarvested: 7, foodPrepared: 2, careGiven: 1, goodsSpoiled: 0, totalFacts: 0,
  stock: [{ good: 'Food', quantity: 10 }, { good: 'Wood', quantity: 11 }, { good: 'Stone', quantity: 12 }, { good: 'Grain', quantity: 13 }, { good: 'Fuel', quantity: 14 }, { good: 'Meal', quantity: 15 }],
  people: [], orders: [], fields: [{ id: '1', location: { x: 2, y: 3 }, growth: 5000, moisture: 5000, condition: 5000, yieldRemaining: 4, harvests: 1 }],
  facilities: [{ id: '2', kind: 'Hearth', location: { x: 4, y: 5 } }], animals: [{ id: '3', predator: false, location: { x: 6, y: 7 }, energy: 5000 }], facts: [],
}

afterEach(cleanup)

it('shows the canonical agriculture section once for unified M13 and only Living specialty stocks', () => {
  const world = parseLivingWorld({ ...baseWorld, rulesVersion: 'm13-rng1-unified1', fields: [] })
  const { container } = render(<LivingRecords world={world} citizens={[]} foodStored={99} onSelectCitizen={() => {}} />)

  expect(screen.getByRole('heading', { name: 'Facilities and wildlife' })).toBeTruthy()
  expect(screen.getByText('1 production facilities · 1 wild animals')).toBeTruthy()
  expect(screen.queryByRole('heading', { name: 'Fields and wildlife' })).toBeNull()
  expect(screen.queryByText('Harvested grain')).toBeNull()
  expect(screen.queryByText(/Field 1:/)).toBeNull()
  expect(screen.getByText('Prepared food').parentElement?.textContent).toBe('Prepared food2')
  const supplies = container.querySelector('.living-supplies')!
  expect(supplies.textContent).toContain('Grain 13')
  expect(supplies.textContent).toContain('Fuel 14')
  expect(supplies.textContent).not.toMatch(/Food|Wood|Stone|Meal|Ready food/)
})

it('preserves field, harvest, and shared-store rows for existing Living rules', () => {
  const world = parseLivingWorld(baseWorld)
  render(<LivingRecords world={world} citizens={[]} foodStored={99} onSelectCitizen={() => {}} />)

  expect(screen.getByRole('heading', { name: 'Fields and wildlife' })).toBeTruthy()
  expect(screen.getByText('Harvested grain')).toBeTruthy()
  expect(screen.getByText(/Field 1: growth 50%/)).toBeTruthy()
  expect(screen.getByText(/Ready food/)).toBeTruthy()
  expect(screen.getByText(/Wood/)).toBeTruthy()
  expect(screen.getByText(/Stone/)).toBeTruthy()
})
