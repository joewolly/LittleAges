import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useState } from 'react'
import { FamilyRecords, type FamilyViewState } from './FamilyRecords'
import { parseCitizens, type Citizen } from './api'

const person = (citizenId: string, extra = {}) => parseCitizens([{ citizenId, name: 'Iria Jory', age: 22, lifeStage: 'Adult', location: { x: 2, y: 3 }, health: 10000, currentAction: 'Idle', actionSequence: 1, ...extra }])[0]
afterEach(cleanup)
const callbacks = { onBiography: vi.fn(), onHistory: vi.fn(), onFollow: vi.fn() }
function View({ citizens }: { citizens: Citizen[] }) {
  const [state, onChange] = useState<FamilyViewState>({ root: '1', mode: 'ancestors', depth: 2 })
  return <FamilyRecords citizens={citizens} state={state} onChange={onChange} {...callbacks} />
}
describe('family records navigation', () => {
  it('disambiguates duplicate names, exposes unknown/missing facts and never follows during recentering', async () => {
    const roster = [person('1', { founderOrdinal: 0, birthMinute: -100 }), person('9007199254740993', { parentAId: '1', parentBId: '99' })]
    render(<View citizens={roster} />)
    fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'Iria' } })
    expect(screen.getByText('2 matching records')).toBeInTheDocument()
    fireEvent.click(within(document.querySelector('.family-search')!).getByRole('button', { name: /9007199254740993/ }))
    await act(async () => {})
    expect(screen.getByRole('heading', { name: 'Iria Jory · #9007199254740993' })).toHaveFocus()
    expect(screen.getByText('Linked ID only. Name, life and dates unknown.')).toBeInTheDocument()
    expect(screen.getByText(/Birth: Unknown/)).toBeInTheDocument()
    expect(callbacks.onFollow).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Family history' }))
    expect(callbacks.onHistory).toHaveBeenCalledWith('9007199254740993')
  })
  it('retains root/depth/mode/scroll/DOM on movement, then updates topology and death facts', () => {
    const first = [person('1'), person('2', { parentAId: '1' })]
    const { rerender } = render(<View citizens={first} />)
    fireEvent.change(screen.getByLabelText('Direction'), { target: { value: 'descendants' } })
    fireEvent.change(screen.getByLabelText('Generations'), { target: { value: '4' } })
    const node = document.querySelector('[data-person-id="2"]')!
    const scroll = screen.getByRole('generic', { name: /Family graph; scroll/ })
    scroll.scrollTop = 90; scroll.scrollLeft = 40
    rerender(<View citizens={first.map(c => ({ ...c, location: { x: 8, y: 8 }, hunger: 800 }))} />)
    expect(document.querySelector('[data-person-id="2"]')).toBe(node)
    expect(scroll.scrollTop).toBe(90); expect(scroll.scrollLeft).toBe(40)
    rerender(<View citizens={[{ ...first[0], isAlive: false, deathMinute: 222 }, first[1], person('3', { parentAId: '2' })]} />)
    expect(screen.getByText(/2 unique recorded descendants · 2 living resident/)).toBeInTheDocument()
    expect(screen.getByLabelText('Generations')).toHaveValue('4')
    expect(screen.getByLabelText('Direction')).toHaveValue('descendants')
    expect(document.querySelector('[data-person-id="3"]')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Follow on map' })).toBeDisabled()
    expect(screen.getByText(/Minute 222/)).toBeInTheDocument()
  })
  it('bounds large archive DOM and reaches hidden child branches through paged links', () => {
    const roster = [person('1'), ...Array.from({ length: 240 }, (_, i) => person(String(i + 2), { parentAId: '1', isAlive: false }))]
    render(<View citizens={roster} />)
    fireEvent.change(screen.getByLabelText('Direction'), { target: { value: 'descendants' } })
    expect(document.querySelectorAll('[data-person-id]')).toHaveLength(100)
    expect(screen.getByText(/240 unique recorded descendants · 0 living resident/)).toBeInTheDocument()
    const relatives = document.querySelectorAll('.family-relatives')[1]
    expect(within(relatives as HTMLElement).getAllByRole('listitem')).toHaveLength(12)
    fireEvent.click(screen.getByRole('button', { name: 'Next children' }))
    expect(within(relatives as HTMLElement).getByText('Iria Jory · #14')).toBeInTheDocument()
  })
})
