using LittleAges.Domain;
using Xunit;

namespace LittleAges.Domain.Tests;

public sealed class M17NewcomerContractTests
{
    [Fact]
    public void ExternalBiographyAndContactsRoundTripWithoutLegacyFields()
    {
        var legacy = LivingWorldCodec.Serialize(new LivingWorldState());
        Assert.DoesNotContain("newcomers", legacy);
        var guest = new Citizen(new CitizenId(9007199254740993), (int?)null, "Ari", "Ash", -18L * WorldCalendar.MinutesPerYear,
            new(0, 4), new(1, 2, 3, 4, 5, 6), new(11, 12, 13, 14, 15, 16), new(10, 20, 30, 40))
        { CurrentAction = CitizenAction.Idle, ActionPhase = CitizenActionPhase.Perform, ActionStartedMinute = new(0), ActionCompletesMinute = new(60), ActionSequence = 7 };
        var state = new LivingWorldState { Newcomers = new() { Visitors = [new() { CitizenId = guest.Id.Value, Person = guest,
            Contacts = [new(new CitizenId(2), guest.Id, 500, -300, 100, 600, 0, 1)] }] } };
        var json = LivingWorldCodec.Serialize(state);
        Assert.Contains("\"citizenId\":\"9007199254740993\"", json);
        var restored = LivingWorldCodec.Deserialize(json);
        Assert.Equal(guest, restored.Newcomers!.Visitors.Single().Person);
        Assert.Equal(state.Newcomers.Visitors.Single().Contacts, restored.Newcomers.Visitors.Single().Contacts);
        Assert.Equal(json, LivingWorldCodec.Serialize(restored));
        Assert.Equal(legacy, LivingWorldCodec.Serialize(LivingWorldCodec.Deserialize(legacy)));
    }

    [Fact]
    public void TravelerAccountBalancesConsumptionAdmissionAndExportSeparately()
    {
        var visitor = new NewcomerState { InitialProvisions = 100, ProvisionsRemaining = 70, ProvisionsConsumed = 30 };
        visitor.ValidateProvisions();
        visitor.ProvisionsRemaining = 69;
        Assert.Throws<ArgumentException>(visitor.ValidateProvisions);
        visitor.ProvisionsRemaining = 0; visitor.ProvisionsTransferred = 70;
        Assert.Throws<ArgumentException>(visitor.ValidateProvisions);
        visitor.JoinedMinute = 100; visitor.Phase = NewcomerPhase.Resident;
        visitor.ValidateProvisions();
        visitor.ProvisionsTransferred = 0; visitor.JoinedMinute = null; visitor.Phase = NewcomerPhase.Departed; visitor.ProvisionsExported = 70;
        visitor.ValidateProvisions();
        visitor.Phase = NewcomerPhase.Dead;
        Assert.Throws<ArgumentException>(visitor.ValidateProvisions);
    }

    [Fact]
    public void GuestHistoryAvoidsForeignKeysUntilAdmissionAndHasCanonicalPayloads()
    {
        var id = new HistoricalEventId(1);
        foreach (var (type, payload) in new[]
        {
            (HistoricalEventType.NewcomerAppeared, HistoricalEventPayloads.NewcomerAppeared(40, 1)),
            (HistoricalEventType.VisitorArrived, HistoricalEventPayloads.VisitorArrived(40, 2)),
            (HistoricalEventType.VisitorContact, HistoricalEventPayloads.VisitorContact(40, 1, 2, 1000, -500, 500, 600, 2)),
            (HistoricalEventType.VisitorDeparted, HistoricalEventPayloads.VisitorDeparted(40, 1, "lost_support", 25)),
            (HistoricalEventType.VisitorDied, HistoricalEventPayloads.VisitorDied(40, 1, "exposure", 20))
        })
        {
            var item = new HistoricalEvent(id, 1, type, HistoricalImportance.Routine, HistoricalEventOrigin.Live, new(0, 1), payload).Validate();
            item.ValidateLinks([], []);
            Assert.Throws<ArgumentException>(() => item.ValidateLinks([new(id, new CitizenId(40), "subject")], []));
            Assert.False(HistoricalEventPayloads.IsCanonical(type, payload + " "));
        }
        var joined = new HistoricalEvent(id, 1, HistoricalEventType.NewcomerJoined, HistoricalImportance.Notable, HistoricalEventOrigin.Live,
            new(1, 1), HistoricalEventPayloads.NewcomerJoined(40, 1, 41, 25, 30)).Validate();
        joined.ValidateLinks([new(id, new CitizenId(40), "subject")], []);
        Assert.Throws<ArgumentException>(() => joined.ValidateLinks([], []));
        Assert.Throws<ArgumentException>(() => HistoricalEventPayloads.VisitorContact(40, 1, 40, 500, 0, 100, 0, 1));
        Assert.Throws<ArgumentException>(() => HistoricalEventPayloads.VisitorDeparted(40, 3, "deadline", 0));
    }
}
