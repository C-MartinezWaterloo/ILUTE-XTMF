/*
    Copyright 2025 Travel Modelling Group, Department of Civil Engineering, University of Toronto

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
using TMG.Ilute.Data;
using TMG.Ilute.Data.Housing;
using TMG.Ilute.Model.Utilities;
using TMG.Ilute.Model;
using XTMF;

namespace TMG.Ilute.Model.Housing
{
    /// <summary>
    /// Simple yearly process that increases the housing stock by creating new dwellings.
    /// </summary>
    public sealed class HousingSupply : IExecuteYearly, ICSVYearlySummary
    {
        [SubModelInformation(Required = true, Description = "Where dwellings are stored.")]
        public IDataSource<Repository<Dwelling>> DwellingRepository;

        [SubModelInformation(Required = false, Description = "Optional log output.")]
        public IDataSource<ExecutionLog> LogSource;

        [RunParameter("New Dwellings Per Year", 0, "How many additional dwellings to create each year.")]
        public int NewDwellingsPerYear;

        [RunParameter("Random Seed", 12345u, "Seed for dwelling generation randomness.")]
        public uint Seed;

        private RandomStream _rand;

        public string Name { get; set; }

        public float Progress { get; set; }

        public Tuple<byte, byte, byte> ProgressColour => new Tuple<byte, byte, byte>(50, 150, 50);

        private int _builtThisYear;

        public List<string> Headers => new List<string>() { "NewDwellings" };

        public List<float> YearlyResults => new List<float>() { _builtThisYear };

        public void AfterYearlyExecute(int currentYear)
        {
        }

        public void BeforeFirstYear(int firstYear)
        {
            RandomStream.CreateRandomStream(ref _rand, Seed);
        }

        public void BeforeYearlyExecute(int currentYear)
        {
            _builtThisYear = 0;
        }

        public void Execute(int currentYear)
        {
            if (NewDwellingsPerYear <= 0)
            {
                return;
            }

            var repo = Repository.GetRepository(DwellingRepository);

            _rand.ExecuteWithProvider(rand =>
            {
                float baseValue = 87000f + 50000f * Math.Max(0, currentYear - 1986);
                for (int i = 0; i < NewDwellingsPerYear; i++)
                {
                    var typeRoll = rand.NextFloat();
                    var type = PickType(typeRoll);
                    int rooms = PickRooms(type, rand);
                    int sqft = PickSquareFootage(rooms, rand);
                    int zone = (int)(rand.NextFloat() * 5); // simple zone spread

                    var d = new Dwelling
                    {
                        Exists = true,
                        Rooms = rooms,
                        SquareFootage = sqft,
                        Zone = zone,
                        Type = type,
                        Value = new Money(baseValue, new Date(currentYear, 0))
                    };

                    repo.AddNew(d);
                    _builtThisYear++;
                }
            });

            var log = Repository.GetRepository(LogSource);
            log?.WriteToLog($"Year {currentYear} constructed {_builtThisYear} dwellings.");
        }

        public void RunFinished(int finalYear)
        {
            _rand?.Dispose();
            _rand = null;
        }

        public bool RuntimeValidation(ref string error)
        {
            if (DwellingRepository == null)
            {
                error = Name + ": missing dwelling repository.";
                return false;
            }
            return true;
        }

        private static Dwelling.DwellingType PickType(float roll)
        {
            if (roll < 0.4f) return Dwelling.DwellingType.Detached;
            if (roll < 0.6f) return Dwelling.DwellingType.SemiDetached;
            if (roll < 0.8f) return Dwelling.DwellingType.Attached;
            if (roll < 0.95f) return Dwelling.DwellingType.ApartmentLow;
            return Dwelling.DwellingType.ApartmentHigh;
        }

        private static int PickRooms(Dwelling.DwellingType type, Rand rand)
        {
            switch (type)
            {
                case Dwelling.DwellingType.Detached:
                    return 4 + (int)(rand.NextFloat() * 3); // 4-6
                case Dwelling.DwellingType.SemiDetached:
                case Dwelling.DwellingType.Attached:
                    return 3 + (int)(rand.NextFloat() * 2); // 3-4
                case Dwelling.DwellingType.ApartmentLow:
                    return 2 + (int)(rand.NextFloat() * 2); // 2-3
                case Dwelling.DwellingType.ApartmentHigh:
                default:
                    return 1 + (int)(rand.NextFloat() * 2); // 1-2
            }
        }

        private static int PickSquareFootage(int rooms, Rand rand)
        {
            int min = rooms * 200;
            int max = rooms * 400;
            return min + (int)(rand.NextFloat() * (max - min));
        }
    }
}