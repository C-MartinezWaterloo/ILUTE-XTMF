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
        [RunParameter("Average Salary", 20000f, "Mean salary of new jobs")] public float AverageSalary;
        [RunParameter("Salary StdDev", 10000f, "Standard deviation for salary")] public float SalaryStdDev;

        private RandomStream _random;
        private Date _currentDate;

        public void BeforeFirstYear(int firstYear)
        {
            RandomStream.CreateRandomStream(ref _random, Seed);
        }

        public void BeforeYearlyExecute(int currentYear)
        {
            _currentDate = new Date(currentYear, 0);
        }

        public void AfterYearlyExecute(int currentYear) { }
        public void RunFinished(int finalYear) { _random?.Dispose(); _random = null; }

        public void Execute(int currentYear)
        {
            var persons = Repository.GetRepository(PersonRepository);
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
                        }
                    }
                }
            });
        }

        public bool RuntimeValidation(ref string error)
        {
            if (PersonRepository == null)
            {
                error = Name + ": missing persons repository.";
                return false;
            }
            return true;
        }
    }
}