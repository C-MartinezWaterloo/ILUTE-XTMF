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
using System.Text;
using System.Threading.Tasks;
using TMG.Input;
using XTMF;

namespace TMG.Ilute.Model.Utilities;

public enum TemporalInputGranularity
{
    Monthly,
    Yearly
}

[ModuleInformation(Description = @"This module is designed to load in information for ILUTE in the format (Year/(year*12+month)) TAB (Data).
    When using the module as a data source everything is converted into months.")]

public class TemporalDataLoader : IDataSource<SparseArray<float>>
{
    [RootModule]
    public MultiYearTravelDemandModel Root;

    private SparseArray<float> _data;

    [SubModelInformation(Required = true, Description = "The location to load the temporal data from.")]
    public FileLocation LoadFrom;

    [RunParameter("Headers", false, "Does the data file contain headers?")]
    public bool Headers;

    [RunParameter("Ignore Data Outside of Simulation", false, "Set this to true to ignore data outside of the model's time-frame. Data past the end of the simulation stops the read so it can be used if the period is extended.")]
    public bool IgnoreDataOutsideOfSimulation;

    [RunParameter("Input Granularity", TemporalInputGranularity.Monthly, "Set to Monthly if the first column is months, Yearly if it is years.")]
    public TemporalInputGranularity InputGranularity;

    public string Name { get; set; }

    public float Progress { get; set; }

    public Tuple<byte, byte, byte> ProgressColour { get { return new Tuple<byte, byte, byte>(50, 150, 50); } }

    public bool Loaded { get; set; }


    public bool RuntimeValidation(ref string error)
    {
        return true;
    }

    public SparseArray<float> GiveData()
    {
        return _data;
    }

    private SparseArray<float> CreateBlankSparseArrayOfMonthData()
    {
        var StartYear = Root.StartYear;
        var startMonth = (StartYear - 1) * 12;
        var endMonth = StartYear*12 + Root.NumberOfYears * 12;
        return new SparseArray<float>(new SparseIndexing()
        {
            Indexes = new SparseSet[] { new SparseSet() { Start = startMonth, Stop = endMonth } }
        });
    }

    public void LoadData()
    {
        var data = CreateBlankSparseArrayOfMonthData();
        var startMonth = data.GetSparseIndex(0);
        var endMonth = startMonth + Root.NumberOfYears * 12;
        var flatData = data.GetFlatData();
        using (CsvReader reader = new CsvReader(LoadFrom))
        {
            int columns;
            if (Headers)
            {
                reader.LoadLine();
            }
            bool anyData = false;
            while (reader.LoadLine(out columns))
            {
                if (columns >= 2)
                {
                    bool year = InputGranularity == TemporalInputGranularity.Yearly;
                    int time;
                    float entry;
                    reader.Get(out time, 0);
                    reader.Get(out entry, 1);
                    if (year)
                    {
                        // convert year to month
                        time = time * 12;
                       
                    }
                    if (time < startMonth || time > endMonth)
                    {
                        if(IgnoreDataOutsideOfSimulation)
                        {
                            continue;
                        }
                        throw new XTMFRuntimeException(this, $"While loading data in '{Name}' we came across a month = '{time}' that isn't in the model's time-frame.");
                    }
                    if (year)
                    {
                        for (int i = 0; i < 12; i++)
                        {
                            flatData[time - startMonth + i] = entry;
                        }

                    }
                    else
                    {
                        flatData[time - startMonth] = entry;
                    }
                    anyData = true;
                }
            }
            if (!anyData)
            {
                throw new XTMFRuntimeException(this, $"While loading data in '{Name}' no valid entries were found in '{LoadFrom}'.");
            }
        }
        _data = data;
        Loaded = true;
    }

    public void UnloadData()
    {
        _data = null;
        Loaded = false;
    }
}
