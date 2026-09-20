import { lazy, Suspense } from 'react'
import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { SceneLoadBoundary } from './SceneLoadBoundary'

afterEach(() => { cleanup(); vi.restoreAllMocks() })

it('retains the observer controls and map fallback when the scene chunk rejects', async () => {
  vi.spyOn(console, 'error').mockImplementation(() => undefined)
  const FailedScene = lazy(() => Promise.reject(new Error('Scene chunk download failed')))
  render(<><button>Observer records</button><SceneLoadBoundary fallback={<p>2D map</p>}>
    <Suspense fallback={<p>Loading scene</p>}><FailedScene /></Suspense>
  </SceneLoadBoundary></>)
  expect(await screen.findByText('2D map')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Observer records' })).toBeEnabled()
  expect(screen.queryByText('Loading scene')).not.toBeInTheDocument()
})
