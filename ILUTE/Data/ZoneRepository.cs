using System;
using System.Collections.Generic;

namespace TMG.Ilute.Data
{


    // These zone-level attributes are required for computing the asking price of a dwelling.
    // They are accessed by AskPrice.cs during price estimation and reflect socioeconomic and market conditions.

    public interface IZone

    {
        int Id { get; }
        float AvgSellPriceDet { get; }
        float AvgSellPriceSemi { get; }
        float AvgSellPriceAptH { get; }
        float AvgSellPriceAptL { get; }
        float AvgPplPerRoom { get; }
        float AvgDwellValue { get; }
        float UnEmplRate { get; }
        float AvgDaysListedOnMarket { get; }

        void UpdateTo(Date currentDate);
        void ResetOutMig();
        void CalculateOutMigrationRates();
    }

    public class ZoneRepository<T> where T : IZone
    {

        // Dictionary is meant to provide fast access by zone ID
        private readonly Dictionary<int, T> _zones;

        public ZoneRepository()
        {
            _zones = new Dictionary<int, T>();
        }

        public void AddZone(T zone)
        {
            if (_zones.ContainsKey(zone.Id))
                throw new ArgumentException($"Zone with ID {zone.Id} already exists.");

            _zones[zone.Id] = zone;
        }

        public T GetZoneById(int id)
        {
            if (_zones.TryGetValue(id, out var zone))
                return zone;

            throw new KeyNotFoundException($"Zone with ID {id} not found.");
        }

        public bool TryGetZoneById(int id, out T zone)
        {
            return _zones.TryGetValue(id, out zone);
        }

        public IEnumerable<T> GetAllZones()
        {
            return _zones.Values;
        }

        public bool RemoveZone(int id)
        {
            return _zones.Remove(id);
        }

        public void Clear()
        {
            _zones.Clear();
        }

        public bool ContainsZone(int id)
        {
            return _zones.ContainsKey(id);
        }

        public void UpdateZones(Date currentDate)
        {
            foreach (var zone in _zones.Values)
            {
                zone.UpdateTo(currentDate);
            }
        }

        public void ResetOutMigration()
        {
            foreach (var zone in _zones.Values)
            {
                zone.ResetOutMig();
            }
        }

        public void CalculateAllOutMigrationRates()
        {
            foreach (var zone in _zones.Values)
            {
                zone.CalculateOutMigrationRates();
            }
        }
    }
}
