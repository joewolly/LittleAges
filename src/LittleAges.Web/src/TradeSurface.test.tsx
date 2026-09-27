import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { TradeSurface } from './TradeSurface'
import { parseRoads, parseSettlementDetails } from './api'

const base = { foodStored: 1, woodStored: 0, stoneStored: 0, livingPopulation: 1, deadPopulation: 0, totalPopulation: 1 }
const party = { partyId: '7', traderCitizenId: '3', originSettlementId: '1', destinationSettlementId: '2', phase: 'Outbound', location: { x: 4, y: 5 }, departedMinute: 10, load: 45, returnGood: 'Medicine', cargo: [{ good: 'Fuel', quantity: 12, purpose: 'Cargo' }, { good: 'Food', quantity: 14, purpose: 'Provisions' }] }
const trade = { eventId: '99', worldMinute: 500, partyId: '6', traderCitizenId: '3', originSettlementId: '1', destinationSettlementId: '2', outboundGood: 'Fuel', outboundQuantity: 12, returnGood: 'Medicine', returnQuantity: 3 }
const sites = [{ settlementId: '1', site: { x: 1, y: 1 }, livingPopulation: 1, foodStored: 1, woodStored: 0, stoneStored: 0 }, { settlementId: '2', site: { x: 7, y: 7 }, livingPopulation: 1, foodStored: 1, woodStored: 0, stoneStored: 0 }]

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

it('lists traders on the road and recent exchanges once across both sites', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => new Response(JSON.stringify({ ...base, settlementId: path.endsWith('1') ? '1' : '2', site: { x: 1, y: 1 }, tradeParties: [party], recentTrades: [trade] }))))
  render(<TradeSurface settlementSites={sites} refreshToken={0} />)
  expect(await screen.findByText('Trader 3')).toBeInTheDocument()
  expect(screen.getByText(/Heading out/)).toBeInTheDocument()
  expect(screen.getByText(/Carrying 12 Fuel for Medicine/)).toBeInTheDocument()
  expect(screen.getAllByText('12 Fuel for 3 Medicine')).toHaveLength(1)
})

it('renders nothing for worlds without trade fields', async () => {
  const request = vi.fn(async () => new Response(JSON.stringify({ ...base, settlementId: '1', site: { x: 1, y: 1 } })))
  vi.stubGlobal('fetch', request)
  const { container } = render(<TradeSurface settlementSites={sites.slice(0, 1)} refreshToken={0} />)
  await vi.waitFor(() => expect(request).toHaveBeenCalled())
  expect(container).toBeEmptyDOMElement()
})

it('parses trade fields strictly and keeps them optional', () => {
  expect(parseSettlementDetails({ ...base, settlementId: '1', site: { x: 0, y: 0 } }).tradeParties).toBeUndefined()
  expect(() => parseSettlementDetails({ ...base, settlementId: '1', site: { x: 0, y: 0 }, tradeParties: [{ ...party, phase: 'Lost' }] })).toThrow()
  expect(() => parseSettlementDetails({ ...base, settlementId: '1', site: { x: 0, y: 0 }, recentTrades: [{ ...trade, returnGood: 'Gold' }] })).toThrow()
})

it('parses road overlays in row-major order without wear', () => {
  expect(parseRoads({ tiles: [{ x: 3, y: 0, grade: 'Track' }, { x: 0, y: 1, grade: 'Road' }] }).tiles).toEqual([{ x: 3, y: 0, grade: 'Track' }, { x: 0, y: 1, grade: 'Road' }])
  expect(() => parseRoads({ tiles: [{ x: 0, y: 1, grade: 'Road' }, { x: 3, y: 0, grade: 'Track' }] })).toThrow()
  expect(() => parseRoads({ tiles: [{ x: 0, y: 0, grade: 'None' }] })).toThrow()
})
