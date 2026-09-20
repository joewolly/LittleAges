import { useEffect, useState } from 'react'
import { buildStatisticsQuery, parseStatistics, type StatisticsSample } from './api'
import { parseAgriculture, type Agriculture } from './agricultureApi'
import './agriculture.css'

export function AgricultureSurface({ worldMinute }: { worldMinute: number }) {
  const [open, setOpen] = useState(false)
  const [offset, setOffset] = useState(0)
  const [data, setData] = useState<Agriculture | null>(null)
  const [samples, setSamples] = useState<StatisticsSample[]>([])
  const [error, setError] = useState<string | null>(null)
  // Refresh against a month boundary, never restart a pending fetch on every live frame.
  const month = Math.floor(worldMinute / 43200)
  useEffect(() => {
    if (!open) return
    const controller = new AbortController()
    let active = true
    let timer: ReturnType<typeof setTimeout> | undefined
    async function load() {
      try {
        const responses = await Promise.all([
          fetch(`/api/v1/agriculture?offset=${offset}&limit=50`, { signal: controller.signal }),
          fetch(`/api/v1/statistics?${buildStatisticsQuery({ fromMinute: Math.max(0, (month - 12) * 43200), limit: 12 })}`, { signal: controller.signal }),
        ])
        if (responses.some(r => !r.ok)) throw new Error('Farming records are unavailable.')
        const [agriculture, statistics] = await Promise.all(responses.map(r => r.json()))
        const next = parseAgriculture(agriculture)
        const chart = parseStatistics(statistics)
        if (active) { setData(next); setSamples(chart); setError(null) }
      } catch (reason) { if (active) setError(reason instanceof Error ? reason.message : 'Farming records are unavailable.') }
      finally { if (active) timer = setTimeout(() => void load(), document.hidden ? 15000 : 3000) }
    }
    void load()
    return () => { active = false; controller.abort(); clearTimeout(timer) }
  }, [open, offset, month])
  const maximum = Math.max(1, ...samples.flatMap(s => [s.foodProducedPeriod, s.foodConsumedPeriod]))
  return <section className="agriculture-surface" aria-label="Farming and reserves">
    <button className="observer-button" type="button" aria-expanded={open} onClick={() => setOpen(v => !v)}>Farming and reserves</button>
    {open && <>
      <h3>Food through the seasons</h3>
      {error && <p role="alert">{error}</p>}
      {!data && !error && <p role="status">Reading the fields…</p>}
      {data && !data.enabled && <p>This world's rules do not include farming.</p>}
      {data?.enabled && <>
        <dl className="growth-metrics"><div><dt>Food</dt><dd>{data.food}</dd></div><div><dt>Days covered</dt><dd>{data.projectedCoverageDays}</dd></div><div><dt>Granary capacity</dt><dd>{data.dedicatedFoodCapacity}</dd></div><div><dt>Winter target</dt><dd>{data.winterReserveTarget}</dd></div></dl>
        <p>Coverage estimates winter consumption at 9 food per citizen per day. Gathering remains available in every season.</p>
        <p>Spring: plant · Summer: tend · Autumn: harvest and haul · Winter: dormant</p>
        <div className="farm-records">{data.farms.map(f => <article key={f.structureId}><h4>Farm {f.structureId} · {f.stage}</h4><label>Planting <progress max={1200} value={f.plantingWork} /></label><label>Tending <progress max={2400} value={f.tendingWork} /></label><p>{f.remaining} standing food · {f.harvested} harvested this year</p></article>)}</div>
        <h4>Seasonal food flow</h4>
        <p className="section-note">Monthly production (green, including gathered food) and consumption (ochre).</p>
        <svg viewBox="0 0 360 150" role="img" aria-label="Monthly food production and consumption"><line x1="0" y1="125" x2="360" y2="125" stroke="#a38860" />{samples.map((s, index) => <g key={s.worldMinute}><title>{`Month ${Math.floor(s.worldMinute / 43200)}: produced ${s.foodProducedPeriod}, consumed ${s.foodConsumedPeriod}`}</title><rect x={index * 30 + 3} y={125 - s.foodProducedPeriod / maximum * 115} width="10" height={s.foodProducedPeriod / maximum * 115} fill="#537d42" /><rect x={index * 30 + 14} y={125 - s.foodConsumedPeriod / maximum * 115} width="10" height={s.foodConsumedPeriod / maximum * 115} fill="#ba8442" /><text x={index * 30 + 5} y="144" fontSize="9">{Math.floor(s.worldMinute / 43200) % 12 || 12}</text></g>)}</svg>
        <h4>Harvest records</h4>
        {data.harvests.length === 0 ? <p>The first harvest has not finished.</p> : <ul>{data.harvests.map(h => <li key={`${h.year}:${h.structureId}`}>Year {h.year}, farm {h.structureId}: {h.harvested} harvested, {h.lost} left unharvested.</li>)}</ul>}
        <nav aria-label="Farm record pages"><button disabled={offset === 0} onClick={() => setOffset(Math.max(0, offset - 50))}>Previous records</button><button disabled={offset + 50 >= Math.max(data.totalFarms, data.totalHarvests)} onClick={() => setOffset(offset + 50)}>Next records</button></nav>
      </>}
    </>}
  </section>
}
