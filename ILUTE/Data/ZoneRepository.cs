using System;
using System.Collections.Generic;
using TMG.Ilute.Data;

namespace TMG.Ilute.Data
{
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

    public class ZoneRepository<T> where T : IndexedObject, IZone
    {
        private readonly Repository<T> _zones;

        public ZoneRepository(Repository<T> repository)
        {
            _zones = repository;
        }

        public void AddZone(T zone)
        {
            _zones.AddNew(zone);
        }

        public T GetZoneById(long id)
        {
            return _zones.GetByID(id);
        }

        public bool TryGetZoneById(long id, out T zone)
        {
            return _zones.TryGet(id, out zone);
        }

        public IEnumerable<T> GetAllZones()
        {
            return _zones;
        }

        public bool RemoveZone(long id)
        {
            _zones.Remove(id);
            return true;
        }

        public void Clear()
        {
            _zones.UnloadData();
        }

        public bool ContainsZone(long id)
        {
            return _zones.TryGet(id, out _);
        }

        public void UpdateZones(Date currentDate)
        {
            foreach (var zone in _zones)
            {
                zone.UpdateTo(currentDate);
            }
        }

        public void ResetOutMigration()
        {
            foreach (var zone in _zones)
            {
                zone.ResetOutMig();
            }
        }

        public void CalculateAllOutMigrationRates()
        {
            foreach (var zone in _zones)
            {
                zone.CalculateOutMigrationRates();
            }
        }
    }
}
