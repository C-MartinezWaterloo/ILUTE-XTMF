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

        [SubModelInformation(Required = true, Description = "The zone system the dwellings reference.")]
        public IDataSource<ZoneSystem> ZoneSystem;
        private ZoneSystem _zoneSystem;

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
                _zoneSystem = Repository.GetRepository(ZoneSystem);
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
            var households = Repository.GetRepository(Households);

            _unemploymentByZone = new ConcurrentDictionary<int, float>(

            households
                    .Where(h => h.Dwelling?.Zone is int)
                    .GroupBy(h => h.Dwelling.Zone)
                    .Select(g =>
                    {
                        int unemployed = g.SelectMany(h => h.Families)
                                           .SelectMany(f => f.Persons)
                                           .Count(p => p.LabourForceStatus == LabourForceStatus.Unemployed);
                        int totalPersons = g.Sum(h => h.ContainedPersons);
                        return new KeyValuePair<int, float>(g.Key, (float)unemployed / totalPersons);
                    })
            );
        }

        public void Execute(int currentYear, int month)
        {
        }

        public float GetPrice(Household buyer, Dwelling seller, float askingPrice)
        {

            // This calculates the bid amount that a simulated buyer would offer to purchase a dwelling
            float income = GetHouseholdIncome(buyer);
            float savings = GetHouseholdSavings(buyer);
            float purchasingPower = Math.Max(income, savings);

            var buyerDwelling = buyer.Dwelling;

            // Land use effects
            int zoneNumber = _zoneSystem.ZoneNumber[seller.Zone];
            if (!_censusLandUse.TryGet(zoneNumber, out var sellerLU))
            {
                throw new XTMFRuntimeException(
                    this,
                    $"No land-use information found for zone {zoneNumber} when evaluating dwelling {seller.Id}.");
            }

            float openChange = sellerLU.Open > 0 ? (float)Math.Log(sellerLU.Open) : 0f;
            float industrialChange = sellerLU.Industrial > 0 ? (float)Math.Log(sellerLU.Industrial) : 0f;

            // How many more rooms this dwelling offers
            int deltaRooms = buyerDwelling == null ? seller.Rooms : seller.Rooms - buyerDwelling.Rooms;

            // --- Bidding Logic ---

            // Base bid scaled to a multiple of annual income
            // Housing prices tend to be several times the buyer's yearly income,
            // so use a factor of four to better reflect market behaviour.
            float baseBid = 4.0f * purchasingPower;

            // Bonus for more space (positive deltaRooms)
            float spaceValue = deltaRooms * 10000f;

            // Bonus/penalty for local land use
            float openBonus = openChange * 5000f;
            float industrialPenalty = industrialChange * 8000f;

            // Try to bid just under asking (simulates bargaining)
            float proximityDiscount = askingPrice * 0.97f; // start at 97% of asking

            // Final bid: income-based floor vs. environment/location adjusted ceiling
            float bid = Math.Min(proximityDiscount, baseBid + spaceValue + openBonus - industrialPenalty);

            // Do not allow bids below the household's available funds
            bid = Math.Max(bid, purchasingPower);

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

            if (ZoneSystem == null)
            {
                error = Name + ": missing zone system.";
                return false;
            }

            return true;
        }

        // Summing up the houshold income
        private float GetHouseholdIncome(Household household)
        {

            float income = household.Families
                .SelectMany(f => f.Persons)
                .SelectMany(p => p.Jobs)
                .Sum(job =>
                {
                    
                        var salary = job.Salary.Amount;
                        if (_currencyManager != null)
                        {
                            salary = _currencyManager.ConvertToDate(job.Salary, _currentDate).Amount;

                        }
                    return salary;
                });

            // floor to prevent zero-income edge cases
            if (income < 10000f)
            {
                income = 10000f;
            }
            return income;


        }

        // Summing up the household savings (liquid assets)
        private float GetHouseholdSavings(Household household)
        {
            return household.Families
                .Sum(f => f.LiquidAssets);
        }


    }
}
