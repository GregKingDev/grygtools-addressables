using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
namespace GrygTools.AddressableUtils
{
	public class AddressableManager : SingletonBase<AddressableManager>, ISingleton
	{
		private readonly Dictionary<AssetReference, string> referenceToLocation = new();
		private readonly Dictionary<string, AsyncOperationHandle> locationToHandles = new();
		
		public void Init()
		{
		}
		
		public bool TryGetIfLoaded<T>(AssetReference assetReference, out T asset) where T : class
		{
			string location = GetLocationFromAssetReferenceAsync(assetReference).Result;
			if (locationToHandles.TryGetValue(location, out AsyncOperationHandle handle))
			{
				asset = handle.Result as T;
				return asset != null;
			}
			asset = null;
			return false;
		}

#region Async Loading
		public async Task<T> LoadAssetReferenceAsync<T>(AssetReference assetReference) where T : class
		{
			string location = await GetLocationFromAssetReferenceAsync(assetReference);

			return await LoadAssetFromLocationAsync<T>(location);
		}
		
		public async Task<T> LoadAssetFromLocationAsync<T>(string location) where T : class
		{
			if (!locationToHandles.TryGetValue(location, out AsyncOperationHandle handle))
			{
				handle = Addressables.LoadAssetAsync<T>(location);
				locationToHandles.Add(location, handle);
			}
			
			if (handle.Status != AsyncOperationStatus.Succeeded)
			{
				await handle.Task;
			}
			
			if (handle.Status == AsyncOperationStatus.Succeeded)
			{
				if (handle.Result is T thing)
				{
					return thing;
				}
				Debug.LogError($"Asset at location {location} is not type {typeof(T)}");
				return null;
			}
			Debug.LogError($"Unable to load asset reference with location {location}");
			return null;
		}
		
		private async Task<string> GetLocationFromAssetReferenceAsync(AssetReference assetReference)
		{
			if(referenceToLocation.TryGetValue(assetReference, out string location))
			{
				return location;
			}
			var handle = Addressables.LoadResourceLocationsAsync(assetReference);
			await handle.Task;
			if (handle.Status == AsyncOperationStatus.Failed || handle.Result.Count == 0)
			{
				Debug.LogError($"Failed to load address for AssetReference with guid {assetReference.AssetGUID}");
				return null; 
			}
			referenceToLocation.Add(assetReference, handle.Result[0].PrimaryKey);
			return handle.Result[0].PrimaryKey;
		}
		
		public async Task LoadLabel(string label)
		{
			Debug.Log($"Loading label {label} ");
			var handle = Addressables.LoadResourceLocationsAsync(label);
			await handle.Task;

			if (handle.Status == AsyncOperationStatus.Succeeded)
			{
				List<Task<object>> tasks = new();
				
				foreach (IResourceLocation location in handle.Result)
				{
					tasks.Add(LoadAssetFromLocationAsync<object>(location.PrimaryKey));
				}
				await Task.WhenAll(tasks);
			}
			Debug.Log($"Loading label {label} completed");
		}
#endregion Async Loading
		
		
#region Synchronous Loading
		public bool TryLoadAssetReference<T>(AssetReference assetReference, out T tObject) where T : class
		{
			tObject = LoadAssetReference<T>(assetReference);
			return tObject != null;
		}
		
		public T LoadAssetReference<T>(AssetReference assetReference) where T : class
		{
			var location = GetLocationFromAssetReference(assetReference);
			return LoadAssetFromLocation<T>(location);
		}
		
		public T LoadAssetFromLocation<T>(string location) where T : class
		{
			if (!locationToHandles.TryGetValue(location, out AsyncOperationHandle handle))
			{
				handle = Addressables.LoadAssetAsync<T>(location);
				locationToHandles.Add(location, handle);
			}
			
			if (handle.Status != AsyncOperationStatus.Succeeded)
			{
				handle.WaitForCompletion();
			}
			
			if (handle.Status == AsyncOperationStatus.Succeeded)
			{
				if (handle.Result is T thing)
				{
					return thing;
				}
				Debug.LogError($"Asset at location {location} is not type {typeof(T)}");
				return null;
			}
			Debug.LogError($"Unable to load asset reference with location {location}");
			return null;
		}
		
		private string GetLocationFromAssetReference(AssetReference assetReference)
		{
			if(referenceToLocation.TryGetValue(assetReference, out string location))
			{
				return location;
			}
			var handle = Addressables.LoadResourceLocationsAsync(assetReference);
			handle.WaitForCompletion();
			if (handle.Status == AsyncOperationStatus.Failed || handle.Result.Count == 0)
			{
				Debug.LogError($"Failed to load address for AssetReference with guid {assetReference.AssetGUID}");
				return null; 
			}
			referenceToLocation.Add(assetReference, handle.Result[0].PrimaryKey);
			return handle.Result[0].PrimaryKey;
		}
#endregion Synchronous Loading

#region Release
		public void ReleaseAssetReference(AssetReference assetReference)
		{
			string location = GetLocationFromAssetReferenceAsync(assetReference).Result;
			if (locationToHandles.TryGetValue(location, out AsyncOperationHandle handle))
			{
				Addressables.Release(handle);
			}
		}
		
		public async Task ReleaseLabel(string label)
		{
			Debug.Log($"Releasing label {label}");
			var handle = Addressables.LoadResourceLocationsAsync(label);
			await handle.Task;

			if (handle.Status == AsyncOperationStatus.Succeeded)
			{
				for (int i = handle.Result.Count()-1; i >= 0; i--)
				{
					if (locationToHandles.TryGetValue(handle.Result[i].PrimaryKey, out AsyncOperationHandle assetHandle))
					{
						assetHandle.Release();
						locationToHandles.Remove(handle.Result[i].PrimaryKey);
					}
				}
			}
			Debug.Log($"Releasing label {label} completed");
		}
	}
#endregion Release
}
