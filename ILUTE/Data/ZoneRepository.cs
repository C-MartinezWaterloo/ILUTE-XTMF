using System;
using System.Collections.Generic;
using TMG.Ilute.Data;

namespace TMG.Ilute.Data
{
    // Interface that represents a generalized zone structure.
    // All zones used in the repository must implement these properties and methods.
    public interface IZone
    {
        int Id { get; }                           // Unique identifier for the zone
        float X { get; }                         // X Coordinate
        float Y { get; }                         // Y Coordinate
        float Area { get; }                      // Area
        float AvgSellPriceDet { get; }           // Average sale price for detached dwellings
        float AvgSellPriceSemi { get; }          // Average sale price for semi-detached dwellings
        float AvgSellPriceAptH { get; }          // Average sale price for high-rise apartments
        float AvgSellPriceAptL { get; }          // Average sale price for low-rise apartments
        float AvgPplPerRoom { get; }             // Average number of people per room (density metric)
        float AvgDwellValue { get; }             // Average dwelling value in this zone
        float UnEmplRate { get; }                // Unemployment rate for the zone
        float AvgDaysListedOnMarket { get; }     // Average days properties are listed before being sold

        // Simulation time-step logic
        void UpdateTo(Date currentDate);         // Updates the zone based on the current simulation date
        void ResetOutMig();                      // Resets out-migration counters
        void CalculateOutMigrationRates();       // Calculates rates of out-migration based on tracked values
    }

    // A generic repository for any zone type implementing IZone and IndexedObject
    public class ZoneRepository<T> where T : IndexedObject, IZone
    {
        // Internal storage using ILUTE's thread-safe Repository<T>
        private readonly Repository<T> _zones;

     
        public ZoneRepository(Repository<T> repository)
        {
            _zones = repository;
        }

        
        public void AddZone(T zone)
        {
            _zones.AddNew(zone);
        }

        // Retrieves a zone by its unique index (ID); throws if not found
        public T GetZoneById(long id)
        {
            return _zones.GetByID(id);
        }

        // Tries to retrieve a zone by ID without throwing if it's missing
        public bool TryGetZoneById(long id, out T zone)
        {
            return _zones.TryGet(id, out zone);
        }

        // Returns all zones currently stored
        public IEnumerable<T> GetAllZones()
        {
            return _zones;
        }

        // Removes a zone by ID; always returns true since ILUTE handles errors internally
        public bool RemoveZone(long id)
        {
            _zones.Remove(id);
            return true;
        }

        // Clears all zone data from memory (used to reset or reload)
        public void Clear()
        {
            _zones.UnloadData();
        }

        // Checks whether a zone with the given ID exists
        public bool ContainsZone(long id)
        {
            return _zones.TryGet(id, out _);
        }

        // Applies a date update to all zones (e.g., simulate a new month/year)
        public void UpdateZones(Date currentDate)
        {
            foreach (var zone in _zones)
            {
                zone.UpdateTo(currentDate);
            }
        }

        // Resets out-migration counters across all zones
        public void ResetOutMigration()
        {
            foreach (var zone in _zones)
            {
                zone.ResetOutMig();
            }
        }

        // Recalculates out-migration rates for all zones
        public void CalculateAllOutMigrationRates()
        {
            foreach (var zone in _zones)
            {
                zone.CalculateOutMigrationRates();
            }
        }
    }
}
