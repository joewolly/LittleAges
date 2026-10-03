import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { Citizen } from '../api'
import type { IsoWorldProps } from './IsoWorld'
import { WorldViewport } from './WorldViewport'

const scene = vi.hoisted(() => ({ current: null as IsoWorldProps | null }))
vi.mock('./IsoWorld', () => ({ IsoWorld: (props: IsoWorldProps) => { scene.current = props; return <button onClick={() => props.onPickCitizens(props.citizens.map(c => c.citizenId))}>Tap crowded tile</button> } }))
vi.mock('./LegacyMap', () => ({ LegacyMap: () => <p>Records map</p> }))

beforeEach(() => {
  vi.spyOn(navigator, 'userAgent', 'get').mockReturnValue('browser')
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({} as CanvasRenderingContext2D)
})
afterEach(() => { cleanup(); vi.restoreAllMocks() })
const citizens = Array.from({ length: 30 }, (_, i) => ({ citizenId: String(i + 1), name: `Person ${i + 1}`, isAlive: true, location: { x: 4, y: 4 }, currentAction: 'Idle', actionPhase: 'Perform', lifeStage: 'Adult', occupation: 'Generalist', hunger: null, rest: null, shelter: null, social: null }) as Citizen)
const props = { map: { width: 10, height: 10, terrain: [], elevation: [], resources: [], startingSite: { x: 4, y: 4 } }, citizens, structures: [], settlement: null, worldSeed: '0', operationalSpeed: 1.44, worldMinute: 100, paused: true, selectedCitizenId: '1', onSelectCitizen: vi.fn() }

describe('crowd selection and follow', () => {
  it('keeps family navigation separate from explicit follow, and guards life/presence on follow requests', async () => {
    const onViewFamily = vi.fn()
    const { rerender } = render(<WorldViewport {...props} onViewFamily={onViewFamily} />)
    fireEvent.click(screen.getByRole('button', { name: 'View family' }))
    expect(onViewFamily).toHaveBeenCalledWith('1')
    expect(scene.current?.followCitizenId).toBeNull()
    const followRequest = { citizenId: '1', sequence: 1 }
    rerender(<WorldViewport {...props} onViewFamily={onViewFamily} followRequest={followRequest} />)
    await waitFor(() => expect(scene.current?.followCitizenId).toBe('1'))
    rerender(<WorldViewport {...props} citizens={[{ ...citizens[0], isAlive: false }]} followRequest={followRequest} />)
    expect(scene.current?.followCitizenId).toBeNull()
    rerender(<WorldViewport {...props} citizens={[{ ...citizens[0], newcomer: { phase: 'Departed', joinedMinute: null } } as Citizen]} followRequest={{ citizenId: '1', sequence: 2 }} />)
    await waitFor(() => expect(scene.current?.followCitizenId).toBeNull())
  })
  it('makes every dense member selectable and follows the chosen identity through admission and archives', () => {
    const { rerender } = render(<WorldViewport {...props} />)
    fireEvent.click(screen.getByRole('button', { name: 'Tap crowded tile' }))
    expect(screen.getByRole('region', { name: 'People at this spot' })).toBeInTheDocument()
    for (const citizen of citizens) expect(screen.getByRole('button', { name: `${citizen.name} · Idle · Between actions` })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /Person 30/ }))
    expect(props.onSelectCitizen).toHaveBeenCalledWith('30')
    expect(screen.queryByRole('region', { name: 'People at this spot' })).not.toBeInTheDocument()
    const guest = { ...citizens[29], newcomer: { phase: 'Visiting', joinedMinute: null } } as Citizen
    rerender(<WorldViewport {...props} citizens={[guest]} selectedCitizenId="30" />)
    fireEvent.click(screen.getByRole('button', { name: 'Follow selected' }))
    expect(scene.current?.followCitizenId).toBe('30')
    rerender(<WorldViewport {...props} citizens={[{ ...guest, newcomer: { phase: 'Resident', joinedMinute: 101 } } as Citizen]} selectedCitizenId="30" />)
    expect(scene.current?.followCitizenId).toBe('30')
    rerender(<WorldViewport {...props} citizens={[{ ...guest, newcomer: { phase: 'Departed', joinedMinute: null } } as Citizen]} selectedCitizenId="30" />)
    expect(scene.current?.followCitizenId).toBeNull()
    expect(screen.getByRole('button', { name: 'Follow selected' })).toBeDisabled()
    expect(screen.getByText('Last observed leaving the world')).toBeInTheDocument()
  })

  it('removes departed/dead people from an open picker and closes with Escape', () => {
    const { rerender } = render(<WorldViewport {...props} />)
    fireEvent.click(screen.getByRole('button', { name: 'Tap crowded tile' }))
    rerender(<WorldViewport {...props} citizens={[citizens[0], { ...citizens[1], isAlive: false }]} />)
    expect(screen.getByRole('button', { name: /Person 1/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Person 2 / })).not.toBeInTheDocument()
    fireEvent.keyDown(screen.getByRole('region', { name: 'People at this spot' }), { key: 'Escape' })
    expect(screen.queryByRole('region', { name: 'People at this spot' })).not.toBeInTheDocument()
  })
})
