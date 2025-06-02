
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
    // Based on the paper by Yicong Liu et al.
    public class SellerBehaviorModel
    {
        // This determines three possible ways a home can leave the market. (1) Sold (2) Withdrawn and (3) Relisted
        public enum Outcome { Sold, Withdrawn, Relisted }

        public Outcome EvaluateOutcome(Dwelling dwelling, float askingPrice, int monthsOnMarket, bool isRelisted, Rand rand)
        {
            // Compute probabilities for each outcome using logit model
            float pSold = 0;
            float pWithdraw =0;
            float pRelist = 0;

            // Sample one outcome using those probabilities
            float r = rand.NextFloat(); // returns a float between 0.0 and 1.0
            if (r < pSold) return Outcome.Sold;
            if (r < pSold + pWithdraw) return Outcome.Withdrawn;
            return Outcome.Relisted;
        }

        public float GetSurvivalProbability(Dwelling dwelling, int monthsOnMarket)
        {
            // Use a hazard function (e.g., exponential or Weibull)
            float lambda = ComputeHazardRate(dwelling);
            return (float)Math.Exp(-lambda * monthsOnMarket);
        }

        private float ComputeHazardRate(Dwelling dwelling)
        {
            // Use covariates: price, garage, etc.
            // λ = exp(Xβ)
            return MathF.Exp(...);
        }
    }

}
