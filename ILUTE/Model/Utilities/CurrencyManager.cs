/*
    Copyright 2016-2018 Travel Modelling Group, Department of Civil Engineering, University of Toronto

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
using Datastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using TMG.Ilute.Data;
using XTMF;

namespace TMG.Ilute.Model.Utilities
{
    [ModuleInformation(Description = "This module is designed to help convert money between years.")]

    public sealed class CurrencyManager : IDataSource<CurrencyManager>
    {

        // Inflation rates can be provided for each month using a TemporalDataLoader. The array index corresponds to the number of months since year zero.
        [SubModelInformation(Required = false, Description = "Inflation rate by month.")]
        public IDataSource<SparseArray<float>>? TemperalDataLoader;

        private SparseArray<float>? _inflationRateByMonth;

        public bool Loaded { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// Convert the money to a value for the given month.
        /// </summary>
        /// <param name="money">The original money object</param>
        /// <param name="date">The month (as a Date) to convert it to.</param>
        /// <returns>A new money object for the given date</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]


        public Money ConvertToDate(Money money, Date date)
        {
            // If no inflation data is loaded, return the original amount for the target year
            if (_inflationRateByMonth == null)
            {
                return new Money(money.Amount, date);
            }

            if (GetRate(date) == 0 || GetRate(money.WhenCreated) == 0)
            {
                throw new XTMFRuntimeException(this, "Inflation rate is zero for one of the dates, which would cause a divide-by-zero error.");
            }

            return new Money(
    money.Amount * GetRate(date) / GetRate(money.WhenCreated),
    date
); ;
        }

        /// <summary>
        /// Compatibility wrapper for older modules that convert by year.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Money ConvertToYear(Money money, Date date)
        {
            return ConvertToDate(money, date);
        }

        /// <summary>
        /// Get the inflation rate for the given date.
        /// </summary>
        /// <param name="date">The date to get the rate for</param>
        /// <returns>The inflation rate for the given year.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]

        private float GetRate(Date date)
        {
            // When inflation data is unavailable, treat the rate as 1
            if (_inflationRateByMonth == null)
            {
                return 1f;
            }

            return _inflationRateByMonth[date.Months];
        }

        public float Progress => 0f;

        public Tuple<byte, byte, byte> ProgressColour => new Tuple<byte, byte, byte>(50, 150, 50);

        public CurrencyManager GiveData()
        {
            return this;
        }

        public void LoadData()
        {
            if (TemperalDataLoader == null)
            {
                throw new XTMFRuntimeException(this, "CurrencyManager requires a TemporalDataLoader to supply inflation data.");
            }

            _inflationRateByMonth = Repository.GetRepository(TemperalDataLoader);

            if (_inflationRateByMonth == null)
            {
                throw new XTMFRuntimeException(this, "Unable to load inflation data from TemporalDataLoader.");
            }

            Loaded = true;
        }

        public bool RuntimeValidation(ref string error)
        {
            return true;
        }

        public void UnloadData()
        {
            Loaded = false;
            _inflationRateByMonth = null;
        }
    }
}
