using System;
using System.Collections.Generic;
using System.IO;
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


        [RunParameter("Seed", 123u, "Random seed for job creation")] public uint Seed;
        [RunParameter("Hiring Probability", 0.05f, "Chance an adult without a job gets hired each year")] public float HiringProbability;
        [RunParameter("Average Salary", 25000f, "Mean salary of new jobs")] public float AverageSalary;
        [RunParameter("Salary StdDev", 10000f, "Standard deviation for salary")] public float SalaryStdDev;

        [SubModelInformation(Required = true, Description = "Repository of persons.")]
        public IDataSource<Repository<Person>> PersonRepository;

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
                            // making a simple variation in salary.
                            float salary = AverageSalary + (float)(rand.InvStdNormalCDF() * SalaryStdDev);

                            if (salary < 0f) salary = AverageSalary;
                            var job = new Job
                            {
                                Owner = person,
                                StartDate = _currentDate,
                                Salary = new Money(salary, _currentDate),
                                OccupationClassification = OccupationClassification.NotApplicable,
                                IndustryClassification = IndustryClassification.NotApplicable,
                                Zone = person.Family?.Household?.Dwelling?.Zone ?? 0
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
            if (JobRepository != null)
            {
                if (!JobRepository.RuntimeValidation(ref error))
                {
                    return false;
                }
                if (!JobRepository.Loaded)
                {
                    error = Name + ": job repository was not loaded.";
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Assigns jobs to unemployed workers using a multinomial logit model
    /// over TAZ alternatives and records the chosen job and probability.
    /// </summary>
    public sealed class JobAssignment : IExecuteYearly, IDisposable
    {
        public string Name { get; set; }
        public float Progress => 0f;
        public Tuple<byte, byte, byte> ProgressColour => new Tuple<byte, byte, byte>(50, 150, 50);

        [RunParameter("Seed", 123u, "Random seed for job assignment")] public uint Seed;
        [RunParameter("Output File", "JobAssignments.csv", "CSV to save chosen jobs and probabilities")] public string OutputFile;
        [RunParameter("Beta Accessibility", 1f, "Utility weight for accessibility logsum")] public float BetaAccessibility;
        [RunParameter("Beta Income", 1f, "Utility weight for income category")] public float BetaIncome;
        [RunParameter("Beta Sector", 1f, "Utility weight for employment sector")] public float BetaSector;
        [RunParameter("Beta Jobs", 1f, "Utility weight for number of jobs")] public float BetaJobs;

        [SubModelInformation(Required = true, Description = "Repository of persons.")]
        public IDataSource<Repository<Person>> PersonRepository;

        [SubModelInformation(Required = true, Description = "Repository of available jobs.")]
        public IDataSource<Repository<Job>> AvailableJobs;

        [SubModelInformation(Required = true, Description = "Accessibility logsum by zone.")]
        public IDataSource<Repository<FloatData>> AccessibilityLogsum;

        private Repository<Job> _jobRepo;
        private Repository<FloatData> _logsumRepo;
        private RandomStream _random;
        private StreamWriter _writer;

        public void BeforeFirstYear(int firstYear)
        {
            RandomStream.CreateRandomStream(ref _random, Seed);
            if (AvailableJobs != null)
            {
                _jobRepo = Repository.GetRepository(AvailableJobs);
            }
            if (AccessibilityLogsum != null)
            {
                _logsumRepo = Repository.GetRepository(AccessibilityLogsum);
            }
            _writer = new StreamWriter(OutputFile);
            _writer.WriteLine("Year,PersonId,JobId,Zone,Probability");
        }

        public void BeforeYearlyExecute(int currentYear)
        {
            if (_jobRepo == null && AvailableJobs != null)
            {
                _jobRepo = Repository.GetRepository(AvailableJobs);
            }
            if (_logsumRepo == null && AccessibilityLogsum != null)
            {
                _logsumRepo = Repository.GetRepository(AccessibilityLogsum);
            }
            if (_writer == null)
            {
                _writer = new StreamWriter(OutputFile, append: true);
            }
        }

        public void Execute(int currentYear)
        {
            var persons = Repository.GetRepository(PersonRepository);
            _random.ExecuteWithProvider(rand =>
            {
                foreach (var person in persons)
                {
                    if (!person.Living || person.Age < 16 || person.Jobs.Count > 0)
                    {
                        continue;
                    }

                    var incomeCat = GetIncomeCategory(person);
                    var sector = person.Jobs.Count > 0 ? person.Jobs[0].IndustryClassification : IndustryClassification.NotApplicable;

                    var alternatives = new List<(int zone, List<Job> jobs, float util)>();

                    foreach (var group in _jobRepo
                        .Where(j => sector == IndustryClassification.NotApplicable || j.IndustryClassification == sector)
                        .GroupBy(j => j.Zone))
                    {
                        float logsum = 0f;
                        if (_logsumRepo != null && _logsumRepo.TryGet(group.Key, out var ls))
                        {
                            logsum = ls.Data;
                        }
                        float jobCount = group.Count();
                        float sectorTerm = sector == IndustryClassification.NotApplicable ? 0f : BetaSector;
                        float util = BetaAccessibility * logsum + BetaIncome * incomeCat + sectorTerm + BetaJobs * jobCount;
                        alternatives.Add((group.Key, group.ToList(), util));
                    }

                    if (alternatives.Count == 0)
                    {
                        continue;
                    }

                    double sumExp = alternatives.Sum(a => Math.Exp(a.util));
                    float draw = rand.NextFloat();
                    float cumulative = 0f;
                    int chosenZone = alternatives[0].zone;
                    List<Job> chosenJobs = alternatives[0].jobs;
                    float chosenProb = 0f;

                    foreach (var alt in alternatives)
                    {
                        float prob = (float)(Math.Exp(alt.util) / sumExp);
                        cumulative += prob;
                        if (draw <= cumulative)
                        {
                            chosenZone = alt.zone;
                            chosenJobs = alt.jobs;
                            chosenProb = prob;
                            break;
                        }
                    }

                    if (chosenJobs.Count == 0)
                    {
                        continue;
                    }
                    var job = chosenJobs[(int)(rand.NextFloat() * chosenJobs.Count)];
                    job.Owner = person;
                    job.StartDate = new Date(currentYear, 0);
                    person.Jobs.Add(job);
                    person.LabourForceStatus = LabourForceStatus.Employed;
                    _jobRepo.Remove(job.Id);
                    _writer?.WriteLine($"{currentYear},{person.Id},{job.Id},{chosenZone},{chosenProb}");
                }
            });
        }

        private int GetIncomeCategory(Person person)
        {
            float income = person.Jobs.Sum(j => j.Salary.Amount);
            if (income < 30000f) return 0;
            if (income < 60000f) return 1;
            return 2;
        }

        public void AfterYearlyExecute(int currentYear) { }

        public void RunFinished(int finalYear)
        {
            _random?.Dispose();
            _random = null;
            _writer?.Dispose();
            _writer = null;
            _jobRepo = null;
            _logsumRepo = null;
        }

        public bool RuntimeValidation(ref string error)
        {
            if (PersonRepository == null)
            {
                error = Name + ": missing persons repository.";
                return false;
            }
            if (AvailableJobs == null)
            {
                error = Name + ": missing jobs repository.";
                return false;
            }
            if (AccessibilityLogsum == null)
            {
                error = Name + ": missing accessibility logsums.";
                return false;
            }
            return true;
        }

        public void Dispose()
        {
            _writer?.Dispose();
        }
    }
}