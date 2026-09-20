import { useEffect, useState } from 'react'
import type { Citizen } from './api'
import { parseEconomy, type Economy, type Goods } from './economyApi'
import './economy.css'

const goods = (g: Goods) => `${g.food} food · ${g.wood} wood · ${g.stone} stone`
export function EconomySurface({ citizens }: { citizens: Citizen[] }) {
  const [open, setOpen] = useState(false)
  const [offset, setOffset] = useState(0)
  const [data, setData] = useState<Economy | null>(null)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    if (!open) return
    const controller = new AbortController()
    let active = true
    let timer: ReturnType<typeof setTimeout> | undefined
    async function load() {
      try {
        const response = await fetch(`/api/v1/economy?offset=${offset}&limit=50`, { signal: controller.signal })
        if (!response.ok) throw new Error('The household ledger is unavailable.')
        const next = parseEconomy(await response.json())
        if (active) { setData(next); setError(null) }
      } catch (reason) { if (active) setError(reason instanceof Error ? reason.message : 'The household ledger is unavailable.') }
      finally { if (active) timer = setTimeout(() => void load(), document.hidden ? 15000 : 3000) }
    }
    void load()
    return () => { active = false; controller.abort(); clearTimeout(timer) }
  }, [open, offset])
  const name = (id: string) => citizens.find(c => c.citizenId === id)?.name ?? `Citizen ${id}`
  return <section className="economy-surface" aria-label="Households and trade">
    <button className="observer-button" aria-expanded={open} onClick={() => setOpen(v => !v)}>Households and trade</button>
    {open && <>
      <h3>The village exchange</h3>
      {error && <p role="alert">{error}</p>}
      {!data && !error && <p role="status">Reading the household ledger…</p>}
      {data && !data.enabled && <p>This world's rules do not include private supplies or barter.</p>}
      {data?.enabled && <>
        <p>Households contribute {data.communalPercent}% of delivered production to shared supplies. Food 1 · Wood 2 · Stone 3 are fixed barter weights, not money.</p>
        <dl className="growth-metrics"><div><dt>Shared food</dt><dd>{data.commons.food}</dd></div><div><dt>Shared wood</dt><dd>{data.commons.wood}</dd></div><div><dt>Shared stone</dt><dd>{data.commons.stone}</dd></div><div><dt>Trades recorded</dt><dd>{data.totalTrades}</dd></div></dl>
        <p>Emergency meals used {data.emergencyFoodConsumed} food. Completed public work paid {data.publicWorkPaid} food; {data.reservedPublicFood} is reserved for work in progress.</p>
        <h4>Household holdings</h4>
        <p>Wealth counts owned goods in storage, transit, escrow, and interrupted cargo. Range: {data.wealth.minimum}–{data.wealth.maximum} barter units. Standing describes food reserves.</p>
        <div className="economy-cards">{data.households.map(h => <article key={h.householdId}><h4>Household {h.householdId}</h4><p>{citizens.filter(c => c.isAlive && c.householdId === h.householdId).map(c => c.name).join(', ')}</p><strong>{goods(h.inventory)}</strong><p>{h.standing} · Wealth {h.wealth}</p><meter aria-label={`Household ${h.householdId} wealth`} min={0} max={Math.max(1, data.wealth.maximum)} value={h.wealth} /><small>Reserved: {goods(h.reserved)}<br />Moving or in escrow: {goods(h.inTransitAndEscrow)}</small></article>)}</div>
        <h4>Work specializations</h4><ul>{data.occupations.map(a => <li key={a.citizenId}>{name(a.citizenId)} · {a.specialization}</li>)}</ul>
        <h4>Offers and requests</h4><ul>{data.offers.filter(o => o.surplus > 0 || o.requested > 0).map(o => <li key={`${o.householdId}:${o.resource}`}>Household {o.householdId}: {o.surplus > 0 ? `offers ${o.surplus}` : `requests ${o.requested}`} {o.resource.toLowerCase()}</li>)}</ul>
        <h4>Physical trades</h4>{data.trades.length === 0 ? <p>No goods have been matched yet.</p> : <ol className="trade-records">{data.trades.map(t => <li key={t.tradeId}><strong>{t.quantityA} {t.resourceA} ↔ {t.quantityB} {t.resourceB}</strong><p>Households {t.householdA} and {t.householdB} · {t.status}</p><small>{t.deliveredA ? 'First delivery at market' : t.pickedA ? 'First cargo travelling' : 'First goods reserved'} · {t.deliveredB ? 'Second delivery at market' : t.pickedB ? 'Second cargo travelling' : 'Second goods reserved'}</small></li>)}</ol>}
        <h4>Supplies for public building</h4><p>Haulers exchange available shared food for household surplus at the depot, then transport public materials to construction.</p><ul>{data.publicSupplyTrades.map(t => <li key={t.transactionId}>Household {t.householdId}: {t.quantity} {t.resource} supplied for {t.foodPaid} shared food · {name(t.citizenId)} hauling</li>)}</ul>
        <h4>Inheritance and milestones</h4><ul>{data.events.map(e => <li key={e.eventId}>Minute {e.worldMinute}: {e.kind === 'HouseholdMerged' ? 'Household joined' : e.kind === 'FirstTrade' ? 'First market exchange' : 'Inheritance'} · household {e.householdId}{e.recipientHouseholdId ? ` → household ${e.recipientHouseholdId}` : ' → descendants or shared supplies'} · {goods(e.goods)}</li>)}</ul>
        {data.recoverable.length > 0 && <><h4>Interrupted cargo</h4><p>These goods remain at their recorded location and retain their owner.</p><ul>{data.recoverable.map(g => <li key={g.cacheId}>{g.quantity} {g.resource} at ({g.location.x}, {g.location.y}) · {g.householdId ? `household ${g.householdId}` : 'shared supplies'}</li>)}</ul></>}
        <nav aria-label="Economy record pages"><button disabled={offset === 0} onClick={() => setOffset(Math.max(0, offset - 50))}>Previous ledger page</button><button disabled={offset + 50 >= Math.max(data.totalHouseholds, data.totalOccupations, data.totalOffers, data.totalTrades, data.totalEvents, data.totalRecoverable, data.totalPublicSupplyTrades)} onClick={() => setOffset(offset + 50)}>Next ledger page</button></nav>
      </>}
    </>}
  </section>
}
