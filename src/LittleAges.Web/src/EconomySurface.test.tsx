import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { EconomySurface } from './EconomySurface'
import { parseEconomy } from './economyApi'

const zero = { food: 0, wood: 0, stone: 0 }
const trade = { tradeId: '9007199254740993', marketId: '50', householdA: '21', householdB: '22', resourceA: 'Food', quantityA: 6, resourceB: 'Stone', quantityB: 2, carrierA: '1', carrierB: '2', pickedA: true, pickedB: false, deliveredA: true, deliveredB: false, status: 'Reserved', createdMinute: 100, closedMinute: null }
const data = { enabled: true, version: 1, communalPercent: 20, commons: zero, produced: zero, foodConsumed: 10, emergencyFoodConsumed: 10, publicWorkPaid: 4, reservedPublicFood: 4,
  households: [{ householdId: '21', inventory: { food: 100, wood: 20, stone: 10 }, reserved: zero, inTransitAndEscrow: { food: 6, wood: 0, stone: 0 }, wealth: 176, standing: 'Food reserve met' }], occupations: [{ citizenId: '1', specialization: 'Farmer', assignedMinute: 0 }], offers: [], trades: [trade], events: [], recoverable: [], totalHouseholds: 1, totalOccupations: 1, totalOffers: 0, totalTrades: 51, totalEvents: 0, totalRecoverable: 0, publicSupplyTrades: [], totalPublicSupplyTrades: 0, offset: 0, limit: 50, wealth: { minimum: 176, maximum: 176, total: 176 } }
afterEach(() => { cleanup(); vi.unstubAllGlobals() })
it('shows holdings and escrow without treating a partial delivery as a completed exchange', async () => {
  const request = vi.fn(async () => new Response(JSON.stringify(data)))
  vi.stubGlobal('fetch', request)
  render(<EconomySurface citizens={[]} />)
  expect(request).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: 'Households and trade' }))
  expect(await screen.findByText('6 Food ↔ 2 Stone')).toBeTruthy()
  expect(screen.getByText(/First delivery at market · Second goods reserved/)).toBeTruthy()
  expect(screen.getByText(/Citizen 1 · Farmer/)).toBeTruthy()
  fireEvent.click(screen.getByRole('button', { name: 'Next ledger page' }))
  expect(request).toHaveBeenCalledWith('/api/v1/economy?offset=50&limit=50', expect.anything())
})
it('rejects unsupported rules, unequal barter, lossy IDs, and premature settlement', () => {
  expect(() => parseEconomy({ ...data, version: 2 })).toThrow()
  expect(() => parseEconomy({ ...data, trades: [{ ...trade, quantityA: 5 }] })).toThrow()
  expect(() => parseEconomy({ ...data, trades: [{ ...trade, tradeId: Number("9007199254740993") }] })).toThrow()
  expect(() => parseEconomy({ ...data, trades: [{ ...trade, status: 'Completed' }] })).toThrow()
  expect(parseEconomy(data)).toEqual(data)
})
