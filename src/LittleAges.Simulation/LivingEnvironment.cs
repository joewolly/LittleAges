using LittleAges.Domain;

namespace LittleAges.Simulation;

public sealed partial class SimulationEngine
{
    // Dedicated purpose range in a stateless domain: legacy RNG calls and sequences remain unchanged.
    private ulong LivingRandom(long id, ulong purpose) => new DeterministicRandom(Seed).NextUInt64(RandomDomain.DecisionVariation,
        (ulong)id, (ulong)(CurrentMinute.Value / WorldCalendar.MinutesPerDay), 20000 + purpose);
    private void AdvanceLivingEnvironment()
    {
        var state = _living!;
        var day = CurrentMinute.Value / WorldCalendar.MinutesPerDay;
        var season = CurrentMinute.ToCalendar().Season;
        var pattern = new DeterministicRandom(Seed).NextUInt64(RandomDomain.DecisionVariation, 0, (ulong)(day / 7), 21000) % 100;
        var weather = pattern < 12 ? LivingWeatherKind.Drought : pattern < 22 ? LivingWeatherKind.ColdSpell : pattern < 52 ? LivingWeatherKind.Rain : LivingWeatherKind.Fair;
        if (weather != state.Weather) Fact(LivingFactKind.WeatherChanged, value: (int)weather);
        state.Weather = weather;
        state.Temperature = (season switch { WorldSeason.Spring => 14, WorldSeason.Summer => 25, WorldSeason.Autumn => 12, _ => -3 }) + (weather == LivingWeatherKind.ColdSpell ? -12 : weather == LivingWeatherKind.Drought ? 5 : 0);
        state.Rainfall = weather switch { LivingWeatherKind.Rain => 85, LivingWeatherKind.Drought => 0, _ => 25 };
        foreach (var field in state.Fields)
        {
            field.Moisture = Math.Clamp(field.Moisture + state.Rainfall * 30 - 1200, 0, 10000);
            if (field.SownMinute < 0 || field.YieldRemaining > 0) continue;
            var stressed = field.Moisture < 1500 || state.Temperature < 0;
            field.Condition = Math.Clamp(field.Condition + (stressed ? -500 : 100), 0, 10000);
            if (field.Condition == 0) { field.SownMinute = -1; field.Growth = 0; continue; }
            field.Growth = Math.Min(10000, field.Growth + (stressed ? 50 : 350 + World.GetTile(field.Location).Fertility / 30));
            if (field.Growth == 10000) field.YieldRemaining = Math.Max(60, 900 * field.Condition / 10000);
        }
        if (weather == LivingWeatherKind.Drought)
            foreach (var node in World.Resources.Where(x => x.Type == ResourceType.Food))
                _resourceStates[node.Id.Value].CurrentQuantity = Math.Max(0, _resourceStates[node.Id.Value].CurrentQuantity - Math.Max(1, node.RegenerationPotential / 4));
        foreach (var item in state.Stock.Where(x => x.Good is LivingGood.Grain or LivingGood.PreservedFood or LivingGood.Fiber or LivingGood.Hide).ToArray())
        {
            var spoilage = item.Good == LivingGood.Grain ? item.Quantity / 100 : item.Good is LivingGood.Fiber or LivingGood.Hide ? item.Quantity / 200 : day % 30 == 0 ? item.Quantity / 200 : 0;
            ChangeGood(item.Good, -spoilage); state.GoodsSpoiled += spoilage;
        }
        var neededFood = Math.Max(0, LivingPopulation * 30 - Settlement.FoodStored);
        var release = Math.Min(neededFood, Good(LivingGood.PreservedFood));
        ChangeGood(LivingGood.PreservedFood, -release); Settlement.FoodStored += release;
        if (state.Temperature < 5)
            ChangeGood(LivingGood.Fuel, -Math.Min(Good(LivingGood.Fuel), Math.Max(1, (LivingPopulation + 3) / 4)));
        AdvanceLivingWildlife(day);
    }
    private void AdvanceLivingWildlife(long day)
    {
        var state = _living!;
        foreach (var animal in state.Animals.OrderBy(x => x.Id).ToArray())
        {
            if (!state.Animals.Contains(animal)) continue;
            var neighbours = Enumerable.Range(Math.Max(0, animal.Location.Y - 2), Math.Min(World.Height - 1, animal.Location.Y + 2) - Math.Max(0, animal.Location.Y - 2) + 1)
                .SelectMany(y => Enumerable.Range(Math.Max(0, animal.Location.X - 2), Math.Min(World.Width - 1, animal.Location.X + 2) - Math.Max(0, animal.Location.X - 2) + 1).Select(x => World.GetTile(new TileCoordinate(x, y))))
                .Where(x => x.Walkable && Distance(x.Coordinate, animal.Location) <= 2).OrderBy(x => x.Coordinate).ToArray();
            if (!animal.Predator && neighbours.Length > 0)
            {
                var threats = _citizens.Values.Where(x => x.IsAlive && Distance(x.Location, animal.Location) <= 5).Select(x => x.Location)
                    .Concat(state.Animals.Where(x => x.Predator && Distance(x.Location, animal.Location) <= 5).Select(x => x.Location)).ToArray();
                if (threats.Length > 0)
                {
                    var safety = neighbours.Max(x => threats.Min(t => Math.Min(4, Distance(t, x.Coordinate))));
                    neighbours = neighbours.Where(x => threats.Min(t => Math.Min(4, Distance(t, x.Coordinate))) == safety).ToArray();
                }
            }
            if (neighbours.Length > 0) animal.Location = neighbours[(int)(LivingRandom(animal.Id, 90) % (ulong)neighbours.Length)].Coordinate;
            animal.Energy = Math.Max(0, animal.Energy - (animal.Predator ? 400 : 150));
            if (animal.Predator)
            {
                var prey = state.Animals.Where(x => !x.Predator && Distance(x.Location, animal.Location) <= 2).OrderBy(x => x.Id).FirstOrDefault();
                if (prey is not null) { state.Animals.Remove(prey); animal.Energy = Math.Min(10000, animal.Energy + 3000); }
                var exposed = _citizens.Values.Where(x => x.IsAlive && x.Location == animal.Location && x.HomeStructureId is null).OrderBy(x => x.Id.Value).FirstOrDefault();
                if (exposed is not null && LivingRandom(animal.Id, 91) % 8 == 0) InjureLiving(exposed, 1500);
            }
            else
            {
                var crop = state.Fields.FirstOrDefault(x => Distance(x.Location, animal.Location) <= 1 && x.SownMinute >= 0);
                if (crop is not null) { crop.Condition = Math.Max(0, crop.Condition - 150); animal.Energy = Math.Min(10000, animal.Energy + 600); }
                else if (World.GetTile(animal.Location).Fertility > 3000) animal.Energy = Math.Min(10000, animal.Energy + (state.Temperature > 0 ? 250 : 100));
            }
            if (animal.Energy == 0) { state.Animals.Remove(animal); continue; }
            if (day % 30 == 0 && animal.Energy > 7000 && state.Animals.Count < 40 && state.Animals.Any(x => x.Id != animal.Id && x.Predator == animal.Predator && Distance(x.Location, animal.Location) <= 8))
            {
                animal.Energy -= 2000;
                state.Animals.Add(new LivingAnimal { Id = state.NextId++, Predator = animal.Predator, Location = animal.Location, BornMinute = CurrentMinute.Value, Energy = 5000 });
            }
        }
        state.Animals.Sort((a, b) => a.Id.CompareTo(b.Id));
    }
}
