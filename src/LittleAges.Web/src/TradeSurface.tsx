import { useEffect, useState } from 'react'
import './trade.css'

import { fetchSettlementDetails, type SettlementDetails, type SettlementSite, type TradeCargo } from './api'

const phaseLabels = { Outbound: 'Heading out', Dwell: 'Trading', Returning: 'Returning home' } as const

function goodLabel(good: string) { return good.replace(/([a-z])([A-Z])/g, '$1 $2') }
function cargoLabel(cargo: TradeCargo[]) {
  const goods = cargo.filter(stack => stack.purpose === 'Cargo')
  return goods.length === 0 ? 'No trade goods' : goods.map(stack => `${stack.quantity} ${goodLabel(stack.good)}`).join(' · ')
}

/** M15 trade between the sites: traders on the road and the latest exchanges. Hidden for worlds without trade. */
export function TradeSurface({ settlementSites, refreshToken }: { settlementSites: SettlementSite[]; refreshToken: number }) {
  const [details, setDetails] = useState<SettlementDetails[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const siteIds = settlementSites.map(site => site.settlementId).join(',')
  useEffect(() => {
    if (siteIds === '') return
    let active = true
    void Promise.all(siteIds.split(',').map(fetchSettlementDetails)).then(value => {
      if (active) { setDetails(value); setError(null) }
    }).catch(reason => {
      if (active) setError(reason instanceof Error ? reason.message : 'Trade records are unavailable.')
    })
    return () => { active = false }
  }, [siteIds, refreshToken])
  const tradeSites = details?.filter(site => site.tradeParties !== undefined) ?? []
  if (error === null && tradeSites.length === 0) return null
  const parties = [...new globalThis.Map(tradeSites.flatMap(site => site.tradeParties ?? []).map(party => [party.partyId, party])).values()]
  const trades = [...new globalThis.Map(tradeSites.flatMap(site => site.recentTrades ?? []).map(trade => [trade.eventId, trade])).values()]
    .sort((first, second) => second.worldMinute - first.worldMinute || Number(BigInt(second.eventId) - BigInt(first.eventId)))
    .slice(0, 10)
  return <section className="trade-surface" aria-labelledby="trade-heading">
    <div className="section-heading"><span className="section-kicker">Roads & trade</span><h2 id="trade-heading">Between the settlements</h2></div>
    {error && <p className="notice" role="alert">Trade records unavailable: {error}</p>}
    <h3>On the road</h3>
    {parties.length === 0 ? <p>No traders are travelling.</p> : <ul className="trade-list">{parties.map(party => <li key={party.partyId}>
      <strong>Trader {party.traderCitizenId}</strong>
      <span>Settlement {party.originSettlementId} → {party.destinationSettlementId} · {phaseLabels[party.phase]} · at ({party.location.x}, {party.location.y})</span>
      <span>Carrying {cargoLabel(party.cargo)} for {goodLabel(party.returnGood)} · load limit {party.load}</span>
    </li>)}</ul>}
    <h3>Recent exchanges</h3>
    {trades.length === 0 ? <p>No trades have been completed yet.</p> : <ul className="trade-list">{trades.map(trade => <li key={trade.eventId}>
      <strong>{trade.outboundQuantity} {goodLabel(trade.outboundGood)} for {trade.returnQuantity} {goodLabel(trade.returnGood)}</strong>
      <span>Settlement {trade.originSettlementId} traded in Settlement {trade.destinationSettlementId} · Trader {trade.traderCitizenId} · minute {trade.worldMinute.toLocaleString('en-US')}</span>
    </li>)}</ul>}
  </section>
}
