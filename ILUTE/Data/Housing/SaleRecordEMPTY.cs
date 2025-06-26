using System;
using TMG.Ilute.Data;
using XTMF;

namespace TMG.Ilute.Data.Housing
{
    /// <summary>
    /// Provides an initially empty repository of sale records.
    /// </summary>
    public sealed class SaleRecordRepositoryDataSource : IDataSource<Repository<SaleRecord>>
    {
        public bool Loaded { get; private set; }

        public string Name { get; set; }

        public float Progress => 0f;

        public Tuple<byte, byte, byte> ProgressColour => new Tuple<byte, byte, byte>(50, 150, 50);

        private Repository<SaleRecord> _data;

        public Repository<SaleRecord> GiveData() => _data;

        public void LoadData()
        {
            try
            {
                var repo = new Repository<SaleRecord>();
                repo.LoadData();
                _data = repo;
                Loaded = true;
            }
            catch (Exception ex)
            {
                throw new XTMFRuntimeException(this, ex);
            }
        }

        public bool RuntimeValidation(ref string error) => true;

        public void UnloadData()
        {
            Loaded = false;
            _data = null;
        }
    }
}