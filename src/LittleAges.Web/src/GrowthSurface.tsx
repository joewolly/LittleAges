import { useEffect, useState } from 'react'
import './growth.css'

import { parseGrowth, type GrowthObservation } from './growthApi'

export function GrowthSurface() {
  const [open, setOpen] = useState(false)
  const [data, setData] = useState<GrowthObservation | null>(null)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    if (!open) return
    let active = true
    let timer: ReturnType<typeof setTimeout> | undefined
    const controller = new AbortController()
    async function load() {
      try {
        const response = await fetch('/api/v1/growth', { signal: controller.signal })
        if (!response.ok) throw new Error(`Population request failed (${response.status}).`)
        const value = parseGrowth(await response.json())
        if (active) { setData(value); setError(null) }
      } catch (reason) {
        if (active) setError(reason instanceof Error ? reason.message : 'Population information is unavailable.')
      } finally {
        if (active) timer = setTimeout(() => void load(), document.hidden ? 10_000 : 2_000)
      }
    }
    void load()
    return () => { active = false; controller.abort(); clearTimeout(timer) }
  }, [open])
  return <section className="growth-surface" aria-label="Population outlook">
    <button type="button" className="observer-button" aria-expanded={open} aria-controls="population-outlook" onClick={() => setOpen(value => !value)}>Population outlook</button>
    {open && <div id="population-outlook">
      <div className="section-heading"><span className="section-kicker">Generations</span><h3>What helps the village grow</h3></div>
      {error && <p role="alert" className="notice">{error}</p>}
      {!data && !error && <p role="status">Reading the village…</p>}
      {data && <>
        <dl className="growth-metrics"><div><dt>Living</dt><dd>{data.living}</dd></div><div><dt>Born here</dt><dd>{data.births}</dd></div><div><dt>Deaths</dt><dd>{data.deaths}</dd></div><div><dt>Unhoused</dt><dd>{data.unhoused}</dd></div></dl>
        <p>{data.unpartneredAdults} adults without a current partner. Food reserve target: {data.foodReserveTarget} units.</p>
        <p className="section-note">These are current conditions. A ready household still needs a successful daily birth opportunity.</p>
        {Object.keys(data.deathCauses).length > 0 && <p>Recorded deaths: {Object.entries(data.deathCauses).map(([cause, count]) => `${count} ${cause}`).join(' · ')}.</p>}
        <div className="growth-households">{data.households.slice(0, 100).map(h => <article key={h.householdId} className={h.ready ? 'is-ready' : ''}><strong>Household {h.householdId}</strong><span>{h.ready ? 'Conditions support a child' : h.blockers.join(' · ') || 'Conditions not yet met'}</span></article>)}</div>
        {data.households.length > 100 && <p>Showing the first 100 active households.</p>}
      </>}
    </div>}
  </section>
}
