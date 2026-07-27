using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MapReroll {
	public enum MapRerollSafety {
		Safe,
		NoMap,
		NotPlayerHome,
		UnsupportedMapParent,
		UnsupportedGenerationRecipe,
		Committed
	}

	/// <summary>
	/// Central safety policy for destructive map operations. Other mods can explicitly
	/// register a custom MapParent type after confirming that it supports full rerolls.
	/// </summary>
	public static class MapRerollSafetyPolicy {
		private static readonly HashSet<Type> registeredSafeParentTypes = new HashSet<Type>();

		public static MapRerollSafety Evaluate(Map map) {
			if (map == null) return MapRerollSafety.NoMap;

			var mapState = map.GetComponent<MapComponent_MapRerollState>()?.State;
			if (mapState?.MapCommitted == true) return MapRerollSafety.Committed;

			var parent = map.Parent;
			if (parent != null) {
				foreach (var parentType in registeredSafeParentTypes) {
					if (parentType.IsInstanceOfType(parent)) {
						return MapRerollSafety.Safe;
					}
				}
			}

			// Preserve the compatibility behavior that predates this policy.
			if (ModLister.GetActiveModWithIdentifier("syrchalis.setupcamp") != null
				&& parent?.GetType().Name == "CaravanCamp") {
				return MapRerollSafety.Safe;
			}

			if (mapState?.GenerationRecipeCaptured == true
				&& (mapState.GenerationHadExtraGenSteps
					|| mapState.GenerationHadPreContentCallback
					|| mapState.GenerationWasPocketMap
					|| mapState.GenerationUsedStepDebugger)) {
				return MapRerollSafety.UnsupportedGenerationRecipe;
			}

			if (map.IsPlayerHome
				&& parent?.GetType() == typeof(Settlement)
				&& parent.def == WorldObjectDefOf.Settlement) {
				return MapRerollSafety.Safe;
			}

			return !map.IsPlayerHome
				? MapRerollSafety.NotPlayerHome
				: MapRerollSafety.UnsupportedMapParent;
		}

		public static bool CanReroll(Map map) {
			return Evaluate(map) == MapRerollSafety.Safe;
		}

		public static bool RegisterSafeMapParentType(Type mapParentType) {
			if (mapParentType == null || !typeof(MapParent).IsAssignableFrom(mapParentType)) {
				return false;
			}
			return registeredSafeParentTypes.Add(mapParentType);
		}

		public static bool UnregisterSafeMapParentType(Type mapParentType) {
			return mapParentType != null && registeredSafeParentTypes.Remove(mapParentType);
		}

		public static TaggedString GetRejectionMessage(MapRerollSafety safety) {
			switch (safety) {
				case MapRerollSafety.NoMap:
					return "Reroll2_unsafeMap_noMap".Translate();
				case MapRerollSafety.NotPlayerHome:
					return "Reroll2_unsafeMap_notPlayerHome".Translate();
				case MapRerollSafety.UnsupportedMapParent:
					return "Reroll2_unsafeMap_customParent".Translate();
				case MapRerollSafety.UnsupportedGenerationRecipe:
					return "Reroll2_unsafeMap_customRecipe".Translate();
				case MapRerollSafety.Committed:
					return "Reroll2_unsafeMap_committed".Translate();
				default:
					return TaggedString.Empty;
			}
		}

		internal static bool CheckAndNotify(Map map) {
			var safety = Evaluate(map);
			if (safety == MapRerollSafety.Safe) return true;

			var parentType = map?.Parent?.GetType().FullName ?? "<none>";
			var generator = map?.generatorDef?.defName ?? "<none>";
			var mapState = map?.GetComponent<MapComponent_MapRerollState>()?.State;
			MapRerollController.Instance?.Logger.Warning(
				$"Blocked unsafe reroll: safety={safety}, parent={parentType}, generator={generator}, "
				+ $"capturedParent={mapState?.GenerationParentRuntimeType}, parentDef={mapState?.GenerationParentDefName}, "
				+ $"mapSize={mapState?.GenerationMapSize}, "
				+ $"extraSteps={mapState?.GenerationHadExtraGenSteps}, callback={mapState?.GenerationHadPreContentCallback}, "
				+ $"pocketMap={mapState?.GenerationWasPocketMap}, stepDebugger={mapState?.GenerationUsedStepDebugger}");

			if (Current.ProgramState == ProgramState.Playing) {
				Messages.Message(GetRejectionMessage(safety), MessageTypeDefOf.RejectInput);
			}
			return false;
		}
	}
}
