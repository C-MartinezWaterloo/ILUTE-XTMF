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

        [SubModelInformation(Required = true, Description = "The average distance to Regional Transit by Zone")]
        public IDataSource<Repository<FloatData>> DistanceToRegionalTransit;

        [SubModelInformation(Required = true, Description = "The unemployment rate by zone.")]
        public IDataSource<Repository<FloatData>> UnemploymentByZone;

        [SubModelInformation(Required = true, Description = "The repository of all dwellings.")]
        public IDataSource<Repository<Dwelling>> Dwellings;


        private Repository<LandUse> _landUse;
        private Repository<FloatData> _distanceToSubway;
        private Repository<FloatData> _distanceToRegionalTransit;
        private Repository<FloatData> _unemployment;
        private Dictionary<int, float> _averageDwellingValueByZone;
        private CurrencyManager _currencyManager;

        private Date _currentDate;

        public string Name { get; set; }

        public float Progress => 0f;

        public Tuple<byte, byte, byte> ProgressColour => new Tuple<byte, byte, byte>(50, 150, 50);

        public void AfterMonthlyExecute(int currentYear, int month)
        {
            _averageDwellingValueByZone = null;
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
        }

        public void Execute(int currentYear, int month)
        {
            //TODO: Update all of the monthly rates / data here

            _currentDate = new Date(currentYear, month);

            // Loading the required repositories
            _landUse = Repository.GetRepository(LandUse);
            _distanceToSubway = Repository.GetRepository(DistanceToSubwayByZone);
            _unemployment = Repository.GetRepository(UnemploymentByZone);


            AverageDwellingValueByZone(new Date(currentYear, month));
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



        private (float askingPrice, float minimumBid) DwellingPrice(Dwelling seller)
        {
            var ctZone = seller.Zone;
            if (ctZone <= 0)
            {
                throw new XTMFRuntimeException(this, "Found a dwelling that is not linked to a zone!");
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
                case Dwelling.DwellingType.Detched:
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
                throw new XTMFRuntimeException(this, $"We were not able to find land use information for the zone {ctZone}");
            }

            // units are likely in $100,000 with the dollar likely from 2003-2004. This is based on the seller type data shown above.
            double price = 4.0312
                + 0.07625 * seller.Rooms
                - 0.0067 * avgDistToSubwayKM
                - 0.00163 * avgDistToRegionalTransitKM
                + 0.00016 * landUse.Residential
                - 0.00021 * landUse.Commerce
                /*- 0.00183 * myZone.UnEmplRate
                - 0.3746 * myZone.AvgPplPerRoom
                + 0.00151 * AvgCTDwellingValue
                + 0.00288 * AvgSalePriceForThisType
                - 0.00189 * myZone.AvgDaysListedOnMarket*/;
            return ((float)price, 0f);
        }

        public void RunFinished(int finalYear)
        {
        }

        public bool RuntimeValidation(ref string error)
        {
            return true;
        }
    }
}
