namespace Skua.Core.Models;

/// <summary>A game location independent of the interface used to select it.</summary>
public sealed record TravelDestination(string MapName, string Cell, string Pad);
