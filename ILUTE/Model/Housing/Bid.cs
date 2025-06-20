/*
    Copyright 2018 Travel Modelling Group, Department of Civil Engineering, University of Toronto

    This file is part of ILUTE, a set of modules for XTMF.

    XTMF is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    XTMF is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with XTMF.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMG.Ilute.Data;
using TMG.Ilute.Data.Demographics;
using TMG.Ilute.Data.Housing;
using TMG.Ilute.Data.Spatial;
using TMG.Ilute.Model.Utilities;
using TMG.Input;
using XTMF;

namespace TMG.Ilute.Model.Housing
{
    public sealed class Bid : ISelectPriceMonthly<Household, Dwelling>
    {
        public string Name { get; set; }

        public float Progress => 0f;

        public Tuple<byte, byte, byte> ProgressColour => new Tuple<byte, byte, byte>(50,150,50);

        [SubModelInformation(Required = true, Description = "Land-Use data for the Census zone system.")]
        public IDataSource<Repository<LandUse>> CensusLandUse;
        private Repository<LandUse> _censusLandUse;

        [SubModelInformation(Required = false, Description = "Currency conversion utilities.")]
        public IDataSource<CurrencyManager> CurrencyManager;
        private CurrencyManager _currencyManager;

        private Date _currentDate;

        private ConcurrentDictionary<int, float> _unemploymentByZone;

        [SubModelInformation(Required = true, Description = "The repository of households.")]
        public IDataSource<Repository<Household>> Households;

        [SubModelInformation(Required = false, Description = "Optional log output for bids.")]
        public IDataSource<ExecutionLog> LogSource;


        public void AfterMonthlyExecute(int currentYear, int month)
        {
        }

        public void AfterYearlyExecute(int currentYear)
        {
            _unemploymentByZone = null;
        }

        public void BeforeFirstYear(int firstYear)
        {
            try
            {
                _censusLandUse = Repository.GetRepository(CensusLandUse);
                if (CurrencyManager != null)
                {
                    _currencyManager = Repository.GetRepository(CurrencyManager);
                }
            }

            catch (XTMFRuntimeException e)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new XTMFRuntimeException(CensusLandUse.GiveData() as IModule ?? this, e);


            }
        }

        public void BeforeMonthlyExecute(int currentYear, int month)
        {
            _currentDate = new Date(currentYear, month);
        }

        public void BeforeYearlyExecute(int currentYear)
        {
            ComputeUnemploymentByZone();
            if (_currencyManager == null && CurrencyManager != null)
            {
                _currencyManager = Repository.GetRepository(CurrencyManager);
            }
        }

        private void ComputeUnemploymentByZone()
        {
            var data = new Dictionary<int, (int unemployed, int totalPersons)>();
            foreach(var hhld in Repository.GetRepository(Households))
            {
                if(hhld.Dwelling?.Zone is int zone)
                {
                    if(!data.TryGetValue(zone, out var record))
                    {
                        record = (0, 0);
                    }
                    foreach (var fam in hhld.Families)
                    {
                        foreach (var person in fam.Persons)
                        {
                            if (person.LabourForceStatus == LabourForceStatus.Unemployed)
                            {
                                record.unemployed++;
                            }
                        }
                    }
                    record.totalPersons += hhld.ContainedPersons;
                    data[zone] = record;
                }
            }
            _unemploymentByZone = new ConcurrentDictionary<int, float>(
                from record in data
                select new KeyValuePair<int, float>(record.Key, (float)record.Value.unemployed / record.Value.totalPersons)
            );
        }

        public void Execute(int currentYear, int month)
        {
        }

        public float GetPrice(Household buyer, Dwelling seller, float askingPrice)
        {

            // This calculates the bid amoujnt that a simulated buyer would offer to purchase a dwelling
            float income = GetHouseholdIncome(buyer);
            var buyerDwelling = buyer.Dwelling;

            // Land use effects
            if (!_censusLandUse.TryGet(seller.Zone, out var sellerLU))
            {
                // Default to zero-values when data is missing so bidding can continue
                sellerLU = new LandUse(seller.Zone, 0, 0, 0, 0);
            }



            float openChange = sellerLU.Open > 0 ? (float)Math.Log(sellerLU.Open) : 0f;
            float industrialChange = sellerLU.Industrial > 0 ? (float)Math.Log(sellerLU.Industrial) : 0f;

            // How many more rooms this dwelling offers
            int deltaRooms = buyerDwelling == null ? seller.Rooms : seller.Rooms - buyerDwelling.Rooms;

            // --- Bidding Logic ---

            // Base bid scaled to a multiple of annual income
            // Housing prices tend to be several times the buyer's yearly income,
            // so use a factor of four to better reflect market behaviour.
            float baseBid = 4.0f * income;

            // Bonus for more space (positive deltaRooms)
            float spaceValue = deltaRooms * 10000f;

            // Bonus/penalty for local land use
            float openBonus = openChange * 5000f;
            float industrialPenalty = industrialChange * 8000f;

            // Try to bid just under asking (simulates bargaining)
            float proximityDiscount = askingPrice * 0.97f; // start at 97% of asking

            // Final bid: income-based floor vs. environment/location adjusted ceiling
            float bid = Math.Min(proximityDiscount, baseBid + spaceValue + openBonus - industrialPenalty);

            bid = Math.Max(bid, 1.0f * income); // Don’t offer less than 20% of income

            return bid;
        }

        public void RunFinished(int finalYear)
        {
        }

        public bool RuntimeValidation(ref string error)
        {
            if (Households == null)
            {
                error = Name + ": missing households repository.";
                return false;
            }
            return true;
        }

        // Summing up the houshold income
        private float GetHouseholdIncome(Household household)
        {

            float income = 0f;
            foreach (var family in household.Families)
            {
                foreach (var person in family.Persons)
                {
                    foreach (var job in person.Jobs)
                    {
                        var salary = job.Salary.Amount;
                        if (_currencyManager != null)
                        {
                            salary = _currencyManager.ConvertToDate(job.Salary, _currentDate).Amount;
                        }
                        income += salary;
                    }
                }
            }

            // floor to prevent zero-income edge cases
            if (income < 10000f)
            {
                income = 10000f;
            }
            return income;


        }

    }
}
