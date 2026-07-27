using System;
using System.Collections.Generic;
using Verse;

namespace MapReroll
{
    [Serializable]
    class StarterPossesions : IExposable
    {
        public List<ThingDefCount> StartingPossessions { get => startingPossessions; set => startingPossessions = value; }
        private List<ThingDefCount> startingPossessions;
        public StarterPossesions()
        {
        }

        public void ExposeData()
        {
            startingPossessions = StartingPossessions;
            Scribe_Collections.Look(ref startingPossessions, "startingPossessions", LookMode.Deep);
        }
    }
}
