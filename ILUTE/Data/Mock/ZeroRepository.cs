﻿using System;
using XTMF;

namespace TMG.Ilute.Data.Mock;

[ModuleInformation(Description = "Always returning 0")]
public sealed class ZeroRepository : IDataSource<Repository<FloatData>>
{

    // SYNTAX: Note that => means if someone asks for Loaded it will just return the result of _data is not null
    public bool Loaded => _data is not null;

    [RootModule]
    public ITravelDemandModel Root = null!;
    

    private Repository<FloatData>? _data;

   // Repository will store, reveive and update data

    [SubModelInformation(Required = false, Description = "zone system to use")]
    public IDataSource<IZoneSystem>? ZoneSystem = null!;

    public string Name { get; set; } = null!;

    public float Progress => 0f;

    public Tuple<byte, byte, byte> ProgressColour => new(50, 150, 50);

    public Repository<FloatData>? GiveData()
    {
        return _data;
    }

    public void LoadData()
    {
        var data = new Repository<FloatData>();

        // Ensure the repository is initialized so dependents are setup
        data.LoadData();

        // SYNTAX: The Repository class is defined in Repository.cs

        var zoneSystemToLoad = ZoneSystem ?? Root?.ZoneSystem;

        if (zoneSystemToLoad != null)
        {
            var loaded = zoneSystemToLoad.Loaded;

       

            if (!loaded)
            {
                zoneSystemToLoad.LoadData();
            }

            var zoneSystem = zoneSystemToLoad.GiveData()
                    ?? throw new XTMFRuntimeException(this, "Unable to load zone system!");

            if (!loaded)
            {
                zoneSystemToLoad.UnloadData();
            }

            foreach (var index in zoneSystem.ZoneArray.ValidIndexies())
            {
                data.AddNew(index, new FloatData());
            }
        }

        _data = data;

    }



    public bool RuntimeValidation(ref string? error)
    {
        return true;
    }

    public void UnloadData()
    {
        _data = null;


    }
}
