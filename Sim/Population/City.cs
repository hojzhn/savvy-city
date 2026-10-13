using YouWillDate.Sim.Generation;
using YouWillDate.Sim.Life;
using YouWillDate.Sim.Relations;

namespace YouWillDate.Sim.Population;

/// <summary>A pawn living in the city, with its links to the other residents.</summary>
public sealed class Resident
{
    public required int Id { get; init; }
    public required Pawn Pawn { get; init; }
    /// <summary>Profile vector x, z-scored (attraction spec 7.2).</summary>
    public double[] X { get; internal set; } = [];
    public Aura Aura { get; internal set; } = new([], []);
    public Taste Taste { get; internal set; } = new([], []);
    /// <summary>Direction of desire u (8.3), length 1.</summary>
    public double[] U { get; internal set; } = [];

    /// <summary>The current partner, a resident. Null when single.</summary>
    public int? PartnerId { get; internal set; }
    public int? CoupleId { get; internal set; }
    public int HouseholdId { get; internal set; } = -1;
    /// <summary>For each entry of Pawn.Partners: the resident that partner is, or null when it stays a record.</summary>
    public int?[] PartnerLinks { get; internal set; } = [];
    /// <summary>For each close-friend record in the adult profile: the resident that friend is, or null when it stays a record.</summary>
    public int?[] FriendLinks { get; internal set; } = [];

    public string Sex => Pawn.Sex;
    public int Age => Pawn.Age;
    public string Relationship => Pawn.Facts.Cat("state.relationship") ?? "single";
    public int RelationshipYears => (int)(Pawn.Facts.Num("state.relationship_years") ?? 0);
    public int Children => (int)(Pawn.Facts.Num("state.children") ?? 0);
    /// <summary>The ongoing relationship in the life history, if any.</summary>
    public Partner? CurrentPartner => Pawn.Partners.Count > 0 && !Pawn.Partners[^1].Ended ? Pawn.Partners[^1] : null;
    public IReadOnlyList<Adult.FriendRecord> Friends => Pawn.Adult?.Friends ?? [];
}

/// <summary>Two residents in a current relationship. Shared values are the couple's, reconciled from both histories.</summary>
public sealed record Couple(int Id, int A, int B, string Status, int Years, int Children, double MatchCost);

/// <summary>People living together. Kind is the life simulation's current_household value.</summary>
public sealed record Household(int Id, IReadOnlyList<int> Members, int Children, string Kind);

public sealed record CityStats(int Generated, int Discarded, int DroppedUnmatched, int ExLinks, int FriendLinks);

/// <summary>A city of linked pawns: real people on both ends of every current relationship.</summary>
public sealed class City
{
    public required ulong Seed { get; init; }
    public required World World { get; init; }
    public required IReadOnlyList<Resident> Residents { get; init; }
    public required IReadOnlyList<Couple> Couples { get; init; }
    public required IReadOnlyList<Household> Households { get; init; }
    public required CityStats Stats { get; init; }
    /// <summary>Spec section 9: the runtime's state at the start of play.</summary>
    public required StartingState Start { get; init; }
}
