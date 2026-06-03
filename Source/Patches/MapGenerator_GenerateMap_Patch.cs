using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace MapReroll.Patches {
	/// <summary>
	/// Records the generator used for maps so later rerolls can reuse the same generation path.
	/// </summary>
	[HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateMap))]
	[HarmonyPatch(new[] {
		typeof(IntVec3),
		typeof(MapParent),
		typeof(MapGeneratorDef),
		typeof(IEnumerable<GenStepWithParams>),
		typeof(Action<Map>),
		typeof(bool),
		typeof(bool)
	})]
	internal static class MapGenerator_GenerateMap_Patch {
		[HarmonyPostfix]
		public static void RecordUsedMapGenerator(Map __result, MapGeneratorDef mapGenerator) {
			if (__result != null && mapGenerator != null) {
				MapRerollController.Instance.OnMapGenerated(__result, mapGenerator);
			}
		}
	}
}
