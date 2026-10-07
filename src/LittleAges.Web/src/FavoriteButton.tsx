import { HudIcon } from './world/Hud'

export function FavoriteButton({ id, name, favorites, onToggle }: { id: string; name: string; favorites: ReadonlySet<string>; onToggle: (id: string) => void }) {
  const favorite = favorites.has(id)
  return <button type="button" className="observer-button favorite-button" aria-pressed={favorite} aria-label={`${favorite ? 'Remove' : 'Add'} ${name} ${favorite ? 'from' : 'to'} favorites`} onClick={() => onToggle(id)}>
    <HudIcon name="star" size={20} /><span>{favorite ? 'Favorited' : 'Favorite'}</span>
  </button>
}
