using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace MapReroll.Patches {
	/// <summary>
	/// Records the generation request used for each map. Parameters that cannot be
	/// safely replayed later are retained as provenance and block destructive rerolls.
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
		[HarmonyPrefix]
		public static void CaptureGenerationRecipe(
			IntVec3 mapSize,
			MapParent parent,
			MapGeneratorDef mapGenerator,
			IEnumerable<GenStepWithParams> extraGenStepDefs,
			Action<Map> extraInitBeforeContentGen,
			bool isPocketMap,
			bool stepDebugger,
			out MapGenerationRecipeCapture __state) {
			__state = new MapGenerationRecipeCapture {
				MapSize = mapSize,
				ParentRuntimeType = parent?.GetType().FullName,
				ParentDefName = parent?.def?.defName,
				HasExtraGenSteps = HasAnyExtraGenSteps(extraGenStepDefs),
				HasPreContentCallback = extraInitBeforeContentGen != null,
				IsPocketMap = isPocketMap,
				StepDebugger = stepDebugger
			};
		}

		[HarmonyPostfix]
		public static void RecordUsedMapGenerator(Map __result, MapGeneratorDef mapGenerator, MapGenerationRecipeCapture __state) {
			if (__result != null) {
				MapRerollController.Instance?.OnMapGenerated(__result, __result.generatorDef ?? mapGenerator, __state);
			}
		}

		private static bool HasAnyExtraGenSteps(IEnumerable<GenStepWithParams> extraGenStepDefs) {
			if (extraGenStepDefs == null) return false;
			if (extraGenStepDefs is ICollection<GenStepWithParams> collection) {
				return collection.Count > 0;
			}
			if (extraGenStepDefs is IReadOnlyCollection<GenStepWithParams> readOnlyCollection) {
				return readOnlyCollection.Count > 0;
			}
			if (extraGenStepDefs is ICollection nonGenericCollection) {
				return nonGenericCollection.Count > 0;
			}

			// Enumerating an unknown sequence here could consume a one-shot mod iterator.
			return true;
		}
	}

	internal sealed class MapGenerationRecipeCapture {
		public IntVec3 MapSize;
		public string ParentRuntimeType;
		public string ParentDefName;
		public bool HasExtraGenSteps;
		public bool HasPreContentCallback;
		public bool IsPocketMap;
		public bool StepDebugger;
	}
}
