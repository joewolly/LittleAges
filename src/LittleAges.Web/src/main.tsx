import { StrictMode, lazy, Suspense } from 'react'
import { createRoot } from 'react-dom/client'
import '@fontsource/lilita-one/latin-400.css'
import '@fontsource/nunito/latin-600.css'
import '@fontsource/nunito/latin-700.css'
import '@fontsource/nunito/latin-800.css'
import '@fontsource/nunito/latin-900.css'
import './styles.css'
import './hud.css'
import { App } from './App'

const ArtSlice = import.meta.env.DEV && new URLSearchParams(location.search).has('art-slice')
  ? lazy(() => import('./world/ArtSlice').then(module => ({ default: module.ArtSlice }))) : null
createRoot(document.getElementById('root')!).render(
  <StrictMode>{ArtSlice ? <Suspense fallback={<p>Loading art study…</p>}><ArtSlice /></Suspense> : <App />}</StrictMode>,
)
