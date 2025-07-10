using System;
using TMG.Ilute.Data;
using XTMF;

namespace TMG.Ilute.Data.LabourForce
{
    /// <summary>
    /// Provides an empty repository of jobs for modules that wish to track job records.
    /// </summary>
    public sealed class JobRepositoryDataSource : IDataSource<Repository<Job>>
    {
        public bool Loaded { get; private set; }

        public string Name { get; set; }

        public float Progress => 0f;

        public Tuple<byte, byte, byte> ProgressColour => new Tuple<byte, byte, byte>(50, 150, 50);

        private Repository<Job> _data;


        public Repository<Job> GiveData() => _data;

        public void LoadData()
        {
            try
            {
                var repo = new Repository<Job>();

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