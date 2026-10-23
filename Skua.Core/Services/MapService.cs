using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using System.Collections.Immutable;

namespace Skua.Core.Services;

public partial class MapService : ObservableObject, IMapService
{
    private readonly IScriptMap Map;
    private readonly IScriptPlayer Player;

    public MapService(IScriptMap map, IScriptPlayer player)
    {
        Map = map;
        Player = player;
    }

    public string MapName => Map.Name;

    public string Cell => Player.Cell;

    public string Pad => Player.Pad;

    public List<string> Cells => Map.Cells;

    [ObservableProperty]
    private bool _UsePrivateRoom;

    [ObservableProperty]
    private int _PrivateRoomNumber = 111111;

    public ImmutableList<string> Pads { get; } = ImmutableList.Create("Spawn", "Center", "Left", "Right", "Up", "Down", "Top", "Bottom");

    public (string cell, string pad) GetCurrentCell()
    {
        return !Player.Playing ? ("Spawn", "Enter") : (Cell, Pad);
    }

    public (string mapName, string cell, string pad) GetCurrentLocation()
    {
        return !Player.Playing ? ("Battleon", "Spawn", "Enter") : (MapName, Cell, Pad);
    }

    public void Travel(TravelDestination? destination)
    {
        if (!Player.Playing || destination is null)
            return;
        Task.Run(() =>
        {
            if (destination.MapName != "tercessuinotlim")
            {
                Map.Join(UsePrivateRoom ? $"{destination.MapName}-{PrivateRoomNumber}" : destination.MapName, destination.Cell, destination.Pad);
                return;
            }

            if (Map.Name == "tercessuinotlim")
            {
                Map.Jump(destination.Cell, destination.Pad);
                return;
            }
            Map.Jump("m22", "Left");
            Map.Join(UsePrivateRoom ? $"tercessuinotlim-{PrivateRoomNumber}" : "tercessuinotlim", destination.Cell, destination.Pad);
        });
    }

    public void Jump(string cell, string pad)
    {
        if (!Player.LoggedIn)
            return;
        Map.Jump(cell, pad, false);
    }
}
