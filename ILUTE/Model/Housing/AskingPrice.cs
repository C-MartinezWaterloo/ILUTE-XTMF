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
        [RunParameter("Monthly Time Decay", 0.97, "The decay for the asking price as the house remains on the market.")]
        public double ASKING_PRICE_FACTOR_DECREASE;

        [SubModelInformation(Required = true, Description = "Used to convert monetary values between years.")]
        public IDataSource<CurrencyManager>? CurrencyManager;

        [SubModelInformation(Required = true, Description = "LandUse data for the housing zone system.")]
        public IDataSource<Repository<LandUse>>? LandUse;

        [SubModelInformation(Required = true, Description = "The average distance to the subway by zone.")]
        public IDataSource<Repository<FloatData>>? DistanceToSubwayByZone;

        [SubModelInformation(Required = true, Description = "The average distance to Regional Transit by Zone.")]
        public IDataSource<Repository<FloatData>>? DistanceToRegionalTransit;

        [SubModelInformation(Required = true, Description = "The unemployment rate by zone.")]
        public IDataSource<Repository<FloatData>>? UnemploymentByZone;

        [SubModelInformation(Required = true, Description = "The repository of all dwellings.")]
        public IDataSource<Repository<Dwelling>>? Dwellings;

        [SubModelInformation(Required = false, Description = "Optional log output for asking prices.")]
        public IDataSource<ExecutionLog>? LogSource;

        [SubModelInformation(Required = false, Description = "Sale records for hedonic regression.")]
        public IDataSource<Repository<SaleRecord>>? SaleRecordRepository;


        private Repository<LandUse>? _landUse;
        private Repository<FloatData>? _distanceToSubway;
        private Repository<FloatData>? _distanceToRegionalTransit;
        private Repository<FloatData>? _unemployment;
        private Repository<SaleRecord>? _saleRecords;
        private Dictionary<int, float>? _averageDwellingValueByZone;
        private CurrencyManager? _currencyManager;

        // Initial coefficients for the linear hedonic pricing model. The first
        // value is the intercept while the rest correspond to the explanatory
        // variables used when computing asking prices. These are simple default
        // values and will be overwritten once sale records accumulate and a
        // regression is run.
        private static readonly double[] DefaultBeta = new double[]
        {
            100000,   // intercept
            60000,    // rooms
            10000,   // square footage
            -1000,    // distance to subway
            -1000,    // distance to regional transit
            50,      // residential land use share
            -100     // commercial land use share
        };

        private Dictionary<Dwelling.DwellingType, double[]> _betas =
            Enum.GetValues(typeof(Dwelling.DwellingType))
                .Cast<Dwelling.DwellingType>()
                .ToDictionary(t => t, t => (double[])DefaultBeta.Clone());


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
                _saleRecords = new Repository<SaleRecord>();
                _saleRecords.LoadData();
            }


            if (_averageDwellingValueByZone == null)
            {
                // In case the per-month reset occurred without a yearly
                // initialization, ensure the dictionary exists before
                // computing averages.
                _averageDwellingValueByZone = new Dictionary<int, float>();
            }
            AverageDwellingValueByZone(_currentDate);
            UpdateRegressionCoefficients(_currentDate);
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
            var log = GetLog();
            if (_saleRecords == null)
            {
                log?.WriteToLog("Sale record repository missing; skipping regression update.");
                return;
            }

            int end = now.Months;
            int start = end - 3;
            var records = _saleRecords
                .Where(r => r.Date.Months >= start && r.Date.Months < end)
                .ToList();

            log?.WriteToLog($"Number of records: {records.Count}");

            if (!records.Any())
            {
                if ((now.Month + 1) % 3 == 0)
                {
                    int quarter = now.Month / 3 + 1;
                    log?.WriteToLog($"No sale records available for regression in {now.Year} Q{quarter}.");
                }
                return;
            }

            int p = DefaultBeta.Length;

            foreach (var group in records.GroupBy(r => r.Type))
            {
                var xtx = new double[p, p];
                var xty = new double[p];
                foreach (var rec in group)
                {
                    var x = new double[]
                         {
                        1.0, // Intercept term
                        rec.Rooms,
                        rec.SquareFootage,
                        rec.DistSubway,
                        rec.DistRegional,
                        rec.Residential,
                        rec.Commerce
                         };

                    AddScaledVector(xty, x, rec.Price);
                    AddOuterProduct(xtx, x, 1.0);
                }

                _betas[group.Key] = Solve(xtx, xty);

                // Log updated coefficients only at the end of each quarter
                if ((now.Month + 1) % 3 == 0)
                {
                    int quarter = now.Month / 3 + 1;
                    string coeffs = string.Join(", ", _betas[group.Key].Select(v => v.ToString("F4")));
                    log?.WriteToLog($"Regression coefficients for {group.Key} {now.Year} Q{quarter}: {coeffs}");
                }
            }
        }

        private double[] Solve(double[,] xtx, double[] xty)
        {
            int n = xty.Length;
            var A = (double[,])xtx.Clone();   // A ← XᵀX
            var b = (double[])xty.Clone();    // b ← Xᵀy

            // Add a small ridge penalty for stability
            const double lambda = 1e-4;
            for (int i = 0; i < n; i++)
            {
                A[i, i] += lambda;            // A ← A + λI
            }

            // Cholesky decomposition: A = L * L^T

            // Why did I chose Cholesky? splits it into lower triangular for easier computation. Solving Ax = b is equivalent to solving Ly = b then Ltx = y.


            var L = new double[n, n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j <= i; j++)
                {
                    double sum = A[i, j];
                    for (int k = 0; k < j; k++)
                    {
                        sum -= L[i, k] * L[j, k];
                    }

                    if (i == j)
                    {
                        if (sum <= 0.0)
                        {
                            return (double[])DefaultBeta.Clone(); // Matrix not positive definite
                        }
                        L[i, j] = Math.Sqrt(sum);
                    }
                    else
                    {
                        L[i, j] = sum / L[j, j];
                    }
                }
            }

            // Forward substitution: solve L * y = b
            var yVec = new double[n];
            for (int i = 0; i < n; i++)
            {
                double sum = b[i];
                for (int k = 0; k < i; k++)
                {
                    sum -= L[i, k] * yVec[k];
                }
                yVec[i] = sum / L[i, i];
            }

            // Backward substitution: solve L^T * x = y
            var x = new double[n];
            for (int i = n - 1; i >= 0; i--)
            {
                double sum = yVec[i];
                for (int k = i + 1; k < n; k++)
                {
                    sum -= L[k, i] * x[k];
                }
                x[i] = sum / L[i, i];
            }

            return x;
        }

        private static void AddScaledVector(double[] target, double[] vec, double scale)
        {
            int len = Math.Min(target.Length, vec.Length);
            for (int i = 0; i < len; i++)
            {
                target[i] += vec[i] * scale;
            }
        }

        private static void AddOuterProduct(double[,] target, double[] vec, double scale)
        {
            int rows = Math.Min(target.GetLength(0), vec.Length);
            int cols = Math.Min(target.GetLength(1), vec.Length);
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    target[i, j] += scale * vec[i] * vec[j];
                }
            }
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
                seller.SquareFootage,
                avgDistToSubwayKM,
                avgDistToRegionalTransitKM,
                landUse.Residential,
                landUse.Commerce
            };

            double price = 0.0;
            if (!_betas.TryGetValue(seller.Type, out var beta))
            {
                beta = DefaultBeta;
            }

            for (int i = 0; i < beta.Length && i < x.Length; i++)
            {
                price += beta[i] * x[i];
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
