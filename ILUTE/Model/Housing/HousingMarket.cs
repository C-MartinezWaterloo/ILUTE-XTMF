﻿/*
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
using System.Threading;
using System.Threading.Tasks;
using TMG.Emme.XTMF_Internal;
using TMG.Ilute.Data;
using TMG.Ilute.Data.Demographics;
using TMG.Ilute.Data.Housing;
using TMG.Ilute.Data.Spatial;
using TMG.Ilute.Model.Utilities;
using XTMF;

namespace TMG.Ilute.Model.Housing
{
    public sealed class HousingMarket : MarketModel<Household, Dwelling>, IExecuteMonthly, ICSVYearlySummary, IDisposable
    {
        [RunParameter("Random Seed", 12345, "The random seed to use for this model.")]
        public int RandomSeed;

        [SubModelInformation(Required = true, Description = "The model to select the price a household would spend.")]
        public ISelectPriceMonthly<Household, Dwelling> BidModel;

        [SubModelInformation(Required = true, Description = "The model to predict the asking price for a sale.")]
        public ISelectSaleValue<Dwelling> AskingPrices;

        // Optional module that adds new dwellings to the repository each year
        [SubModelInformation(Required = false, Description = "Generates new dwellings each year before the market runs.")]
        public HousingSupply SupplyModule;

        [SubModelInformation(Required = true, Description = "A source of dwellings in the model.")]
        public IDataSource<Repository<Dwelling>> DwellingRepository;

        [SubModelInformation(Required = true, Description = "A link to the effect of currency over time.")]
        public IDataSource<CurrencyManager> CurrencyManager;
        private CurrencyManager _currencyManager;

        private Repository<LandUse> _landUse;
        private Repository<FloatData> _distanceToSubway;
        private Repository<FloatData> _distanceToRegionalTransit;
        private Repository<SaleRecord> _saleRecords;

        private ZoneSystem _zoneSystem;


        [SubModelInformation(Required = true, Description = "The source of people in the model.")]
        public IDataSource<Repository<Person>> PersonRepository;

        [SubModelInformation(Required = true, Description = "The log to save the write to.")]
        public IDataSource<ExecutionLog> LogSource;

        [SubModelInformation(Required = false, Description = "Records past dwelling sales.")]
        public IDataSource<Repository<SaleRecord>> SaleRecordRepository;

        [SubModelInformation(Required = false, Description = "Land-Use data for the housing zone system.")]
        public IDataSource<Repository<LandUse>> LandUse;

        [SubModelInformation(Required = false, Description = "The average distance to the subway by zone.")]
        public IDataSource<Repository<FloatData>> DistanceToSubwayByZone;

        [SubModelInformation(Required = false, Description = "The average distance to Regional Transit by zone.")]
        public IDataSource<Repository<FloatData>> DistanceToRegionalTransit;

        [SubModelInformation(Required = true, Description = "The zone system the dwellings reference.")]
        public IDataSource<ZoneSystem> ZoneSystem;

        #region Parameters
        private const float RES_MOBILITY_SCALER = 0.5F;
        // From MA Habib pg 46:
        private const float RES_MOBILITY_CONSTANT = -0.084F;
        private const float INC_NUM_JOBS = -0.198F;
        private const float INC_NUM_JOBS_ST_DEV = 1.254F;
        private const float DEC_NUM_JOBS = 0.474F;
        private const float RETIREMENT_IN_HHLD = 0.448F;
        private const float DUR_IN_DWELL_ST_DEV = 0.045F;
        private const float DUR_IN_DWELL = -0.054F;
        private const float JOB_CHANGE = 0.296F;
        private const float JOB_CHANGE_ST_DEV = 0.762F;
        private const float CHILD_BIRTH = 0.326F;
        private const float CHILD_BIRTH_ST_DEV = 0.219F;
        private const float DEC_HHLD_SIZE = 0.133F;
        private const float HHLD_HEAD_AGE = -0.029F;
        private const float HHLD_HEAD_AGE_ST_DEV = 0.002F;
        private const float NUM_JOBS = -0.086F;
        private const float NON_MOVER_RATIO = -0.110F;
        private const float LABOUR_FORCE_PARTN = 0.004F;
        private const float CHANGE_IN_BIR = -0.013F;
        private const float CHANGE_IN_BIR_ST_DEV = 0.035F;
        #endregion
        

     
        public int InitialAverageSellingPriceDetached;
        public int InitialAverageSellingPriceSemi;
        public int InitialAverageSellingPriceApartmentHigh;
        public int InitialAverageSellingPriceApartmentLow;
        public int InitialAverageSellingPriceAtt;

        private int _averageSellingPriceDetached;
        private int _averageSellingPriceSemi;
        private int _averageSellingPriceApartmentHigh;
        private int _averageSellingPriceApartmentLow;
        private int _averageSellingPriceAtt;

        private long _boughtDwellings;
        private double _totalSalePrice;
        private Date _currentTime;

        private ConcurrentDictionary<long, Household> _remainingHouseholds = new ConcurrentDictionary<long, Household>();
        private ConcurrentDictionary<long, Dwelling> _remainingDwellings = new ConcurrentDictionary<long, Dwelling>();


        // Tracks the number of months that each buyer and seller has been active. The idea is that the buyers and sellers should be removed after 3 unsuccessful attempts.

        private Dictionary<long, int> _buyerDurations;
        private Dictionary<long, int> _sellerDurations;


        // Exports columns for writing to CSV

        public List<string> Headers => new List<string>() { "DwellingsSold", "HouseholdsRemaining", "DwellingsRemaining", "AverageSalePrice" };


        public List<float> YearlyResults
        {
            get
            {
                var average = _boughtDwellings > 0 ?
                    (float)(_totalSalePrice / _boughtDwellings) : 0f;
                return new List<float>()
                {
                    _boughtDwellings,
                    _remainingHouseholds.Count,
                    _remainingDwellings.Count,
                    average
                };
            }
        }


        public void AfterMonthlyExecute(int currentYear, int month)
        {
            BidModel.AfterMonthlyExecute(currentYear, month); // Nothing
            AskingPrices.AfterMonthlyExecute(currentYear, month); // Nothing
            PrepareCarryover();
        }

        public void AfterYearlyExecute(int currentYear)
        {
            if (!CurrencyManager.Loaded)
            {
                CurrencyManager.LoadData();
            }
            _currencyManager = CurrencyManager.GiveData();

            BidModel.AfterYearlyExecute(currentYear);
            AskingPrices.AfterYearlyExecute(currentYear);
            SupplyModule?.AfterYearlyExecute(currentYear);

            // compute average sale price
            var average = _boughtDwellings > 0
                ? _totalSalePrice / _boughtDwellings
                : 0f;

            // grab your repositories
            var dwellings = Repository.GetRepository(DwellingRepository);
            var persons = Repository.GetRepository(PersonRepository);
            var log = Repository.GetRepository(LogSource);

            // log counts
            log.WriteToLog($"There are {dwellings.Count} dwellings and {persons.Count} persons.");

            // log sales summary
            log.WriteToLog(
                $"Year {currentYear} sold {_boughtDwellings} homes " +
                $"for a total of {_totalSalePrice}, average {average:F2}."
            );

            // compute and log the average personal income for this year
            float totalIncome = 0f;
            int adultCount = 0;
            foreach (var person in persons)
            {
                if (person.Age > 18)
                {
                    adultCount++;
                    float personIncome = 0f;
                    foreach (var job in person.Jobs)
                    {
                        var salary = job.Salary.Amount;
                        if (_currencyManager != null)
                        {

                            if (salary == 0f && job.Salary.Amount > 0f)
                            {
                                throw new XTMFRuntimeException(this, "currency manager is not working");
                            }
                        }
                        personIncome += salary;
                    }
                    personIncome += personIncome;
                }
                
            }
            var averageIncome = adultCount > 0 ? totalIncome / adultCount : 0f;
            log.WriteToLog($"Average personal income in {currentYear}: {averageIncome:F2}");


        }


        public void BeforeFirstYear(int firstYear)
        {
            BidModel.BeforeFirstYear(firstYear);
            AskingPrices.BeforeFirstYear(firstYear);
            SupplyModule?.BeforeFirstYear(firstYear);

            if (SaleRecordRepository != null && !SaleRecordRepository.Loaded)
            {
                SaleRecordRepository.LoadData();
            }

            if (LandUse != null && !LandUse.Loaded)
            {
                LandUse.LoadData();
            }

            if (DistanceToSubwayByZone != null && !DistanceToSubwayByZone.Loaded)
            {
                DistanceToSubwayByZone.LoadData();
            }

            if (DistanceToRegionalTransit != null && !DistanceToRegionalTransit.Loaded)
            {
                DistanceToRegionalTransit.LoadData();
            }

            if (ZoneSystem != null && !ZoneSystem.Loaded)
            {
                ZoneSystem.LoadData();
            }

            _landUse = Repository.GetRepository(LandUse);
            _distanceToSubway = Repository.GetRepository(DistanceToSubwayByZone);
            _distanceToRegionalTransit = Repository.GetRepository(DistanceToRegionalTransit);

            _zoneSystem = Repository.GetRepository(ZoneSystem);


            if (SaleRecordRepository != null)
            {
                var repo = Repository.GetRepository(SaleRecordRepository);
                if (repo != null)
                {
                    _saleRecords = repo;
                }
            }
            if (_saleRecords == null)
            {
                // Fall back to the shared empty repository so that other
                // modules (e.g., AskingPrice) see the same data source.
                _saleRecords = Repository.GetRepository<Repository<SaleRecord>>(null);
            }


            // reset tracking information for buyers and sellers
            _buyerDurations = new Dictionary<long, int>();
            _sellerDurations = new Dictionary<long, int>();

        }

        public void BeforeMonthlyExecute(int currentYear, int month)
        {
            _currentYear = currentYear;
            _currentMonth = month;
            _monthlyBuyerCurrentDwellings = new List<Dwelling>();
            BidModel.BeforeMonthlyExecute(currentYear, month);
            AskingPrices.BeforeMonthlyExecute(currentYear, month);
        }

        public void BeforeYearlyExecute(int currentYear)
        {
            BidModel.BeforeYearlyExecute(currentYear);
            AskingPrices.BeforeYearlyExecute(currentYear);

            if (SaleRecordRepository != null && !SaleRecordRepository.Loaded)
            {
                SaleRecordRepository.LoadData();
            }
            if (LandUse != null && !LandUse.Loaded)
            {
                LandUse.LoadData();
            }
            if (DistanceToSubwayByZone != null && !DistanceToSubwayByZone.Loaded)
            {
                DistanceToSubwayByZone.LoadData();
            }
            if (DistanceToRegionalTransit != null && !DistanceToRegionalTransit.Loaded)
            {
                DistanceToRegionalTransit.LoadData();
            }

            if (ZoneSystem != null && !ZoneSystem.Loaded)
            {
                ZoneSystem.LoadData();
            }

            _landUse = Repository.GetRepository(LandUse);
            _distanceToSubway = Repository.GetRepository(DistanceToSubwayByZone);
            _distanceToRegionalTransit = Repository.GetRepository(DistanceToRegionalTransit);
            _zoneSystem = Repository.GetRepository(ZoneSystem);

            if (SaleRecordRepository != null)
            {
                var repo = Repository.GetRepository(SaleRecordRepository);
                if (repo != null)
                {
                    _saleRecords = repo;
                }
            }
            if (_saleRecords == null)
            {
                // Fall back to the shared empty repository so that all
                // modules reference the same set of sale records.
                _saleRecords = Repository.GetRepository<Repository<SaleRecord>>(null);
            }


            var dwellings = Repository.GetRepository(DwellingRepository);
            var persons = Repository.GetRepository(PersonRepository);

            if (persons.Count == 0)
            {
                throw new XTMFRuntimeException(this, "Person repository is empty.");
            }

            if (dwellings.Count == 0)
            {
                throw new XTMFRuntimeException(this, "Dwelling repository is empty.");
            }

            // Execute optional supply generator for the year
            SupplyModule?.BeforeYearlyExecute(currentYear);
            SupplyModule?.Execute(currentYear);


            // cleanup the accumulators for statistics
            _boughtDwellings = 0;
            _totalSalePrice = 0;
        }

        public void Execute(int currentYear, int month)
        {
            _currentTime = new Date(currentYear, month);
            // create the random seed for this execution of the housing market and start
            var r = new Rand((uint)(currentYear * RandomSeed + month));
            var log = Repository.GetRepository(LogSource);
            log?.WriteToLog($"Housing market executing {currentYear}-{month + 1}.");
            var previousSales = _boughtDwellings;
            AskingPrices.Execute(currentYear, month);
            BidModel.Execute(currentYear, month);
            Execute(r, currentYear, month);
            var monthlySales = _boughtDwellings - previousSales;
            log?.WriteToLog($"Housing market {currentYear}-{month + 1} completed with {monthlySales} sales.");

        }

        public void RunFinished(int finalYear)
        {
            BidModel.RunFinished(finalYear);
            AskingPrices.RunFinished(finalYear);
            SupplyModule?.RunFinished(finalYear);
        }

        [RunParameter("Max Bedrooms", 7, "The maximum number of bedrooms to consider.")]
        public int MaxBedrooms;

        private const int DwellingCategories = 5;
        private const int Detached = 0;
        private const int Attached = 1;
        private const int SemiDetached = 2;
        private const int ApartmentLow = 3;
        private const int ApartmentHigh = 4;

        // Used to pause the seller-selection logic until buyer-selection is fully completed
        // Acts like a signal to other threads: "Buyers are ready, you can continue".

        private SemaphoreSlim _buyersReady = new SemaphoreSlim(0);

        // A list of dwellings that will be listed for sale this month.

        private List<Dwelling> _monthlyBuyerCurrentDwellings;

        //  Thread-safe collection of households that want a larger home

        private HashSet<long> _demandLargerDwelling;


        // Returns a list of households who are active buyers this month

        protected override List<Household> GetBuyers(Rand rand)
        {
            _demandLargerDwelling = new HashSet<long>();
            try
            {
                var buyers = new HashSet<Household>();
                foreach (var dwelling in Repository.GetRepository(DwellingRepository))
                {
                    var hhld = dwelling.Household;

                    // check if household is valid and an owner
                    if (hhld != null && hhld.Tenure == DwellingUnitTenure.own)
                    {
                        // if this dwelling is not the active dwelling for the household
                        if (hhld.Dwelling != dwelling)
                        {
                            _monthlyBuyerCurrentDwellings.Add(dwelling);
                        }
                        else if (OptIntoMarket(rand, hhld))
                        {
                            _monthlyBuyerCurrentDwellings.Add(dwelling);
                            buyers.Add(hhld);
                            
                        }
                    }
                }
                return buyers.ToList();
            }
            finally
            {
                _buyersReady.Release();
            }
        }

        private double _changeInBIR;

        private int _currentYear, _currentMonth;

        // This function will calculate the labour Force participation rate (LFPR). This measures the active portion of a economy's working age
        private float LabForcePartRateCalculation(IEnumerable<Person> personRepository)
        {
            /*
            int workingAgePop = 0;
            int labourForce = 0;

            foreach (var person in personRepository)
            {
                if (person.Age >= 15)
                {
                    workingAgePop++;
                    // Check if the person is in the labour force (has at least one job)
                    if (person.Jobs != null && person.Jobs.Count > 0)
                    {
                        labourForce++;
                    }
                }
            }

            if (workingAgePop == 0)
            {
                return 0f; // Avoid division by zero
            }

            return (float)labourForce / workingAgePop;
            */

            // Temporary fixed value for labour force participation rate
            return 0.658f;
        }



        private bool OptIntoMarket(Rand rand, Household hhld)
        {
            float labourForcePartRate = 0;

            var people = Repository.GetRepository(PersonRepository);

            labourForcePartRate = LabForcePartRateCalculation(people);

            const float nonMoverRatio = 0.95f;
            var dwelling = hhld.Dwelling;
            // 1% chance of increasing the # of employed people in the household
            bool jobIncrease = false;
            if (rand.NextFloat() <= 0.01) { jobIncrease = true; }

            // 1% chance of decreasing the # of employed people in the household
            bool jobDecrease = false;
            if (rand.NextFloat() <= 0.01) { jobDecrease = true; }

            // 1% chance of a household member retiring
            bool retirement = false;
            if (rand.NextFloat() <= 0.01) { retirement = true; }

            bool jobChange = false;
            if (rand.NextFloat() <= 0.01) { jobChange = true; }

            var newChild = hhld.Families.Any(f => f.Persons.Any(p => p.Age <= 0));
            var lastTransactionDate = hhld.Dwelling.Value.WhenCreated;
            double yearsInDwelling = ((_currentYear * 12 + _currentMonth) - lastTransactionDate.Months) / 12;

            // Determine the age of the household head. Datasets may contain
            // families with no persons which would cause Max() to throw if not
            // guarded. Default to zero when no ages are available.
            var headAge = hhld.Families
                .Where(f => f.Persons.Any())
                .Select(f => f.Persons.Max(p => p.Age))
                .DefaultIfEmpty(0)
                .Max();


            var numbOfJobs = hhld.Families.Sum(f => f.Persons.Count(p => p.Jobs.Any()));

            int demandCounter = 0; // Determines whether a household likely needs a larger dwelling
            double probMoving = RES_MOBILITY_CONSTANT;  // base parameter (M.A. Habib, 2009. pg. 46)

            if (jobIncrease)
            {
                // increasing the demand counter since there is a higher probability of moving into a larger house
                demandCounter++;
                probMoving += rand.InvStdNormalCDF() + INC_NUM_JOBS_ST_DEV + INC_NUM_JOBS;
            }
            if (jobDecrease)
            {
                demandCounter--;
                probMoving += DEC_NUM_JOBS;
            }
            if (retirement)
            {
                probMoving += RETIREMENT_IN_HHLD;
            }
            if (jobChange)
            {
                probMoving += rand.InvStdNormalCDF() * JOB_CHANGE_ST_DEV + JOB_CHANGE;
            }
            if (newChild)
            {
                demandCounter++;
                probMoving += rand.InvStdNormalCDF() * CHILD_BIRTH_ST_DEV + CHILD_BIRTH;
            }

            if (demandCounter > 0) _demandLargerDwelling.Add(hhld.Id);

            probMoving += headAge * (rand.InvStdNormalCDF() * HHLD_HEAD_AGE_ST_DEV + HHLD_HEAD_AGE)
                          + _changeInBIR * (rand.InvStdNormalCDF() * CHANGE_IN_BIR_ST_DEV + CHANGE_IN_BIR)
                          + yearsInDwelling * (rand.InvStdNormalCDF() * DUR_IN_DWELL_ST_DEV + DUR_IN_DWELL)
                          + numbOfJobs * NUM_JOBS
                          + nonMoverRatio * NON_MOVER_RATIO
                          + labourForcePartRate * LABOUR_FORCE_PARTN
                          ;

            probMoving = Math.Exp(probMoving) / (1 + Math.Exp(probMoving)) * RES_MOBILITY_SCALER;
            return probMoving >= rand.NextDouble();
        }

        protected override List<List<SellerValue>> GetSellers(Rand rand)
        {
            // Wait for all of the buyers to be processed.
            _buyersReady.Wait();
            Interlocked.MemoryBarrier();

            // For each combination of dwelling type and bedroom count, an empty list is created
            int length = DwellingCategories * MaxBedrooms;

            // is the main result: a list of lists, where each inner list holds sellers in a specific category (e.g., 2-bedroom rental apartments).
            var ret = new List<List<SellerValue>>(length);
            for (int i = 0; i < length; i++)
            {
                ret.Add(new List<SellerValue>());
            }
            // This list contains homes owned by houshold who intend to sell.
            var dwellings = Repository.GetRepository(DwellingRepository);
            // Candidates are dwellings that exist and currently have no houshold, this is added to _monthlyBuyerCurrentDwellings from before
            var candidates = dwellings.Where(d => d.Exists && (d.Household == null)).Union(_monthlyBuyerCurrentDwellings);
            // sort the candidates into the proper lists
            foreach (var d in candidates)
            {
                // This sets the listing date when added to market
                if (d.ListingDate == null)
                {
                    d.ListingDate = _currentTime;
                }
                
                (var asking, var min) = AskingPrices.GetPrice(d);
                ret[ComputeHouseholdCategory(d)].Add(new SellerValue(d, asking, min));
            }
            return ret;
        }

        private int ComputeHouseholdCategory(Dwelling d)
        {
            return ComputeHouseholdCategory(d.Type, d.Rooms);
        }

        private const int MAX_HOUSEHOLD_DURATION = 3; // in months
        private const int MAX_DWELLING_DURATION = 3;  // in months

        private List<Household> _buyersToCarry = new();
        private List<Dwelling> _sellersToCarry = new();




        private void PrepareCarryover()
        {
            // In a full implementation, you'd reinsert these into next month's market entry queue
            foreach (var buyer in _buyersToCarry)
            {
                // Add to MonthlyActiveHhldIDs[month + 1] or equivalent (not shown here)
            }
            foreach (var seller in _sellersToCarry)
            {
                // Add to MonthlyActiveDwellingIDs[month + 1] or equivalent (not shown here)
            }

            _buyersToCarry.Clear();
            _sellersToCarry.Clear();
        }

        private void RegisterActiveAgents(List<Household> buyers, List<Dwelling> sellers)
        {
            foreach (var b in buyers) _buyerDurations[b.Id] = 0;
            foreach (var s in sellers) _sellerDurations[s.Id] = 0;
        }



        private int ComputeHouseholdCategory(Dwelling.DwellingType dwellingType, int rooms)
        {
            var offset = Detached;
            switch (dwellingType)
            {
                case Dwelling.DwellingType.Detached:
                    break;
                case Dwelling.DwellingType.SemiDetached:
                    offset = SemiDetached;
                    break;
                case Dwelling.DwellingType.Attached:
                    offset = Attached;
                    break;
                case Dwelling.DwellingType.ApartmentLow:
                    offset = ApartmentLow;
                    break;
                case Dwelling.DwellingType.ApartmentHigh:
                    offset = ApartmentHigh;
                    break;
            }
            return MaxBedrooms * offset + Math.Max(Math.Min(MaxBedrooms - 1, rooms), 0);
        }
        
        protected override void ResolveSale(Household buyer, Dwelling seller, float transactionPrice)
        {
            // if this house is the current dwelling of the household that owns it, set that household to not have a dwelling
            if (seller.Household != null)
            {
                var sellerDwelling = seller.Household.Dwelling;
                if (sellerDwelling == seller)
                {
                    seller.Household.Dwelling = null;
                }
            }
            // Link the buying household with their new dwelling
            seller.Household = buyer;
            buyer.Dwelling = seller;
            seller.Value = new Money(transactionPrice, _currentTime);
            // Clearing the listing date when sold
            seller.ListingDate = null;
            _boughtDwellings++;
            _totalSalePrice += transactionPrice;



            if (_saleRecords != null)
            {
                int zoneNumber = _zoneSystem.ZoneNumber[seller.Zone];
                float distSubway = 0f;
                if (_distanceToSubway != null && _distanceToSubway.TryGet(zoneNumber, out var sub))

                {
                    distSubway = sub.Data;
                }

                float distRegional = 0f;
                if (_distanceToRegionalTransit != null && _distanceToRegionalTransit.TryGet(zoneNumber, out var rt))
                {
                    distRegional = rt.Data;
                }

                float res = 0f, com = 0f;
                if (_landUse != null && _landUse.TryGet(zoneNumber, out var lu))
                {
                    res = lu.Residential;
                    com = lu.Commerce;
                }

                var rec = new SaleRecord
                {
                    Date = _currentTime,
                    Price = transactionPrice,
                    Rooms = seller.Rooms,
                    SquareFootage = seller.SquareFootage,
                    Zone = zoneNumber,
                    DistSubway = distSubway,
                    DistRegional = distRegional,
                    Residential = res,
                    Commerce = com,
                    Type = seller.Type
                };
                _saleRecords.AddNew(rec);
            }

            Repository.GetRepository(LogSource)
                .WriteToLog($"Sold dwelling {seller.Id} for {transactionPrice} in year {_currentTime.Year}.");
        }

        [RunParameter("Choice Set Size", 10, "The size of the choice set for the buyer for each dwelling class.")]
        public int ChoiceSetSize;


        // Construct a personalized list of bids that a buyer places on suitable dwellings
        // Outer list: One entry per dwelling category
        // Inner lists: bids placed by this buyer in that category

        protected override List<List<Bid>> SelectSellers(Rand rand, Household buyer, IReadOnlyList<IReadOnlyList<SellerValue>> sellers)
        {
            // empty list to hold the buyer's bid
            // Returns a List<Bid> for each dwelling category
            var ret = InitializeBidSet(sellers);

            // Get what room sizes the buyer wants
            (var minSize, var maxSize) = GetHouseholdBounds(buyer);

            // Go through all the dwelling types and room sizes that buyer is open to

            for (int dwellingType = 0; dwellingType < DwellingCategories; dwellingType++)
            {
                for (int rooms = minSize; rooms <= maxSize; rooms++)
                {

                    // Convert(type,rooms) into a flat index
                    var index = ComputeHouseholdCategory((Dwelling.DwellingType)dwellingType, rooms);
                    // Where this buyer's bid for this category will be stored
                    var retRow = ret[index];
                    // where this buyer's bids for this category will be stored
                    var sellerRow = sellers[index];

                    // If fewer sellers than the choice set size, bid on everyone. ChoiceSetSize = 10. 
                    if (sellerRow.Count < ChoiceSetSize)
                    {
                        // For all sellers in this category, convert each one into a Bid for this buyer, then store all those bids in the return list for this household category.
                        // Since we have fewer sellers than the choice set size, take them all and stop looking at other room sizes.

                        retRow.AddRange(sellerRow.Select((seller, i) => new Bid(BidModel.GetPrice(buyer, seller.Unit, seller.AskingPrice), i)));
                        break;
                    }
                    var attempts = 0;


                       // keep looping until you have enough bids
                       // retRow is for a single category
                    while (retRow.Count < ChoiceSetSize && attempts++ < ChoiceSetSize * 2)
                    {
                        // sellerIndex picks a random house
                        var sellerIndex = (int)(sellerRow.Count * rand.NextFloat());
                        var toCheck = sellerRow[sellerIndex];
                        var price = BidModel.GetPrice(buyer, toCheck.Unit, toCheck.AskingPrice);
                        if (sellerIndex >= sellerRow.Count || sellerIndex < 0)
                        {
                            throw new XTMFRuntimeException(this, "We found an out of bounds issue when selecting sellers.");
                        }
                        if (price >= toCheck.MinimumPrice)
                        {
                            retRow.Add(new Bid(price, sellerIndex));
                        }
                    }
                }
            }
            return ret;
        }

        private (int minSize, int maxSize) GetHouseholdBounds(Household buyer)
        {
            int persons = buyer.ContainedPersons;
            var isDemandingLarger = _demandLargerDwelling.Contains(buyer.Id);
            // The compute function will take care of the remainders
            return isDemandingLarger ? (persons, persons + 1)
                                     : (persons - 1, persons);
        }

        // It creates a new outer list of empty inner lists, one for each seller category.

        private static List<List<Bid>> InitializeBidSet(IReadOnlyList<IReadOnlyList<SellerValue>> sellers)
        {
            return sellers.Select(s => new List<Bid>()).ToList();
        }

        #region IDisposable Support
        private bool _disposedValue = false; // To detect redundant calls

        void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _buyersReady?.Dispose();
                    _buyersReady = null;
                }
                _disposedValue = true;
            }
        }

        ~HousingMarket()
        {
            Dispose(false);
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public override bool RuntimeValidation(ref string error)
        {
            if (DwellingRepository == null)
            {
                error = Name + ": missing dwelling repository.";
                return false;
            }

            if (ZoneSystem == null)
            {
                error = Name + ": missing zone system.";
                return false;
            }

            return base.RuntimeValidation(ref error);
        }
        #endregion
    }
}
