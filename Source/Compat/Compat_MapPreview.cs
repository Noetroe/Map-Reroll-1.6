using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using HugsLib;
using MapReroll.Promises;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MapReroll.Compat {
	internal static class Compat_MapPreview {
		private const int MaxReadyWaitUpdates = 300;
		private const string MapPreviewApiTypeName = "MapPreview.MapPreviewAPI";
		private const string MapPreviewGeneratorTypeName = "MapPreview.MapPreviewGenerator";
		private const string MapPreviewRequestTypeName = "MapPreview.MapPreviewRequest";
		private const string MapPreviewResultTypeName = "MapPreview.MapPreviewResult";
		private const string SeedRerollDataTypeName = "MapPreview.SeedRerollData";

		private static Type apiType;
		private static Type generatorType;
		private static Type requestType;
		private static Type resultType;
		private static Type seedRerollDataType;
		private static ConstructorInfo requestCtor;
		private static MethodInfo generatorInitMethod;
		private static MethodInfo queuePreviewRequestMethod;
		private static MethodInfo copyToTextureMethod;
		private static MethodInfo getSeedRerollDataMethod;
		private static MethodInfo commitSeedMethod;
		private static FieldInfo resultPixelsField;
		private static PropertyInfo invalidCellsProperty;
		private static PropertyInfo isReadyProperty;
		private static PropertyInfo isGeneratingPreviewProperty;
		private static PropertyInfo textureSizeProperty;
		private static PropertyInfo useTrueTerrainColorsProperty;
		private static PropertyInfo useMinimalMapComponentsProperty;
		private static PropertyInfo generatorDefProperty;
		private static bool cacheValid;
		private static bool loggedBrokenApi;

		public static bool IsGeneratingPreview {
			get {
				if (!PrepareReflectionCache()) return false;
				return GetStaticBool(isGeneratingPreviewProperty);
			}
		}

		public static bool TryQueuePreviewForSeed(string seed, int mapTile, int mapSize, MapGeneratorDef generatorDef, Promise<Texture2D> promise, Action fallbackQueue) {
			if (!PrepareReflectionCache()) return false;

			QueuePreviewWhenReady(seed, mapTile, mapSize, generatorDef, promise, fallbackQueue, MaxReadyWaitUpdates);
			return true;
		}

		public static void CommitMapSeedForReroll(string seed, int mapTile) {
			if (!PrepareReflectionCache() || Current.Game?.World == null) return;

			try {
				var seedData = getSeedRerollDataMethod.Invoke(null, new object[] {Find.World});
				commitSeedMethod.Invoke(seedData, new object[] {mapTile, GetMapSeed(seed, mapTile), true});
			} catch (Exception e) {
				LogWarningOnce("Map Preview seed sync failed; Map Reroll may not match Map Preview's world-map seed. " + Unwrap(e));
			}
		}

		private static void QueuePreviewWhenReady(string seed, int mapTile, int mapSize, MapGeneratorDef generatorDef, Promise<Texture2D> promise, Action fallbackQueue, int remainingUpdates) {
			if (promise.CurState != PromiseState.Pending) return;

			if (GetStaticBool(isReadyProperty)) {
				if (!TryQueueReadyPreview(seed, mapTile, mapSize, generatorDef, promise, fallbackQueue)) {
					fallbackQueue();
				}
				return;
			}

			if (remainingUpdates <= 0) {
				LogWarningOnce("Map Preview was loaded but did not become ready for preview generation. Map Reroll will use its own previews.");
				fallbackQueue();
				return;
			}

			HugsLibController.Instance.DoLater.DoNextUpdate(() =>
				QueuePreviewWhenReady(seed, mapTile, mapSize, generatorDef, promise, fallbackQueue, remainingUpdates - 1)
			);
		}

		private static bool TryQueueReadyPreview(string seed, int mapTile, int mapSize, MapGeneratorDef generatorDef, Promise<Texture2D> promise, Action fallbackQueue) {
			try {
				var generator = generatorInitMethod.Invoke(null, null);
				if (generator == null) return false;

				var textureSize = new IntVec2(mapSize, mapSize);
				var request = requestCtor.Invoke(new object[] {GetMapSeed(seed, mapTile), (PlanetTile) mapTile, textureSize});
				textureSizeProperty.SetValue(request, textureSize, null);
				useTrueTerrainColorsProperty.SetValue(request, true, null);
				useMinimalMapComponentsProperty.SetValue(request, true, null);
				generatorDefProperty.SetValue(request, generatorDef ?? MapGeneratorDefOf.Base_Player, null);

				var mapPreviewPromise = queuePreviewRequestMethod.Invoke(generator, new[] {request});
				if (mapPreviewPromise == null) return false;

				var bridge = new PreviewPromiseBridge(promise, mapSize, copyToTextureMethod, resultPixelsField, invalidCellsProperty, fallbackQueue);
				var resolveDelegate = CreateResolveDelegate(bridge);
				var doneMethod = mapPreviewPromise.GetType().GetMethod(
					"Done",
					new[] {resolveDelegate.GetType(), typeof(Action<Exception>)}
				);
				if (doneMethod == null) return false;
				doneMethod.Invoke(mapPreviewPromise, new object[] {resolveDelegate, (Action<Exception>) bridge.Reject});
				return true;
			} catch (Exception e) {
				LogWarningOnce("Map Preview compatibility failed; falling back to Map Reroll previews. " + Unwrap(e));
				return false;
			}
		}

		private static bool PrepareReflectionCache() {
			if (cacheValid) return true;

			apiType = FindType(MapPreviewApiTypeName);
			generatorType = FindType(MapPreviewGeneratorTypeName);
			requestType = FindType(MapPreviewRequestTypeName);
			resultType = FindType(MapPreviewResultTypeName);
			seedRerollDataType = FindType(SeedRerollDataTypeName);
			if (apiType == null || generatorType == null || requestType == null || resultType == null || seedRerollDataType == null) {
				return false;
			}

			const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;
			const BindingFlags publicInstance = BindingFlags.Public | BindingFlags.Instance;
			isReadyProperty = apiType.GetProperty("IsReady", publicStatic);
			isGeneratingPreviewProperty = apiType.GetProperty("IsGeneratingPreview", publicStatic);
			generatorInitMethod = generatorType.GetMethod("Init", publicStatic, null, Type.EmptyTypes, null);
			queuePreviewRequestMethod = generatorType.GetMethod("QueuePreviewRequest", publicInstance, null, new[] {requestType}, null);
			requestCtor = requestType.GetConstructor(new[] {typeof(int), typeof(PlanetTile), typeof(IntVec2)});
			textureSizeProperty = requestType.GetProperty("TextureSize", publicInstance);
			useTrueTerrainColorsProperty = requestType.GetProperty("UseTrueTerrainColors", publicInstance);
			useMinimalMapComponentsProperty = requestType.GetProperty("UseMinimalMapComponents", publicInstance);
			generatorDefProperty = requestType.GetProperty("GeneratorDef", publicInstance);
			copyToTextureMethod = resultType.GetMethod("CopyToTexture", publicInstance, null, new[] {typeof(Texture2D)}, null);
			resultPixelsField = resultType.GetField("Pixels", publicInstance);
			invalidCellsProperty = resultType.GetProperty("InvalidCells", publicInstance);
			getSeedRerollDataMethod = seedRerollDataType.GetMethod("GetFor", publicStatic, null, new[] {typeof(World)}, null);
			commitSeedMethod = seedRerollDataType.GetMethod("Commit", publicInstance, null, new[] {typeof(int), typeof(int), typeof(bool)}, null);

			cacheValid = isReadyProperty != null
				&& isGeneratingPreviewProperty != null
				&& generatorInitMethod != null
				&& queuePreviewRequestMethod != null
				&& requestCtor != null
				&& textureSizeProperty != null
				&& useTrueTerrainColorsProperty != null
				&& useMinimalMapComponentsProperty != null
				&& generatorDefProperty != null
				&& copyToTextureMethod != null
				&& getSeedRerollDataMethod != null
				&& commitSeedMethod != null;
			if (!cacheValid) {
				LogWarningOnce("Map Preview is loaded, but its preview API shape was not recognized. Map Reroll will use its own previews.");
			}
			return cacheValid;
		}

		public static void SetExternalPreviewGenerationActive(bool active) {
			try {
				const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;
				apiType = apiType ?? FindType(MapPreviewApiTypeName);
				isGeneratingPreviewProperty = isGeneratingPreviewProperty ?? apiType?.GetProperty("IsGeneratingPreview", publicStatic);
				isGeneratingPreviewProperty?.GetSetMethod(true)?.Invoke(null, new object[] {active});
			} catch {
				// Optional compatibility signal only.
			}
		}

		private static Type FindType(string typeName) {
			return AppDomain.CurrentDomain.GetAssemblies()
				.Select(assembly => assembly.GetType(typeName, false))
				.FirstOrDefault(type => type != null);
		}

		private static bool GetStaticBool(PropertyInfo property) {
			try {
				return property != null && (bool) property.GetValue(null, null);
			} catch {
				return false;
			}
		}

		private static int GetMapSeed(string seed, int mapTile) {
			return Gen.HashCombineInt(GenText.StableStringHash(seed), mapTile.GetHashCode());
		}

		private static Delegate CreateResolveDelegate(PreviewPromiseBridge bridge) {
			var parameter = Expression.Parameter(resultType, "result");
			var body = Expression.Call(
				Expression.Constant(bridge),
				typeof(PreviewPromiseBridge).GetMethod(nameof(PreviewPromiseBridge.Resolve)),
				Expression.Convert(parameter, typeof(object))
			);
			return Expression.Lambda(typeof(Action<>).MakeGenericType(resultType), body, parameter).Compile();
		}

		private static void LogWarningOnce(string message) {
			if (loggedBrokenApi) return;
			loggedBrokenApi = true;
			MapRerollController.Instance?.Logger.Warning(message);
		}

		private static Exception Unwrap(Exception e) {
			return e is TargetInvocationException invocationException && invocationException.InnerException != null
				? invocationException.InnerException
				: e;
		}

		private class PreviewPromiseBridge {
			private readonly MethodInfo copyToTextureMethod;
			private readonly Action fallbackQueue;
			private readonly FieldInfo pixelsField;
			private readonly PropertyInfo invalidCellsProperty;
			private readonly int mapSize;
			private readonly Promise<Texture2D> promise;

			public PreviewPromiseBridge(Promise<Texture2D> promise, int mapSize, MethodInfo copyToTextureMethod, FieldInfo pixelsField, PropertyInfo invalidCellsProperty, Action fallbackQueue) {
				this.promise = promise;
				this.mapSize = mapSize;
				this.copyToTextureMethod = copyToTextureMethod;
				this.pixelsField = pixelsField;
				this.invalidCellsProperty = invalidCellsProperty;
				this.fallbackQueue = fallbackQueue;
			}

			public void Resolve(object result) {
				HugsLibController.Instance.DoLater.DoNextUpdate(() => {
					if (promise.CurState != PromiseState.Pending) return;
					try {
						if (ResultLooksBlank(result)) {
							FallbackOrReject(new Exception("Map Preview returned a blank preview texture."));
							return;
						}
						var texture = new Texture2D(mapSize, mapSize, TextureFormat.RGB24, false);
						copyToTextureMethod.Invoke(result, new object[] {texture});
						texture.Apply();
						promise.Resolve(texture);
					} catch (Exception e) {
						FallbackOrReject(Unwrap(e));
					}
				});
			}

			public void Reject(Exception e) {
				HugsLibController.Instance.DoLater.DoNextUpdate(() => FallbackOrReject(e));
			}

			private void FallbackOrReject(Exception e) {
				if (promise.CurState != PromiseState.Pending) return;
				if (fallbackQueue != null) {
					LogWarningOnce("Map Preview failed to generate a preview; Map Reroll will use its fallback preview generator. " + Unwrap(e).Message);
					fallbackQueue();
					return;
				}
				promise.Reject(e);
			}

			private bool ResultLooksBlank(object result) {
				try {
					if (invalidCellsProperty != null && (int)invalidCellsProperty.GetValue(result, null) > 0) {
						return true;
					}
					var pixels = pixelsField?.GetValue(result) as Color[];
					if (pixels == null || pixels.Length == 0) return false;
					for (int i = 0; i < pixels.Length; i++) {
						var pixel = pixels[i];
						if (pixel != Color.black && pixel != Color.clear) {
							return false;
						}
					}
					return true;
				} catch {
					return false;
				}
			}
		}
	}
}
