using System;
using System.Collections.Generic;
using System.Linq;
using TMG.Ilute.Data;
using TMG.Ilute.Data.Demographics;
using TMG.Ilute.Data.LabourForce;
using TMG.Ilute.Model.Utilities;
using XTMF;

namespace TMG.Ilute.Model.Demographic
{
    /// <summary>
    /// Simple employment model that assigns new jobs to unemployed adults
    /// and updates their income accordingly.
    /// </summary>
    public sealed class JobGrowth : IExecuteYearly
    {
        public string Name { get; set; }
        public float Progress => 0f;
        public Tuple<byte, byte, byte> ProgressColour => new Tuple<byte, byte, byte>(50, 150, 50);

        [SubModelInformation(Required = true, Description = "Repository of persons.")]
        public IDataSource<Repository<Person>> PersonRepository;

        [RunParameter("Seed", 123u, "Random seed for job creation")] public uint Seed;
        [RunParameter("Hiring Probability", 0.05f, "Chance an adult without a job gets hired each year")] public float HiringProbability;
        [RunParameter("Average Salary", 25000f, "Mean salary of new jobs")] public float AverageSalary;
        [RunParameter("Salary StdDev", 10000f, "Standard deviation for salary")] public float SalaryStdDev;

        [SubModelInformation(Required = false, Description = "Optional repository of jobs.")]
        public IDataSource<Repository<Job>> JobRepository;
        private Repository<Job> _jobRepo;

        [SubModelInformation(Required = false, Description = "Optional log output.")]
        public IDataSource<ExecutionLog> LogSource;
        private ExecutionLog _log;

        private RandomStream _random;
        private Date _currentDate;

        public void BeforeFirstYear(int firstYear)
        {
            RandomStream.CreateRandomStream(ref _random, Seed);
            if (JobRepository != null)
            {
                _jobRepo = Repository.GetRepository(JobRepository);
            }
            if (LogSource != null)
            {
                _log = Repository.GetRepository(LogSource);
            }
        }

        public void BeforeYearlyExecute(int currentYear)
        {
            _currentDate = new Date(currentYear, 0);
            if (_jobRepo == null && JobRepository != null)
            {
                _jobRepo = Repository.GetRepository(JobRepository);
            }
            if (_log == null && LogSource != null)
            {
                _log = Repository.GetRepository(LogSource);
            }
        }

        public void AfterYearlyExecute(int currentYear) { }
        public void RunFinished(int finalYear)
        {
            _random?.Dispose();
            _random = null;
            _jobRepo = null;
            _log = null;
        }

        public void Execute(int currentYear)
        {
            var persons = Repository.GetRepository(PersonRepository);

            // the is esentially going through the person who are >= 16, and adding a new job based on probability 
            int jobsCreated = 0;
            _random.ExecuteWithProvider(rand =>
            {
                foreach (var person in persons)
                {
                    if (person.Living && person.Age >= 16 && person.Jobs.Count == 0)
                    {
                        if (rand.NextFloat() < HiringProbability)
                        {
                            float salary = AverageSalary + (float)(rand.InvStdNormalCDF() * SalaryStdDev);
                            if (salary < 0f) salary = AverageSalary;
                            var job = new Job
                            {
                                Owner = person,
                                StartDate = _currentDate,
                                Salary = new Money(salary, _currentDate),
                                OccupationClassification = OccupationClassification.NotApplicable,
                                IndustryClassification = IndustryClassification.NotApplicable
                            };
                            person.Jobs.Add(job);
                            person.LabourForceStatus = LabourForceStatus.Employed;
                            if (_jobRepo != null)
                            {
                                _jobRepo.AddNew(job);
                            }
                            jobsCreated++;
                        }
                    }
                }
            });
            _log?.WriteToLog($"Year {currentYear}: created {jobsCreated} jobs.");
        }

        public bool RuntimeValidation(ref string error)
        {
            if (PersonRepository == null)
            {
                error = Name + ": missing persons repository.";
                return false;
            }
            if (JobRepository != null && !JobRepository.Loaded)
            {
                error = Name + ": job repository was not loaded.";
                return false;
            }
            return true;
        }
    }
}