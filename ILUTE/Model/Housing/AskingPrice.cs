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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMG.Ilute;
using TMG.Ilute.Data;
using TMG.Ilute.Data.Housing;
using TMG.Ilute.Data.Spatial;
using TMG.Ilute.Model.Utilities;
using XTMF;

namespace TMG.Ilute.Model.Housing
{
    public sealed class AskingPrice : ISelectSaleValue<Dwelling>

        // Injecting the data from other modulus
    {
        [RunParameter("Monthly Time Decay", 0.95, "The decay for the asking price as the house remains on the market.")]
        public double ASKING_PRICE_FACTOR_DECREASE;

        [SubModelInformation(Required = true, Description = "Used to convert monetary values between years.")]
        public IDataSource<CurrencyManager> CurrencyManager;

        [SubModelInformation(Required = true, Description = "LandUse data for the housing zone system.")]
        public IDataSource<Repository<LandUse>> LandUse;

        [SubModelInformation(Required = true, Description = "The average distance to the subway by zone.")]
        public IDataSource<Repository<FloatData>> DistanceToSubwayByZone;

        [SubModelInformation(Required = true, Description = "The average distance to Regional Transit by Zone.")]
        public IDataSource<Repository<FloatData>> DistanceToRegionalTransit;

        [SubModelInformation(Required = true, Description = "The unemployment rate by zone.")]
        public IDataSource<Repository<FloatData>> UnemploymentByZone;

        [SubModelInformation(Required = true, Description = "The repository of all dwellings.")]
        public IDataSource<Repository<Dwelling>> Dwellings;

        [SubModelInformation(Required = false, Description = "Optional log output for asking prices.")]
        public IDataSource<ExecutionLog> LogSource;

        [SubModelInformation(Required = false, Description = "Sale records for hedonic regression.")]
        public IDataSource<Repository<SaleRecord>> SaleRecordRepository;


        private Repository<LandUse> _landUse;
        private Repository<FloatData> _distanceToSubway;
        private Repository<FloatData> _distanceToRegionalTransit;
        private Repository<FloatData> _unemployment;
        private Repository<SaleRecord> _saleRecords;
        private Dictionary<int, float> _averageDwellingValueByZone;
        private CurrencyManager _currencyManager;

        // Initial coefficients for the linear hedonic pricing model. The first
        // value is the intercept while the rest correspond to the explanatory
        // variables used when computing asking prices. These are simple default
        // values and will be overwritten once sale records accumulate and a
        // regression is run.
        private double[] _beta = new double[]
        {
            300000.0,   // intercept
            10000.0,    // rooms
            -1000.0,    // distance to subway
            -1000.0,    // distance to regional transit
            500.0,      // residential land use share
            -500.0      // commercial land use share
        };


        private Date _currentDate;

        public string Name { get; set; }

        public float Progress => 0f;

        public Tuple<byte, byte, byte> ProgressColour => new Tuple<byte, byte, byte>(50, 150, 50);

        public void AfterMonthlyExecute(int currentYear, int month)
        {
            _averageDwellingValueByZone = null;
            _averageDwellingValueByZone = new Dictionary<int, float>();
        }

        public void AfterYearlyExecute(int currentYear)
        {
        }

        public void BeforeFirstYear(int firstYear)
        {
        }

        public void BeforeMonthlyExecute(int currentYear, int month)
        {
        }

        public void BeforeYearlyExecute(int currentYear) { 


            _averageDwellingValueByZone = new Dictionary<int, float>();
            _currencyManager = Repository.GetRepository(CurrencyManager);
            _saleRecords = Repository.GetRepository(SaleRecordRepository);
            if (_saleRecords == null)
            {
                _saleRecords = new Repository<SaleRecord>();
                _saleRecords.LoadData();

            }

        }

        public void Execute(int currentYear, int month)
        {
          

            _currentDate = new Date(currentYear, month);
            // Loading the required repositories
            _landUse = Repository.GetRepository(LandUse);
            _distanceToSubway = Repository.GetRepository(DistanceToSubwayByZone);
            _unemployment = Repository.GetRepository(UnemploymentByZone);
            _distanceToRegionalTransit = Repository.GetRepository(DistanceToRegionalTransit);
            _saleRecords = Repository.GetRepository(SaleRecordRepository);

            if(_saleRecords == null)
            {
                _saleRecords= new Repository<SaleRecord>();
                _saleRecords.LoadData();

            }

            if (_averageDwellingValueByZone == null)
            {
                // In case the per-month reset occurred without a yearly
                // initialization, ensure the dictionary exists before
                // computing averages.
                _averageDwellingValueByZone = new Dictionary<int, float>();
            }
            AverageDwellingValueByZone(new Date(currentYear, month));
            UpdateRegressionCoefficients(new Date(currentYear, month));
        }
        


        /// <summary>
        /// The code loops over all dwellings and groups them by zone. Computes the average dwelling value per zone
        /// </summary>


        private void AverageDwellingValueByZone(Date now)
        {
            var valueSumByZone = new Dictionary<int, float>();
            var recordsByZone = new Dictionary<int, int>();

            foreach (var dwelling in Repository.GetRepository(Dwellings))
            {
                var zone = dwelling.Zone;
                float adjustedValue = _currencyManager.ConvertToYear(dwelling.Value, now).Amount;

                if (!valueSumByZone.ContainsKey(zone))
                {
                    valueSumByZone[zone] = 0f;
                    recordsByZone[zone] = 0;
                }

                valueSumByZone[zone] += adjustedValue;
                recordsByZone[zone] += 1;
            }

            foreach (var zone in valueSumByZone.Keys)
            {
                float avg = valueSumByZone[zone] / recordsByZone[zone];
                _averageDwellingValueByZone[zone] = avg;
            }
        }

        private ExecutionLog? GetLog()
        {
            if (LogSource != null)
            {
                return Repository.GetRepository(LogSource);
            }
            return null;
        }


        private void UpdateRegressionCoefficients(Date now)
        {

            if (_saleRecords == null)
            {
                var log = GetLog();
                log?.WriteToLog("Sale record repository missing; skipping regression update.");
                return;

            }
 
            int end = now.Months;
            int start = end - 3;
            var records = _saleRecords.Where(r => r.Date.Months >= start && r.Date.Months < end).ToList();
            if (records.Count == 0)
            {
                if ((now.Month + 1) % 3 == 0)
                {
                    var log = GetLog();
                    if (log != null)
                    {
                        int quarter = now.Month / 3 + 1;
                        log.WriteToLog($"No sale records available for regression in {now.Year} Q{quarter}.");
                    }
                }
                return;
            }

            int p = 6;
            double[,] xtx = new double[p, p];
            double[] xty = new double[p];

            foreach (var rec in records)
            {
                // Vector of explanatory variables for the linear model.
                double[] x =
                    { 1.0, rec.Rooms, rec.DistSubway, rec.DistRegional, rec.Residential, rec.Commerce };
                double y = rec.Price;
                for (int i = 0; i < p; i++)
                {
                    xty[i] += x[i] * y;
                    for (int j = 0; j < p; j++)
                    {
                        xtx[i, j] += x[i] * x[j];
                    }
                }
            }

            _beta = Solve(xtx, xty);

            // Log updated coefficients only at the end of each quarter.
            if ((now.Month + 1) % 3 == 0)
            {
                
                    var log = GetLog();
                    if (log != null)
                    {
                        int quarter = now.Month / 3 + 1;
                        string coeffs = string.Join(", ", _beta.Select(v => v.ToString("F4")));
                        log.WriteToLog($"Regression coefficients for {now.Year} Q{quarter}: {coeffs}");
                    }

                }
            }

        private double[] Solve(double[,] a, double[] b)
        {
            int n = b.Length;
            var x = new double[n];
            var A = new double[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    A[i, j] = a[i, j];
            var B = new double[n];
            for (int i = 0; i < n; i++) B[i] = b[i];

            for (int i = 0; i < n; i++)
            {
                int max = i;
                for (int k = i + 1; k < n; k++)
                {
                    if (Math.Abs(A[k, i]) > Math.Abs(A[max, i])) max = k;
                }
                for (int j = i; j < n; j++) (A[i, j], A[max, j]) = (A[max, j], A[i, j]);
                (B[i], B[max]) = (B[max], B[i]);

                double pivot = A[i, i];
                if (Math.Abs(pivot) < 1e-12) return _beta;
                for (int j = i; j < n; j++) A[i, j] /= pivot;
                B[i] /= pivot;

                for (int k = 0; k < n; k++)
                {
                    if (k == i) continue;
                    double factor = A[k, i];
                    for (int j = i; j < n; j++) A[k, j] -= factor * A[i, j];
                    B[k] -= factor * B[i];
                }
            }

            for (int i = 0; i < n; i++) x[i] = B[i];
            return x;
        }




        /// <summary>
        /// Calculates the current asking price and minimum acceptable price for a dwelling,
        /// applying a monthly decay factor based on how long the dwelling has been listed.
        /// </summary>

        public (float askingPrice, float minimumPrice) GetPrice(Dwelling seller)
        {
            int monthsOnMarket = 0;

            if (seller.ListingDate.HasValue)
            {
                monthsOnMarket = (_currentDate.Year - seller.ListingDate.Value.Year) * 12
                               + (_currentDate.Month - seller.ListingDate.Value.Month);
                monthsOnMarket = Math.Max(0, monthsOnMarket);
            }

            (var askingPrice, var minPrice) = DwellingPrice(seller);
            float decayedPrice = askingPrice * (float)Math.Pow(ASKING_PRICE_FACTOR_DECREASE, monthsOnMarket);


            return (decayedPrice, minPrice);
        }

        // Asking price is the inital price that the seller will publically list. The minimum bid is lowest price the seller is willing to accept

        private (float askingPrice, float minimumBid) DwellingPrice(Dwelling seller)
        {
            var ctZone = seller.Zone;
            if (ctZone <= 0)
            {
                ctZone = 0;
                // throw new XTMFRuntimeException(this, "Found a dwelling that is not linked to a zone!");
                
            }

            float avgDistToRegionalTransitKM = 0f;

            if (_distanceToRegionalTransit.TryGet(ctZone, out var RTData) && RTData != null)
            {
                avgDistToRegionalTransitKM = RTData.Data;
            }

            float avgDistToSubwayKM = 0f;

            if (_distanceToSubway.TryGet(ctZone, out var subwayData) && subwayData != null)
            {
                avgDistToSubwayKM = subwayData.Data;
            }

            _averageDwellingValueByZone.TryGetValue(ctZone, out var avgPersonsPerRoom);
            var averageSalePriceForThisType = 0.0f;

            switch (seller.Type)
            {
                // townhouse
                case Dwelling.DwellingType.Detached:
                    //myZone.AvgSellPriceDet
                    averageSalePriceForThisType = 300000;
                    break;
                case Dwelling.DwellingType.SemiDetached:
                    averageSalePriceForThisType = 270000;
                    break;
                case Dwelling.DwellingType.ApartmentHigh:
                    averageSalePriceForThisType = 250000;
                    break;
                case Dwelling.DwellingType.ApartmentLow:
                    averageSalePriceForThisType = 220000;
                    break;
                default:
                    averageSalePriceForThisType = 240000;
                    break;
            }
            if (!_landUse.TryGet(ctZone, out var landUse))
            {
                //throw new XTMFRuntimeException(this, $"We were not able to find land use information for the zone {ctZone}");
                landUse = new TMG.Ilute.Data.Spatial.LandUse(ctZone, 0, 0, 0, 0);
            }

            double[] x = new double[]
            {
                1.0,
                seller.Rooms,
                avgDistToSubwayKM,
                avgDistToRegionalTransitKM,
                landUse.Residential,
                landUse.Commerce
            };

            double price = 0.0;
            for (int i = 0; i < _beta.Length && i < x.Length; i++)
            {
                price += _beta[i] * x[i];
            }

            return ((float)price, 0f);
        }

        public void RunFinished(int finalYear)
        {
        }

        public bool RuntimeValidation(ref string error)
        {
            if (Dwellings == null)
            {
                error = Name + ": missing dwellings repository.";
                return false;
            }
            return true;
        }
    }
}
