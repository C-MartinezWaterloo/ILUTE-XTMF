using System;
using System.Collections.Generic;
using System.Linq;
using TMG.Ilute.Data;
using TMG.Ilute.Data.Demographics;
using TMG.Ilute.Model.Utilities;
using XTMF;

namespace TMG.Ilute.Model.Demographic
{
    /// <summary>
    /// Adds each family's annual income to a running savings total.
    /// </summary>
    public sealed class SavingsCounter : IExecuteYearly
    {
        public string Name { get; set; }
        public float Progress => 0f;
        public Tuple<byte, byte, byte> ProgressColour => new Tuple<byte, byte, byte>(50, 150, 50);

        [SubModelInformation(Required = true, Description = "Repository of households.")]
        public IDataSource<Repository<Household>> HouseholdRepository;

        [SubModelInformation(Required = false, Description = "Currency conversion utilities.")]
        public IDataSource<CurrencyManager> CurrencyManager;
        private CurrencyManager _currencyManager;

        private Date _currentDate;

        public void AfterYearlyExecute(int currentYear) { }

        public void BeforeFirstYear(int firstYear)
        {
            if (CurrencyManager != null)
            {
                _currencyManager = Repository.GetRepository(CurrencyManager);
            }
        }

        public void BeforeYearlyExecute(int currentYear)
        {
            _currentDate = new Date(currentYear, 0);
            if (_currencyManager == null && CurrencyManager != null)
            {
                _currencyManager = Repository.GetRepository(CurrencyManager);
            }
        }

        public void Execute(int currentYear)
        {
            var households = Repository.GetRepository(HouseholdRepository);

            foreach (var fam in households.SelectMany(h => h.Families))
            {
                float income = fam.Persons
                    .SelectMany(p => p.Jobs)
                    .Sum(job =>
                    {
                        var salary = job.Salary.Amount;
                        if (_currencyManager != null)
                        {
                            salary = _currencyManager.ConvertToYear(job.Salary, _currentDate).Amount;

                            if (salary == 0f && job.Salary.Amount > 0f)
                            {
                                throw new XTMFRuntimeException(this, "currency manager is not working");
                            }
                        }
                        return salary;
                    });

                if (income > 0f)
                {
                    fam.Savings += income;
                    fam.LiquidAssets += income;
                }
            }
        }

        public void RunFinished(int finalYear) { }

        public bool RuntimeValidation(ref string error)
        {
            if (HouseholdRepository == null)
            {
                error = Name + ": missing households repository.";
                return false;
            }
            return true;
        }
    }
}