using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using Object = UnityEngine.Object;

namespace GrygTools.AddressableUtils
{
    public class AddressableController : MbSingleton<AddressableController>
    {
	    private readonly Dictionary<string, AsyncOperationHandle> locationToHandle = new();
	    private readonly Dictionary<AssetReference, string> referenceToLocation = new();
	    private readonly Dictionary<string, List<IAddressableCallback>> activeLoadCallbacks = new();
	    
	    public void LoadAssetReferenceAsync(AssetReference assetReference, Action<object> onComplete = null)
	    {
		    LoadAssetReferenceAsync<Object>(assetReference, onComplete);
	    }
	    
	    public void LoadAssetReferenceAsync<T>(AssetReference assetReference, Action<T> onComplete = null) where T : class
	    {
		    AsyncOperationHandle handle = GetHandleFromReference<T>(assetReference);
		    if (handle.IsValid() && handle.Result != null)
		    {
			    onComplete?.Invoke(handle.Result as T);
			    return;
		    }

		    if (!activeLoadCallbacks.TryGetValue(assetReference.AssetGUID, out List<IAddressableCallback> actionList))
		    {
			    actionList = new List<IAddressableCallback>(){new AddressableCallback<T>(onComplete)};
			    activeLoadCallbacks.Add(assetReference.AssetGUID, actionList);
			    InternalLoadAssetRef<T>(assetReference, (obj) =>
			    {
				    foreach (IAddressableCallback addressableCallback in actionList)
				    {
					    addressableCallback.InvokeLoadedCallback(obj);
				    }
				    activeLoadCallbacks.Remove(assetReference.AssetGUID);
			    });
		    }
		    else
		    {
			    actionList.Add(new AddressableCallback<T>(onComplete));
		    }
	    }
	    
	    public Object LoadAssetReference(AssetReference assetReference)
	    {
		    return assetReference is AssetReferenceSprite ? LoadAssetReference<Sprite>(assetReference) : LoadAssetReference<Object>(assetReference);
	    }
        
	    public T LoadAssetReference<T>(AssetReference assetReference) where T : class
	    {
		    AsyncOperationHandle handle = GetHandleFromReference<T>(assetReference);
		    if (handle.IsValid())
		    {
			    if (handle.Result != null)
			    {
				    return ReturnLogic(handle);
			    }
			    handle.WaitForCompletion();
		    }
		    else
		    {
			    InternalLoadAssetRef<T>(assetReference, null, false);
		    }

		    //Grab the handle again to make sure we have the updated instance after InternalLoadAssetRef
		    handle = GetHandleFromReference<T>(assetReference);
			
		    return ReturnLogic(handle);
		    
		    T ReturnLogic(AsyncOperationHandle returnHandle)
		    {
			    if (typeof(T).IsSubclassOf(typeof(Component)))
			    {
				    if (returnHandle.Result is GameObject go)
				    {
					    return go.GetComponent<T>();
				    }
				    Debug.LogError($"Asset is not a GameObject {assetReference}");
				    return null;
			    }
			    
			    if (!typeof(T).IsInterface)
			    {
				    return returnHandle.Result as T;
			    }
			    
			    switch (returnHandle.Result)
			    {
				    case GameObject go:
					    return go.GetComponent<T>();
				    case T typed:
					    return typed;
			    }
			    
			    Debug.LogError($"Could not handle {assetReference} as Type {typeof(T)}");
			    return null;
		    }
	    }
	    
	    public bool TryLoadAssetReference<T>(AssetReference assetReference, out T tObject) where T : class
	    {
		    tObject = null;
		    if (assetReference.RuntimeKeyIsValid())
		    {
			    T loadedObject = LoadAssetReference<T>(assetReference);
			    tObject = default;
			    if (loadedObject is T testObject)
			    {
				    tObject = testObject;
				    return true;
			    }
		    }

		    Debug.LogWarning($"Invalid asset reference");
		    return false;
	    }
	    
	    private void InternalLoadAssetRef<T>(AssetReference assetReference, Action<T> onComplete, bool isAsync = true) where T : class
		{
			void OnLocationsRetrieved(IList<string> locations)
			{
				if (locations.Count == 0)
				{
					Debug.LogError($"No resource location found for asset reference: {assetReference}");
					return;
				}
				
				if (!referenceToLocation.ContainsKey(assetReference))
				{
					referenceToLocation.Add(assetReference, locations[0]);
				}

				if (locationToHandle.TryGetValue(locations[0], out AsyncOperationHandle opHandle))
				{
					if (opHandle.IsValid() && opHandle is {Status: AsyncOperationStatus.Succeeded, Result: not null})
					{
						CompletionLogic(opHandle);
					}
					else
					{
						if(!activeLoadCallbacks.TryGetValue(assetReference.AssetGUID, out List<IAddressableCallback> callbacks))
						{
							callbacks = new List<IAddressableCallback>();
							activeLoadCallbacks.Add(assetReference.AssetGUID, callbacks);
						}
						callbacks.Add(new AddressableCallback<T>(onComplete));
					}

					return;
				}

				if (typeof(T).IsSubclassOf(typeof(Component)) || typeof(T).IsInterface)
				{
					opHandle = Addressables.LoadAssetAsync<Object>(assetReference);
				}
				else
				{
					opHandle = Addressables.LoadAssetAsync<T>(assetReference);
				}
				
				if (!locationToHandle.ContainsKey(locations[0]))
				{
					locationToHandle.Add(locations[0], opHandle);
				}

				opHandle.Completed += result =>
				{
					if (result.Status == AsyncOperationStatus.Succeeded)
					{
						CompletionLogic(result);
					}
					else
					{
						Debug.LogError($"Failed to load resource location {locations[0]}");
					}
				};
				
				if (!isAsync)
				{
					opHandle.WaitForCompletion();
				}
			}

			if (referenceToLocation.TryGetValue(assetReference, out string location))
			{
				OnLocationsRetrieved(new List<string>(){location});
			}
			else
			{
				GetResourceLocations(assetReference, OnLocationsRetrieved);
			}

			void CompletionLogic(AsyncOperationHandle opHandle)
			{
				if (typeof(T).IsSubclassOf(typeof(Component)))
				{
					if (opHandle.Result is GameObject go)
					{
						onComplete?.Invoke(go.GetComponent<T>());
						return;
					}

					Debug.LogError($"Asset is not a GameObject, cannot retrieve component. {assetReference}");
				}
				else if (typeof(T).IsInterface)
				{
					switch (opHandle.Result)
					{
						case GameObject go:
							onComplete?.Invoke(go.GetComponent<T>());	
							return;
						case T typed:
							onComplete?.Invoke(typed);
							return;
					}
					Debug.LogError($"Object is an interface but does not match type T {typeof(T)}");
				}
				onComplete?.Invoke(opHandle.Result as T);
			}
		}

	    public bool TryGetReferenceIfLoaded<T>(AssetReference assetReference, out T tObject) where T : class
	    {
		    tObject = null;
		    AsyncOperationHandle handle = GetHandleFromReference<T>(assetReference);
		    if (handle.IsValid())
		    {
			    if (handle.Result != null)
			    {
				    tObject = handle.Result as T;
				    return true;
			    }
		    }
		    return false;
	    }
	    
        private IEnumerator InternalLoadLabel(string label, Action onComplete, bool isAsync = true)
		{
			AsyncOperationHandle<IList<IResourceLocation>> labelOp = Addressables.LoadResourceLocationsAsync(label);
			bool locationsLoaded = false;
			int expectedLoadCount = 0;
			int completedLoads = 0;
			labelOp.Completed += labelLoadObj =>
			{
				expectedLoadCount = labelOp.Result.Count;
				locationsLoaded = true;
  
				void LoadComplete(AsyncOperationHandle<Object> handle)
				{
					completedLoads++;
				}
				
				foreach (IResourceLocation location in labelOp.Result)
				{
					if(locationToHandle.ContainsKey(location.PrimaryKey))
					{
						expectedLoadCount--;
						continue;
					}
					
					AsyncOperationHandle<Object> handle =
						Addressables.LoadAssetAsync<Object>(location);
					
					if (!locationToHandle.ContainsKey(location.PrimaryKey))
					{
						locationToHandle.Add(location.PrimaryKey, handle);
					}
  
					handle.Completed += LoadComplete;
  
					if (!isAsync)
					{
						handle.WaitForCompletion();
					}
				}
			};
  
			if (!isAsync)
			{
				labelOp.WaitForCompletion();
			}
  
			yield return new WaitUntil(() => locationsLoaded && (completedLoads >= expectedLoadCount || expectedLoadCount == 0));
			Addressables.Release(labelOp);
			onComplete?.Invoke();
		}
		
		private void GetResourceLocations(AssetReference assetReference, Action<IList<string>> onComplete)
		{
			AsyncOperationHandle<IList<IResourceLocation>> locationsOp = Addressables.LoadResourceLocationsAsync(assetReference);
			locationsOp.WaitForCompletion();
  
			if (locationsOp.Status == AsyncOperationStatus.Failed)
			{
				Debug.LogError($"Retrieving resource location failed for: {assetReference}");
			}
			
			List<string> locations = new List<string>();
			foreach (IResourceLocation resourceLocation in locationsOp.Result)
			{
				locations.Add(resourceLocation.PrimaryKey);
			}
			
			onComplete?.Invoke(locations);
			Addressables.Release(locationsOp);
		}
		
		private AsyncOperationHandle GetHandleFromReference<T>(AssetReference reference)
		{
			if (referenceToLocation.TryGetValue(reference, out string location))
			{
				if(locationToHandle.TryGetValue(location, out AsyncOperationHandle handle))
				{
					if (handle.IsValid())
					{
						return handle;
					}
					
					AsyncOperationHandle opHandle = Addressables.LoadAssetAsync<T>(reference);
					locationToHandle[location] = opHandle;
					return opHandle;
				}
			}
  
			return new AsyncOperationHandle();
		}
		
		public void ReleaseAssetReferences(List<AssetReference> assetReferences)
		{
			if (assetReferences == null)
			{
				return;
			}

			foreach (AssetReference assetReference in assetReferences)
			{
				ReleaseAssetReference(assetReference);
			}
		}
		
		public void ReleaseAssetReference(AssetReference assetReference)
		{
			if (assetReference == null)
			{
				return;
			}
			
			//Only unload the handle. The asset reference to resource location is low cost and will save needing to redo the async operation
			if (referenceToLocation.TryGetValue(assetReference, out string location))
			{
				if (locationToHandle.TryGetValue(location, out AsyncOperationHandle handle))
				{
					Addressables.Release(handle);
					locationToHandle.Remove(location);
				}
			}
		}
		
		private interface IAddressableCallback
		{
			void InvokeLoadedCallback(object loadedObject);
		}
  
		private class AddressableCallback<T> : IAddressableCallback where T : class
		{
			private Action<T> callBack;
  
			public AddressableCallback(Action<T> callBack)
			{
				this.callBack = callBack;
			}
  
			public void InvokeLoadedCallback(object loadedObject)
			{
				if (loadedObject is T castObject)
				{
					callBack?.Invoke(castObject);
					return;
				}
				Debug.LogError($"Loaded addressable object is not of type T: {typeof(T)}");
			}
		}
    }
}
