using System;
using HarmonyLib;
using RimWorld;

namespace MapReroll.Patches {
	/// <summary>
	/// Replacing the starting map is not a real loss of the exostrider wreck. Let the
	/// normal Biotech GenStep create the wreck again on the selected replacement map.
	/// </summary>
	[HarmonyPatch(typeof(Building_AncientMechRemains), nameof(Building_AncientMechRemains.Notify_AbandonedAtTile))]
	internal static class Building_AncientMechRemains_Notify_AbandonedAtTile_Patch {
		[ThreadStatic]
		private static bool suppressAbandonmentNotification;

		internal static void DuringStartingMapReplacement(Action removeMap) {
			suppressAbandonmentNotification = true;
			try {
				removeMap();
			} finally {
				suppressAbandonmentNotification = false;
			}
		}

		[HarmonyPrefix]
		private static bool PreserveMechanitorOpportunity() {
			return !suppressAbandonmentNotification;
		}
	}
}
