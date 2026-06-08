using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HugsLib;
using MapReroll.Compat;
using MapReroll.Promises;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MapReroll {
	/// <summary>
	/// Given a map location and seed, generates an approximate preview texture of how the map would look once generated.
	/// </summary>
	public class MapPreviewGenerator : IDisposable {
		private static readonly Color defaultTerrainColor = GenColor.FromHex("6D5B49");
		private static readonly Color missingTerrainColor = new Color(0.38f, 0.38f, 0.38f);
		private static readonly Color solidStoneColor = GenColor.FromHex("36271C");
		private static readonly Color solidStoneHighlightColor = GenColor.FromHex("4C3426");
		private static readonly Color solidStoneShadowColor = GenColor.FromHex("1C130E");
		private static readonly Color waterColorDeep = GenColor.FromHex("3A434D");
		private static readonly Color waterColorShallow = GenColor.FromHex("434F50");
		private static readonly Color caveColor = GenColor.FromHex("42372b");

		private static readonly Dictionary<string, Color> terrainColors = new Dictionary<string, Color> {
			{"Sand", GenColor.FromHex("806F54")},
			{"Soil", defaultTerrainColor},
			{"MarshyTerrain", GenColor.FromHex("3F412B")},
			{"SoilRich", GenColor.FromHex("42362A")},
			{"Gravel", defaultTerrainColor},
			{"Mud", GenColor.FromHex("403428")},
			{"Marsh", GenColor.FromHex("363D30")},
			{"MossyTerrain", defaultTerrainColor},
			{"Ice", GenColor.FromHex("9CA7AC")},
			{"WaterDeep", waterColorDeep},
			{"WaterOceanDeep", waterColorDeep},
			{"WaterMovingDeep", waterColorDeep},
			{"WaterShallow", waterColorShallow},
			{"WaterOceanShallow", waterColorShallow},
			{"WaterMovingShallow", waterColorShallow}
		};

		private readonly Queue<QueuedPreviewRequest> queuedRequests = new Queue<QueuedPreviewRequest>();
		private Thread workerThread;
		private EventWaitHandle workHandle = new AutoResetEvent(false);
		private EventWaitHandle disposeHandle = new AutoResetEvent(false);
		private EventWaitHandle mainThreadHandle = new AutoResetEvent(false);
		private bool disposed;

		public IPromise<Texture2D> QueuePreviewForSeed(string seed, int mapTile, int mapSize, bool revealCaves, MapGeneratorDef generatorDef = null) {
			if (disposeHandle == null) {
				throw new Exception("MapPreviewGenerator has already been disposed.");
			}
			var promise = new Promise<Texture2D>();
			generatorDef = generatorDef ?? Find.Maps.Find(m => m.Tile == mapTile)?.generatorDef ?? MapGeneratorDefOf.Base_Player;
			if (Compat_MapPreview.TryQueuePreviewForSeed(seed, mapTile, mapSize, generatorDef, promise, () => QueueLocalPreview(promise, seed, mapTile, mapSize, revealCaves))) {
				return promise;
			}
			QueueLocalPreview(promise, seed, mapTile, mapSize, revealCaves);
			return promise;
		}

		private void QueueLocalPreview(Promise<Texture2D> promise, string seed, int mapTile, int mapSize, bool revealCaves) {
			if (workerThread == null) {
				workerThread = new Thread(DoThreadWork);
				workerThread.Start();
			}
			queuedRequests.Enqueue(new QueuedPreviewRequest(promise, seed, mapTile, mapSize, revealCaves));
			workHandle.Set();
		}

		private void DoThreadWork() {
			QueuedPreviewRequest request = null;
			try {
				while (queuedRequests.Count > 0 || WaitHandle.WaitAny(new WaitHandle[] {workHandle, disposeHandle}) == 0) {
					Exception rejectException = null;
					if (queuedRequests.Count > 0) {
						var req = queuedRequests.Dequeue();
						request = req;
						Texture2D texture = null;
						int width = 0, height = 0;
						WaitForExecutionInMainThread(() => {
							// textures must be instantiated in the main thread
							texture = new Texture2D(req.MapSize, req.MapSize, TextureFormat.RGB24, false);
							width = texture.width;
							height = texture.height;
						});
						ThreadableTexture placeholderTex = null;
						try {
							if (texture == null) {
								throw new Exception("Could not create required texture.");
							}
							placeholderTex = new ThreadableTexture(width, height);
							GeneratePreviewForSeed(req.Seed, req.MapTile, req.MapSize, req.RevealCaves, placeholderTex);
						} catch (Exception e) {
							MapRerollController.Instance.Logger.Error("Failed to generate map preview: " + e);
							rejectException = e;
							texture = null;
						}
						if (texture != null && placeholderTex != null) {
							WaitForExecutionInMainThread(() => {
								// upload in main thread
								placeholderTex.CopyToTexture(texture);
								texture.Apply();
							});
						}
						WaitForExecutionInMainThread(() => {
							if (texture == null) {
								req.Promise.Reject(rejectException);
							} else {
								req.Promise.Resolve(texture);
							}
						});
					}
				}
				workHandle.Close();
				mainThreadHandle.Close();
				disposeHandle.Close();
				mainThreadHandle = disposeHandle = workHandle = null;
			} catch (Exception e) {
				MapRerollController.Instance.Logger.Error("Exception in preview generator thread: " + e);
				if (request != null) {
					request.Promise.Reject(e);
				}
			}
		}

		public void Dispose() {
			if (disposed) {
				throw new Exception("MapPreviewGenerator has already been disposed.");
			}
			disposed = true;
			queuedRequests.Clear();
			disposeHandle.Set();
		}

		/// <summary>
		/// The worker cannot be aborted- wait for the worker to complete before generating map
		/// </summary>
		public void WaitForDisposal() {
			if (!disposed || workerThread == null || !workerThread.IsAlive || workerThread.ThreadState == ThreadState.WaitSleepJoin) return;
			LongEventHandler.QueueLongEvent(() => workerThread.Join(60 * 1000), "Reroll2_finishingPreview", true, null);
		}

		/// <summary>
		/// Block until delegate is executed or times out
		/// </summary>
		private void WaitForExecutionInMainThread(Action action) {
			if (mainThreadHandle == null) return;
			HugsLibController.Instance.DoLater.DoNextUpdate(() => {
				action();
				mainThreadHandle.Set();
			});
			mainThreadHandle.WaitOne(1000);
		}

		private static void GeneratePreviewForSeed(string seed, int mapTile, int mapSize, bool revealCaves, ThreadableTexture texture) {
			var prevSeed = Find.World.info.seedString;

			try {
				MapRerollController.HasCavesOverride.HasCaves = Find.World.HasCaves(mapTile);
				MapRerollController.HasCavesOverride.OverrideEnabled = true;
                MapGeneratorDef generatorDef = Find.Maps.Find(m => m.Tile == mapTile).generatorDef;
				Find.World.info.seedString = seed;

				MapRerollController.RandStateStackCheckingPaused = true;
				var grids = GenerateMapGrids(mapTile, mapSize, revealCaves, generatorDef);
				var mapBounds = CellRect.WholeMap(grids.Map);
				foreach (var cell in mapBounds) {
					const float rockCutoff = .7f;
					var terrainDef = grids.Map.terrainGrid.TerrainAt(cell);
					if (!terrainColors.TryGetValue(terrainDef.defName, out Color pixelColor)) {
						pixelColor = missingTerrainColor;
					}
					if (grids.ElevationGrid[cell] > rockCutoff && !terrainDef.IsRiver && !terrainDef.IsWater) {
						pixelColor = solidStoneColor;
						if (revealCaves && grids.CavesGrid[cell] > 0) {
							pixelColor = caveColor;
						}
					}
					texture.SetPixel(cell.x, cell.z, pixelColor);
				}

				AddBevelToSolidStone(texture);

				foreach (var terrainPatchMaker in grids.Map.Biome.terrainPatchMakers) {
					terrainPatchMaker.Cleanup();
				}
			} finally {
				RockNoises.Reset();
				Find.World.info.seedString = prevSeed;
				MapRerollController.RandStateStackCheckingPaused = false;
				MapRerollController.HasCavesOverride.OverrideEnabled = false;
			}
		}

		/// <summary>
		/// Adds highlights and shadows to the solid stone color in the texture
		/// </summary>
		private static void AddBevelToSolidStone(ThreadableTexture tex) {
			for (int x = 0; x < tex.width; x++) {
				for (int y = 0; y < tex.height; y++) {
					var isStone = tex.GetPixel(x, y) == solidStoneColor;
					if (isStone) {
						var colorBelow = y > 0 ? tex.GetPixel(x, y - 1) : Color.clear;
						var isStoneBelow = colorBelow == solidStoneColor || colorBelow == solidStoneHighlightColor || colorBelow == solidStoneShadowColor;
						var isStoneAbove = y < tex.height - 1 && tex.GetPixel(x, y + 1) == solidStoneColor;
						if (!isStoneAbove) {
							tex.SetPixel(x, y, solidStoneHighlightColor);
						} else if (!isStoneBelow) {
							tex.SetPixel(x, y, solidStoneShadowColor);
						}
					}
				}
			}
		}

		/// <summary>
		/// Generate a minimal map with elevation and fertility grids
		/// </summary>
		private static MapGridSet GenerateMapGrids(int mapTile, int mapSize, bool revealCaves, MapGeneratorDef generatorDef) {
			try {
				Rand.PushState();
				var mapGeneratorData = (Dictionary<string, object>)ReflectionCache.MapGenerator_Data.GetValue(null);
				mapGeneratorData.Clear();

				var map = CreateMapStub(mapSize, mapTile, generatorDef);
				MapGenerator.mapBeingGenerated = map;
				
				var mapSeed = Gen.HashCombineInt(Find.World.info.Seed, map.Tile.GetHashCode());
				Rand.Seed = mapSeed;
				RockNoises.Init(map);
				foreach (var mutator in map.TileInfo.Mutators) {
					mutator.Worker?.Init(map);
				}

				var genSteps = GetOrderedGenStepsFor(map, generatorDef);
				for (int i = 0; i < genSteps.Count; i++) {
					if (!IsPreviewTerrainGenStep(genSteps[i].def.genStep)) continue;
					Rand.PushState();
					try {
						Rand.Seed = Gen.HashCombineInt(mapSeed, GetSeedPart(genSteps, i));
						genSteps[i].def.genStep.Generate(map, genSteps[i].parms);
					} finally {
						Rand.PopState();
					}
				}

				var result = new MapGridSet(MapGenerator.Elevation, MapGenerator.Fertility, MapGenerator.Caves, map);
				mapGeneratorData.Clear();

				return result;
			} finally {
				MapGenerator.mapBeingGenerated = null;
				try {
					Rand.PopState();
				} catch (InvalidOperationException e) {
					MapRerollController.Instance.Logger.Warning("Preview generation Rand stack was already empty: " + e.Message);
				}
			}
		}

		private static List<GenStepWithParams> GetOrderedGenStepsFor(Map map, MapGeneratorDef generatorDef) {
			var genSteps = generatorDef.genSteps.Where(IsValidBiome).Select(GetGenStepParams);
			foreach (var mutator in map.TileInfo.Mutators) {
				if (mutator.extraGenSteps.Any()) {
					genSteps = genSteps.Concat(mutator.extraGenSteps.Select(GetGenStepParams));
				}
			}
			if (map.Biome.extraGenSteps.Any()) {
				genSteps = genSteps.Concat(map.Biome.extraGenSteps.Where(IsValidBiome).Select(GetGenStepParams));
			}
			if (map.Biome.preventGenSteps.Any()) {
				genSteps = genSteps.Where(step => !map.Biome.preventGenSteps.Contains(step.def));
			}
			foreach (var mutator in map.TileInfo.Mutators) {
				if (mutator.preventGenSteps.Any()) {
					genSteps = genSteps.Where(step => !mutator.preventGenSteps.Contains(step.def));
				}
			}
			var orderedGenSteps = genSteps.Distinct()
				.OrderBy(step => step.def.order)
				.ThenBy(step => step.def.index)
				.ToList();
			orderedGenSteps.RemoveAll(a => orderedGenSteps.Any(b => b.def.preventsGenSteps != null && b.def.preventsGenSteps.Contains(a.def)));
			return orderedGenSteps;
		}

		private static GenStepWithParams GetGenStepParams(GenStepDef def) {
			return new GenStepWithParams(def, default(GenStepParams));
		}

		private static bool IsValidBiome(GenStepDef genStepDef) {
			return !Find.Scenario.AllParts.Any(p =>
				typeof(ScenPart_DisableMapGen).IsAssignableFrom(p.def.scenPartClass) &&
				p.def.genStep == genStepDef);
		}

		private static bool IsPreviewTerrainGenStep(GenStep genStep) {
			return genStep is GenStep_ElevationFertility ||
				genStep is GenStep_MutatorPostElevationFertility ||
				genStep is GenStep_Terrain ||
				genStep is GenStep_MutatorPostTerrain;
		}

		private static int GetSeedPart(List<GenStepWithParams> genSteps, int index) {
			var seedPart = genSteps[index].def.genStep.SeedPart;
			var duplicateOffset = 0;
			for (int i = 0; i < index; i++) {
				if (genSteps[i].def.genStep.SeedPart == seedPart) {
					duplicateOffset++;
				}
			}
			return seedPart + duplicateOffset;
		}

        /// <summary>
        /// Make an absolute bare minimum map instance for grid generation.
        /// </summary>
        private static Map CreateMapStub(int mapSize, int mapTile, MapGeneratorDef generatorDef) { 
            var parent = new MapParent {Tile = mapTile};
			var map = new Map {
				info = {
					parent = parent,
					Size = new IntVec3(mapSize, 1, mapSize)
				}
			};
            map.generatorDef = generatorDef;
			map.events = new MapEvents(map);
			map.components.Add(new MixedBiomeMapComponent(map));
            map.cellIndices = new CellIndices(map);
			map.floodFiller = new FloodFiller(map);
			map.waterInfo = new WaterInfo(map);
			map.thingGrid = new ThingGrid(map);
			map.edificeGrid = new EdificeGrid(map);
			map.terrainGrid = new TerrainGrid(map);
			map.roofGrid = new RoofGrid(map);
			map.mapDrawer = new MapDrawer(map);
			map.regionGrid = new RegionGrid(map);
			map.reachability = new Reachability(map);
			map.regionDirtyer = new RegionDirtyer(map);
			map.zoneManager = new ZoneManager(map);
			map.glowGrid = new GlowGrid(map);
			map.snowGrid = new SnowGrid(map);
			map.fertilityGrid = new FertilityGrid(map);
			map.designationManager = new DesignationManager(map);
			map.pathing = new Pathing(map);
			if (ModsConfig.OdysseyActive) {
				map.sandGrid = new SandGrid(map);
				map.substructureGrid = new SubstructureGrid(map);
			}
			return map;
		}

		private class MapGridSet {
			public readonly MapGenFloatGrid ElevationGrid;
			public readonly MapGenFloatGrid FertilityGrid;
			public readonly MapGenFloatGrid CavesGrid;
			public readonly Map Map;

			public MapGridSet(MapGenFloatGrid elevationGrid, MapGenFloatGrid fertilityGrid, MapGenFloatGrid cavesGrid, Map map) {
				ElevationGrid = elevationGrid;
				FertilityGrid = fertilityGrid;
				CavesGrid = cavesGrid;
				Map = map;
			}
		}

		private class QueuedPreviewRequest {
			public readonly Promise<Texture2D> Promise;
			public readonly string Seed;
			public readonly int MapTile;
			public readonly int MapSize;
			public readonly bool RevealCaves;

			public QueuedPreviewRequest(Promise<Texture2D> promise, string seed, int mapTile, int mapSize, bool revealCaves) {
				Promise = promise;
				Seed = seed;
				MapTile = mapTile;
				MapSize = mapSize;
				RevealCaves = revealCaves;
			}
		}

		// A placeholder for Texture2D that can be used in threads other than the main one (required since 1.0)
		private class ThreadableTexture {
			// pixels are laid out left to right, top to bottom
			private readonly Color[] pixels;
			public readonly int width;
			public readonly int height;

			public ThreadableTexture(int width, int height) {
				this.width = width;
				this.height = height;
				pixels = new Color[width * height];
			}

			public void SetPixel(int x, int y, Color color) {
				pixels[y * height + x] = color;
			}

			public Color GetPixel(int x, int y) {
				return pixels[y * height + x];
			}

			public void CopyToTexture(Texture2D tex) {
				tex.SetPixels(pixels);
			}
		}
	}
}
