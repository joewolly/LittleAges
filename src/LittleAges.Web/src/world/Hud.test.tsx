import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { Citizen, Settlement } from '../api'
import { ResourceMeters, SettlementBadge, VillagerCard } from './Hud'

const DAY = 24 * 60

describe('world HUD', () => {
  it('shows the season, weather, and days left in the season', () => {
    render(<SettlementBadge worldMinute={(90 + 30) * DAY} living={{ weather: 'Rain', temperature: 12, age: 'Agrarian' } as never} hint="Drag to pan" />)
    expect(screen.getByRole('heading', { name: 'The settlement grounds' })).toBeInTheDocument()
    expect(screen.getByText(/Summer · Rain · 12°C · Agrarian age/)).toBeInTheDocument()
    expect(screen.getByRole('meter', { name: 'Season progress: 60 days to autumn' })).toHaveAttribute('aria-valuenow', '30')
  })

  it('reads stores against shared capacity and housing against population', () => {
    const settlement = { foodStored: 500, woodStored: 250, stoneStored: 0, storageUsed: 750, storageCapacity: 1000, livingPopulation: 20, shelteredPopulation: 15 } as Settlement
    render(<ResourceMeters settlement={settlement} />)
    expect(screen.getByRole('meter', { name: 'Food: 500 stored' })).toHaveAttribute('aria-valuenow', '50')
    expect(screen.getByRole('meter', { name: 'Wood: 250 stored' })).toHaveAttribute('aria-valuenow', '25')
    expect(screen.getByRole('meter', { name: 'Citizens: 20 living, 15 housed' })).toHaveAttribute('aria-valuenow', '75')
    expect(screen.getByText('Shared storage 750 / 1,000')).toBeInTheDocument()
  })

  it('shows how well each need is met and offers follow and record actions', () => {
    const onFollow = vi.fn()
    const onOpen = vi.fn()
    const citizen = { citizenId: '7', name: 'Iria Jory', lifeStage: 'Adult', occupation: 'Farmer', isAlive: true, hunger: 2500, rest: 10000, shelter: 0, social: null, carriedResource: 'Wood', carriedQuantity: 12 } as unknown as Citizen
    render(<VillagerCard citizen={citizen} activity="Gather Wood · Perform" portrait="/villager.svg" following={false} onFollow={onFollow} onOpen={onOpen} />)
    expect(screen.getByRole('meter', { name: 'Fed: 75%' })).toBeInTheDocument()
    expect(screen.getByRole('meter', { name: 'Rested: 0%' })).toBeInTheDocument()
    expect(screen.getByRole('meter', { name: 'Sheltered: 100%' })).toBeInTheDocument()
    expect(screen.queryByRole('meter', { name: /Company/ })).not.toBeInTheDocument()
    expect(screen.getByText('Carrying 12 wood')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Follow' }))
    fireEvent.click(screen.getByRole('button', { name: 'Open record' }))
    expect(onFollow).toHaveBeenCalledOnce()
    expect(onOpen).toHaveBeenCalledOnce()
  })
})
