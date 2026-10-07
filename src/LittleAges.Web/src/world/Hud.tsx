import { citizenObservationLabel, isCitizenPresent } from '../newcomers'
import { useState } from 'react'
import type { ReactNode } from 'react'
import type { Citizen, Settlement } from '../api'
import type { LivingWorld } from '../living'
import { livingLabel } from '../living'
import { seasonAt, type Season } from './iso/seasons'

const ICON_PATHS = {
  // Lucide star, retrieved through better-icons; follows the existing HUD stroke treatment.
  star: <path d="M11.525 2.295a.53.53 0 0 1 .95 0l2.31 4.679a2.12 2.12 0 0 0 1.595 1.16l5.166.756a.53.53 0 0 1 .294.904l-3.736 3.638a2.12 2.12 0 0 0-.611 1.878l.882 5.14a.53.53 0 0 1-.771.56l-4.618-2.428a2.12 2.12 0 0 0-1.973 0L6.396 21.01a.53.53 0 0 1-.77-.56l.881-5.139a2.12 2.12 0 0 0-.611-1.879L2.16 9.795a.53.53 0 0 1 .294-.906l5.165-.755a2.12 2.12 0 0 0 1.597-1.16z" />,
  book: <><path d="M4 5.5C4 4.7 4.7 4 5.5 4H11c1.1 0 2 .9 2 2v14c0-1.1-.9-2-2-2H4z" /><path d="M22 5.5c0-.8-.7-1.5-1.5-1.5H15c-1.1 0-2 .9-2 2v14c0-1.1.9-2 2-2h7z" /></>,
  eye: <><path d="M2 12s3.6-6.5 10-6.5S22 12 22 12s-3.6 6.5-10 6.5S2 12 2 12z" /><circle cx="12" cy="12" r="3" /></>,
  target: <><circle cx="12" cy="12" r="7.5" /><circle cx="12" cy="12" r="2.5" /><path d="M12 1.5v4M12 18.5v4M1.5 12h4M18.5 12h4" /></>,
  map: <><path d="M3 6.5 9 4l6 2.5L21 4v13.5L15 20l-6-2.5L3 20z" /><path d="M9 4v13.5M15 6.5V20" /></>,
  home: <><path d="M3 11 12 4l9 7" /><path d="M5.5 9.5V20h13V9.5" /><path d="M10 20v-5h4v5" /></>,
  expand: <><path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5" /></>,
  close: <path d="M6 6l12 12M18 6 6 18" />,
  sun: <><circle cx="12" cy="12" r="4.5" /><path d="M12 2v2.5M12 19.5V22M2 12h2.5M19.5 12H22M4.9 4.9l1.8 1.8M17.3 17.3l1.8 1.8M4.9 19.1l1.8-1.8M17.3 6.7l1.8-1.8" /></>,
  leaf: <><path d="M5 19c0-8 5-14 15-14 0 10-6 15-14 15" /><path d="M5 19c3-4 6-7 10-9" /></>,
  sprout: <><path d="M12 21v-9" /><path d="M12 12C12 7 8.5 5 4 5c0 4.5 3 7 8 7z" /><path d="M12 14c0-4 3-6.5 8-6.5 0 4-3 6.5-8 6.5z" /></>,
  snow: <><path d="M12 2v20M3.3 7l17.4 10M3.3 17 20.7 7" /><path d="m9 4 3 2 3-2M9 20l3-2 3 2" /></>,
  rain: <><path d="M6 14a4 4 0 0 1 .5-8 5.5 5.5 0 0 1 10.6 1.5A3.3 3.3 0 0 1 17 14z" /><path d="M8 17l-1 3M12 17l-1 3M16 17l-1 3" /></>,
} as const

export type HudIconName = keyof typeof ICON_PATHS

export function HudIcon({ name, size = 24 }: { name: HudIconName; size?: number }) {
  return <svg className="hud-icon" width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2.6} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{ICON_PATHS[name]}</svg>
}

const SEASON_NAMES = { spring: 'Spring', summer: 'Summer', autumn: 'Autumn', winter: 'Winter' } as const
const SEASON_ICONS: Record<keyof typeof SEASON_NAMES, HudIconName> = { spring: 'sprout', summer: 'sun', autumn: 'leaf', winter: 'snow' }
const NEXT_SEASON = { spring: 'summer', summer: 'autumn', autumn: 'winter', winter: 'spring' } as const
const MINUTES_PER_DAY = 1440

/** Top-left badge: the settlement, its season, and the weather, with progress toward the next season. */
export function SettlementBadge({ worldMinute, living, hint, previewSeason }: { worldMinute: number; living?: LivingWorld | null; hint: string; previewSeason?: Season }) {
  const season = previewSeason ?? seasonAt(worldMinute)
  const dayOfSeason = Math.floor(worldMinute / MINUTES_PER_DAY) % 90
  const remaining = 90 - dayOfSeason
  const weatherIcon: HudIconName = living?.weather === 'Rain' ? 'rain' : living?.weather === 'ColdSpell' ? 'snow' : SEASON_ICONS[season]
  return <div className="hud-badge">
    <svg className="hud-shield" width="58" height="66" viewBox="0 0 74 84" aria-hidden="true">
      <path d="M37 3 L69 14 V42 C69 62 54 74 37 81 C20 74 5 62 5 42 V14 Z" fill="#1f8a7e" stroke="#2a1a0e" strokeWidth="5" />
      <path d="M37 11 L62 19 V42 C62 58 51 67 37 73 C23 67 12 58 12 42 V19 Z" fill="none" stroke="#f5c451" strokeWidth="3" />
      <circle cx="37" cy="36" r="10" fill="#f5c451" stroke="#2a1a0e" strokeWidth="3" />
      <path d="M22 58 Q37 44 52 58" fill="none" stroke="#f5c451" strokeWidth="4" strokeLinecap="round" />
    </svg>
    <div className="hud-badge-text">
      <span className="hud-kicker">Living diorama</span>
      <h2 id="world-heading" className="hud-title">The settlement grounds</h2>
      <p className="hud-weather"><HudIcon name={weatherIcon} size={18} />{SEASON_NAMES[season]}{living ? <> · {livingLabel(living.weather)} · {living.temperature}°C · {living.age} age</> : null}</p>
      <div className="hud-bar hud-season-bar" role="meter" aria-label={`Season progress: ${remaining} days to ${NEXT_SEASON[season]}`} aria-valuemin={0} aria-valuemax={90} aria-valuenow={dayOfSeason}>
        <span className="hud-bar-fill" style={{ width: `${(dayOfSeason / 90) * 100}%` }} />
        <span className="hud-bar-label">{remaining} {remaining === 1 ? 'day' : 'days'} to {NEXT_SEASON[season]}</span>
      </div>
      <p className="hud-hint">{hint}</p>
    </div>
  </div>
}

function Meter({ icon, label, value, detail, fraction, kind }: { icon: string; label: string; value: ReactNode; detail: string; fraction: number; kind: string }) {
  const percent = Math.round(Math.max(0, Math.min(1, fraction)) * 100)
  return <div className={`hud-meter hud-meter-${kind}`} role="meter" aria-label={`${label}: ${detail}`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent}>
    <span className="hud-meter-label" aria-hidden="true">{label}</span>
    <span className="hud-bar"><span className="hud-bar-fill" style={{ width: `${percent}%` }} /><strong className="hud-meter-value" aria-hidden="true">{value}</strong></span>
    <img className="hud-meter-icon" src={`/assets/sprites/common/icon-${icon}.svg`} alt="" />
  </div>
}

/** Top-right storage and population meters, read straight from the settlement observation. */
export function ResourceMeters({ settlement }: { settlement: Settlement | null }) {
  if (!settlement) return null
  const capacity = Math.max(1, settlement.storageCapacity)
  const number = (value: number) => value.toLocaleString('en-US')
  const housed = settlement.livingPopulation === 0 ? 0 : settlement.shelteredPopulation / settlement.livingPopulation
  return <section className="hud-meters" aria-label="Settlement stores">
    <Meter icon="food" kind="food" label="Food" value={number(settlement.foodStored)} detail={`${number(settlement.foodStored)} stored`} fraction={settlement.foodStored / capacity} />
    <Meter icon="wood" kind="wood" label="Wood" value={number(settlement.woodStored)} detail={`${number(settlement.woodStored)} stored`} fraction={settlement.woodStored / capacity} />
    <Meter icon="stone" kind="stone" label="Stone" value={number(settlement.stoneStored)} detail={`${number(settlement.stoneStored)} stored`} fraction={settlement.stoneStored / capacity} />
    <p className="hud-storage">Shared storage {number(settlement.storageUsed)} / {number(settlement.storageCapacity)}</p>
    <Meter icon="people" kind="people" label="Citizens" value={number(settlement.livingPopulation)} detail={`${number(settlement.livingPopulation)} living, ${number(settlement.shelteredPopulation)} housed`} fraction={housed} />
    {settlement.guestPopulation !== undefined && <p className="hud-storage">Visitors {number(settlement.guestPopulation)} · residents {number(settlement.livingPopulation)}</p>}
  </section>
}

const NEEDS: Array<{ key: 'hunger' | 'rest' | 'shelter' | 'social'; label: string }> = [
  { key: 'hunger', label: 'Fed' }, { key: 'rest', label: 'Rested' }, { key: 'shelter', label: 'Sheltered' }, { key: 'social', label: 'Company' },
]

/** Parchment card for the selected villager. Needs run 0 (met) to 10000 (critical), so bars show how well each is met. */
export function VillagerCard({ citizen, activity, portrait, following, onFollow, onOpen, onViewFamily, onLocate, favorite = false, onFavorite, onClear, notice }: { citizen: Citizen; activity: string; portrait: string; following: boolean; onFollow: () => void; onOpen?: () => void; onViewFamily?: () => void; onLocate?: () => void; favorite?: boolean; onFavorite?: () => void; onClear?: () => void; notice?: string | null }) {
  const [expanded, setExpanded] = useState(false)
  return <div className={`world-selection hud-card${expanded ? ' is-expanded' : ' is-compact'}`} aria-label="Selected person">
    <strong className="hud-card-name">{citizen.name}</strong><div className="hud-card-tools"><button type="button" className="card-expand" aria-expanded={expanded} onClick={() => setExpanded(value => !value)}>{expanded ? 'Less' : 'Details'}</button>{onClear && <button type="button" onClick={onClear} aria-label="Clear person selection"><HudIcon name="close" size={18} /></button>}</div>
    <div className="hud-card-body">
      <div className="hud-portrait"><img src={portrait} alt="" /></div>
      <div className="hud-card-facts">
        <div className="hud-chips"><span className="hud-chip hud-chip-blue">{citizen.lifeStage}</span><span className="hud-chip hud-chip-gold">{citizen.newcomer ? citizenObservationLabel(citizen) : citizen.occupation}</span></div>
        <span className="hud-card-activity">{activity}</span>
        {citizen.carriedResource && <span className="hud-card-detail">Carrying {citizen.carriedQuantity ?? 0} {citizen.carriedResource.toLowerCase()}</span>}
      </div>
    </div>
    {isCitizenPresent(citizen) && <div className="hud-needs">{NEEDS.map(({ key, label }) => {
      const need = citizen[key]
      if (need === null) return null
      const met = Math.round((1 - Math.max(0, Math.min(10000, need)) / 10000) * 100)
      return <div key={key} className={`hud-need hud-need-${key}`} role="meter" aria-label={`${label}: ${met}%`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={met}>
        <span aria-hidden="true">{label}</span><span className="hud-bar"><span className="hud-bar-fill" style={{ width: `${met}%` }} /></span>
      </div>
    })}</div>}
    <div className="hud-card-actions">
      {onLocate && <button type="button" className="hud-button hud-button-cream" disabled={!isCitizenPresent(citizen)} onClick={onLocate}><HudIcon name="target" size={20} />Locate</button>}
      {onFavorite && <button type="button" className="hud-button hud-button-cream hud-favorite" aria-pressed={favorite} aria-label={`${favorite ? 'Remove' : 'Add'} ${citizen.name} ${favorite ? 'from' : 'to'} favorites`} onClick={onFavorite}><HudIcon name="star" size={20} />{favorite ? 'Favorited' : 'Favorite'}</button>}
      {isCitizenPresent(citizen) && <button type="button" className="hud-button hud-button-green" onClick={onFollow}><HudIcon name="eye" size={20} />{following ? 'Unfollow' : 'Follow'}</button>}
      {onOpen && <button type="button" className="hud-button hud-button-gold" onClick={onOpen}><HudIcon name="book" size={20} />Open record</button>}
      {onViewFamily && <button type="button" className="hud-button hud-button-cream hud-family-action" onClick={onViewFamily}>View family</button>}
    </div>
    {notice && <p className="hud-favorites-notice" role="status">{notice}</p>}
  </div>
}
