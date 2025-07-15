using TMG.Ilute.Data;
using TMG.Ilute.Data.Spatial;

namespace TMG.Ilute.Data.Housing
{
    /// <summary>
    /// Records information about a dwelling sale for hedonic regression.
    /// </summary>
    public sealed class SaleRecord : IndexedObject
    {
        public Date Date { get; set; }
        public float Price { get; set; }
        public int Rooms { get; set; }
        public int SquareFootage { get; set; }
        public int Zone { get; set; }
        public float DistSubway { get; set; }
        public float DistRegional { get; set; }
        public float Residential { get; set; }
        public float Commerce { get; set; }

        public Dwelling.DwellingType Type { get; set; }
    }
}