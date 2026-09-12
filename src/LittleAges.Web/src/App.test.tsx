import { render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'

beforeEach(() => {
  vi.stubGlobal('fetch', vi.fn((path: string) => Promise.resolve({ ok: true, text: () => Promise.resolve(path.endsWith('health') ? 'Healthy' : JSON.stringify({ state: 'Running', worldMinute: 19452830, worldSeed: '18446744073709551615' })) })))
})

describe('status page', () => {
  it('shows the observer foundation after loading', async () => {
    render(<App />)
    await waitFor(() => expect(screen.getByText('19,452,830')).toBeInTheDocument())
    expect(screen.getByText('Running')).toBeInTheDocument()
    expect(screen.getByText('No controls. No interruptions.')).toBeInTheDocument()
    expect(screen.queryByText('18446744073709551615')).not.toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveAttribute('aria-live', 'polite')
  })

  it('shows a useful connection error', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.reject(new Error('Network down'))))
    render(<App />)
    expect(await screen.findByRole('alert')).toHaveTextContent('Network down')
  })
})
