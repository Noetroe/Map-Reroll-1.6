using RimWorld.Planet;
using System.Collections.Generic;
using Verse;

namespace MapReroll {
	public class RerollWorldState : WorldComponent {
		public int StartingTile = -1;

        private Dictionary<Pawn, StarterPossesions> StartingPossessions = new Dictionary<Pawn, StarterPossesions>();
        private List<Pawn> pawns;
        private List<StarterPossesions> possesions;

        public void SetStartingPosssesions(Dictionary<Pawn, List<ThingDefCount>> sp)
        {
            foreach (KeyValuePair<Pawn, List<ThingDefCount>> entry in sp)
            {
                StarterPossesions starterPossesions = new StarterPossesions();
                starterPossesions.StartingPossessions = entry.Value;
                StartingPossessions[entry.Key] = starterPossesions;
            }
        }

        public Dictionary<Pawn, List<ThingDefCount>> GetStartingPossessions()
        {
            Dictionary<Pawn, List<ThingDefCount>> res = new Dictionary<Pawn, List<ThingDefCount>>();
            foreach (KeyValuePair<Pawn, StarterPossesions> entry in StartingPossessions)
            {
                res[entry.Key] = entry.Value.StartingPossessions;
            }
            return res;
        }

        public Dictionary<Pawn, List<ThingDefCount>> WorkAround(List<Pawn> colonists)
        {
            // If there are no colonists return an empty dictionary
            if (colonists.Count == 0)
            {
                return new Dictionary<Pawn, List<ThingDefCount>>();
            }

            // Check if pawn label (must use Label not object) exists in StartingPossessions pawns label (also must use label), if not remove it from StartingPossessions
            List<Pawn> toRemove = new List<Pawn>();
            foreach (KeyValuePair<Pawn, StarterPossesions> entry in StartingPossessions)
            {
                if (!colonists.Exists(x => x.Label == entry.Key.Label))
                {
                    toRemove.Add(entry.Key);
                }
            }
            foreach(Pawn pawn in toRemove)
            {
                StartingPossessions.Remove(pawn);
            }

            // Create a dictionary with the colonists as keys and an empty list as value
            Dictionary<Pawn, List<ThingDefCount>> res = new Dictionary<Pawn, List<ThingDefCount>>();
            foreach (Pawn col in colonists)
            {
                res[col] = new List<ThingDefCount>();
            }

            // Create a list to store all the ThingDefCounts from all the StarterPossesions
            List<ThingDefCount> firstCol = new List<ThingDefCount>();
            foreach (KeyValuePair<Pawn, StarterPossesions> entry in StartingPossessions)
            {
                firstCol.AddRange(entry.Value.StartingPossessions);
            }

            // If firstCol contains more than 1 ThingDefCount with the same Label sum up the counts and remove the duplicates
            // Create a new list to store the unique ThingDefCounts
            List<ThingDefCount> uniqueList = new List<ThingDefCount>();

            // Iterate through the "firstCol" list
            foreach (ThingDefCount tdc in firstCol)
            {
                // Check if the current ThingDefCount's label already exists in the "uniqueList"
                ThingDefCount match = uniqueList.Find(x => x.Label == tdc.Label);

                // If a match is found, add the current ThingDefCount's count to the match's count
                if (match != null)
                {
                    match = match.WithCount(match.Count + tdc.Count);
                }
                else
                {
                    // If no match is found, add the current ThingDefCount to the "uniqueList"
                    uniqueList.Add(tdc);
                }
            }

            // Replace "firstCol" with the "uniqueList"
            firstCol = uniqueList;

            // Set the value of the first colonist to the "firstCol" list
            res[colonists[0]] = firstCol;

            // Remove the first colonist from the list of colonists
            StartingPossessions.Clear();
            SetStartingPosssesions(res);
            return res;
        }

        public void crosscheck(List<Pawn> pawns)
        {
            foreach (Pawn pawn in pawns)
            {
                Log.Message(StartingPossessions.ContainsKey(pawn).ToString());
            }
        }
        
        public bool StartingTileIsKnown {
			get { return StartingTile >= 0; }
		}

		public RerollWorldState(World world) : base(world) {
		}

		public override void ExposeData() {
			base.ExposeData();
			Scribe_Values.Look(ref StartingTile, "startingTile", -1);
            Scribe_Collections.Look(ref StartingPossessions, "startingPossessions", LookMode.Reference, LookMode.Deep, ref pawns, ref possesions);
        }
	}
}